using System.Collections.Concurrent;

namespace TravelCut.Mcp.Facade;

/// <summary>
/// Cache de idempotência em memória por (ferramenta, chave). Mesma chave + mesmos argumentos devolvem o resultado
/// já obtido sem re-executar; mesma chave com argumentos diferentes → IDEMPOTENCY_KEY_REUSED.
/// <para>
/// A mutação roda com o token do SERVIDOR (<c>serverToken</c>, normalmente <c>ApplicationStopping</c>), não com o token
/// do primeiro chamador: se ele cancelar, a execução continua e os demais chamadores recebem o resultado. Cada chamador
/// aguarda com o próprio token (<c>WaitAsync(ct)</c>); cancelar só interrompe a espera dele.
/// </para>
/// <para>
/// Falhas transitórias (ver <see cref="McpErrorCodes.IsTransient"/>), resultados marcados <see cref="McpOutcome.NoCache"/>
/// e exceções são removidos do cache, para nova tentativa.
/// Limite de <c>maxEntries</c> entradas: ao exceder, remove as mais antigas já concluídas (entradas em voo não são removidas).
/// Vida útil: processo do servidor (sem persistência; reiniciar o servidor limpa o cache).
/// </para>
/// </summary>
public sealed class McpIdempotencyCache
{
    public const int DefaultMaxEntries = 1000;

    private readonly ConcurrentDictionary<CacheKey, Entry> _entries = new();
    private readonly ConcurrentQueue<KeyValuePair<CacheKey, Entry>> _order = new();
    private readonly object _evictSync = new();
    private readonly CancellationToken _serverToken;
    private readonly int _maxEntries;

    /// <param name="serverToken">Token do servidor usado para executar as mutações (ex.: ApplicationStopping).</param>
    /// <param name="maxEntries">Limite de entradas no cache (≥ 1).</param>
    public McpIdempotencyCache(CancellationToken serverToken = default, int maxEntries = DefaultMaxEntries)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxEntries, 1);
        _serverToken = serverToken;
        _maxEntries = maxEntries;
    }

    /// <summary>Quantidade de entradas (diagnóstico e testes).</summary>
    public int Count => _entries.Count;

    /// <summary>Versão sem token do chamador (compatibilidade): o corpo recebe apenas o token do servidor.</summary>
    public Task<McpIdempotencyResult> RunAsync(
        string tool,
        string idempotencyKey,
        string argumentsHash,
        Func<Task<McpOutcome>> body) =>
        RunAsync(tool, idempotencyKey, argumentsHash, _ => body(), CancellationToken.None);

    public async Task<McpIdempotencyResult> RunAsync(
        string tool,
        string idempotencyKey,
        string argumentsHash,
        Func<CancellationToken, Task<McpOutcome>> body,
        CancellationToken callerToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(tool);
        ArgumentException.ThrowIfNullOrEmpty(idempotencyKey);
        ArgumentException.ThrowIfNullOrEmpty(argumentsHash);
        ArgumentNullException.ThrowIfNull(body);

        // Chamador já cancelado: não cria entrada nem inicia mutação.
        callerToken.ThrowIfCancellationRequested();

        var key = new CacheKey(tool, idempotencyKey);
        var candidate = new Entry(argumentsHash, () => body(_serverToken));
        var entry = _entries.GetOrAdd(key, candidate);
        if (ReferenceEquals(entry, candidate))
        {
            _order.Enqueue(new KeyValuePair<CacheKey, Entry>(key, entry));
            EvictCompletedIfNeeded();
        }

        if (!string.Equals(entry.ArgumentsHash, argumentsHash, StringComparison.Ordinal))
        {
            return new McpIdempotencyResult(
                McpOutcome.Error(McpErrorCodes.IdempotencyKeyReused,
                    "Esta chave de idempotência já foi usada com outros argumentos."),
                Reused: false);
        }

        // Só uma chamada inicia o corpo (o Lazy garante); as demais aguardam a mesma Task.
        bool executedHere = Interlocked.CompareExchange(ref entry.Claimed, 1, 0) == 0;

        McpOutcome outcome;
        try
        {
            outcome = await entry.Execution.Value.WaitAsync(callerToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (callerToken.IsCancellationRequested)
        {
            // Só o chamador desistiu: a execução segue no token do servidor e a entrada permanece.
            throw;
        }
        catch
        {
            Forget(key, entry);
            throw;
        }

        if (outcome.NoCache || McpErrorCodes.IsTransient(outcome.ErrorCode))
        {
            Forget(key, entry);
        }

        return new McpIdempotencyResult(outcome, Reused: !executedHere);
    }

    /// <summary>
    /// Remove a entrada (só se ainda for a mesma) e poda a fila de ordem: sem isso, cada remoção deixaria um item morto
    /// em <c>_order</c> e a fila cresceria sem limite com erros transitórios repetidos.
    /// </summary>
    private void Forget(CacheKey key, Entry entry)
    {
        _entries.TryRemove(new KeyValuePair<CacheKey, Entry>(key, entry));
        PruneOrderIfNeeded();
    }

    /// <summary>
    /// Quando a fila passa de max(2×limite, 2×entradas vivas), mantém só os itens cuja entrada ainda está no cache.
    /// Itens de entradas removidas são descartados; entradas vivas mantêm o seu item (criado antes de entrar na fila).
    /// </summary>
    private void PruneOrderIfNeeded()
    {
        if (_order.Count <= Math.Max(2 * _maxEntries, 2 * _entries.Count))
        {
            return;
        }

        lock (_evictSync)
        {
            int scans = _order.Count;
            for (int i = 0; i < scans && _order.TryDequeue(out var item); i++)
            {
                if (_entries.TryGetValue(item.Key, out var current) && ReferenceEquals(current, item.Value))
                {
                    _order.Enqueue(item);
                }
            }
        }
    }

    /// <summary>Remove as entradas mais antigas já concluídas até respeitar o limite. Em voo: mantidas.</summary>
    private void EvictCompletedIfNeeded()
    {
        lock (_evictSync)
        {
            int scans = _order.Count;
            while (_entries.Count > _maxEntries && scans-- > 0 && _order.TryDequeue(out var oldest))
            {
                if (!oldest.Value.IsCompleted)
                {
                    _order.Enqueue(oldest);
                    continue;
                }
                // TryRemove(par): remove só se ainda for a mesma entrada (chave pode ter sido recriada depois).
                _entries.TryRemove(oldest);
            }
        }
    }

    private readonly record struct CacheKey(string Tool, string IdempotencyKey);

    private sealed class Entry(string argumentsHash, Func<Task<McpOutcome>> body)
    {
        public string ArgumentsHash { get; } = argumentsHash;
        public int Claimed;
        public Lazy<Task<McpOutcome>> Execution { get; } =
            new(body, LazyThreadSafetyMode.ExecutionAndPublication);

        /// <summary>Concluída (com sucesso ou falha). Entrada cujo corpo nem chegou a iniciar conta como não concluída.</summary>
        public bool IsCompleted
        {
            get
            {
                if (!Execution.IsValueCreated) return false;
                try
                {
                    return Execution.Value.IsCompleted;
                }
                catch (Exception)
                {
                    return true;
                }
            }
        }
    }
}

public sealed record McpIdempotencyResult(McpOutcome Outcome, bool Reused);
