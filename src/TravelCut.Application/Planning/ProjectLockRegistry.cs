namespace TravelCut.Application.Planning;

/// <summary>
/// Exclusão mútua por projeto sem vazamento: cada entrada conta quem a segura OU espera por ela e é removida quando o contador
/// zera, então a tabela só tem projetos com operação em andamento. A contagem e a remoção ocorrem sob o mesmo monitor da tabela,
/// logo ninguém pode adquirir um semáforo já removido (quem chega depois cria uma entrada nova, sem detentor nem espera na antiga).
/// </summary>
public sealed class ProjectLockRegistry
{
    private sealed class Entry
    {
        public readonly SemaphoreSlim Gate = new(1, 1);
        public int Refs;
    }

    private readonly Dictionary<Guid, Entry> _entries = new();

    /// <summary>Quantidade de projetos com lock em uso (detentor ou fila). Diagnóstico/testes.</summary>
    public int ActiveCount
    {
        get { lock (_entries) return _entries.Count; }
    }

    /// <summary>Adquire o lock do projeto. Cancelar a espera devolve a referência (a entrada ociosa é removida).</summary>
    public async Task<IDisposable> AcquireAsync(Guid projectId, CancellationToken ct)
    {
        Entry entry;
        lock (_entries)
        {
            if (!_entries.TryGetValue(projectId, out entry!)) _entries[projectId] = entry = new Entry();
            entry.Refs++;
        }

        try
        {
            await entry.Gate.WaitAsync(ct);
        }
        catch
        {
            Unref(projectId, entry);
            throw;
        }
        return new Releaser(this, projectId, entry);
    }

    private void Unref(Guid projectId, Entry entry)
    {
        lock (_entries)
        {
            if (--entry.Refs == 0) _entries.Remove(projectId);
        }
    }

    private sealed class Releaser(ProjectLockRegistry owner, Guid projectId, Entry entry) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) != 0) return;
            entry.Gate.Release();
            owner.Unref(projectId, entry);
        }
    }
}
