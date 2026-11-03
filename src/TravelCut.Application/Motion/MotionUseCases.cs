using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using TravelCut.Application.Ports;
using TravelCut.Contracts.Dtos;

namespace TravelCut.Application.Motion;

/// <summary>
/// Manifesto do pedido gravado em 3-Trabalho/motion/&lt;renderId&gt;/request.json (escrita atômica).
/// <c>AdapterMode</c> registra o renderer ("mock" ou "hyperframes") que produziu o resultado; só ele decide reaproveitamento.
/// </summary>
public sealed record MotionStoredRequest(
    string SchemaVersion,
    string RenderId,
    string RequestHash,
    DateTime CreatedAtUtc,
    MotionRenderRequestDto Request,
    string? AdapterMode = null);

/// <summary>
/// Casos de uso de animações (F09.5). UI (IPC) e MCP chamam esta classe; não há rota paralela de render.
/// Regras:
/// - valida antes de qualquer escrita; pedido inválido não gera pasta nem manifesto;
/// - grava request.json e result.json atomicamente em 3-Trabalho/motion/&lt;renderId&gt;;
/// - idempotência: mesma chave e mesmo conteúdo reaproveitam SIMULATED/READY_REAL já gravado, somente se o
///   adaptador (adapterMode do manifesto) for o mesmo e, para READY_REAL, o output.mp4 existir e não estiver vazio;
///   mesma chave com conteúdo diferente = IDEMPOTENCY_KEY_REUSED; ERROR/UNAVAILABLE anteriores são refeitos;
/// - link/junção em qualquer segmento de 3-Trabalho/motion/&lt;renderId&gt; = PERMISSION_DENIED antes de escrever;
/// - um render por vez nesta instância (SemaphoreSlim), para não disputar CPU/GPU nem arquivos;
/// - entre processos, a operação <c>motion-render</c> (<see cref="IProjectOperationLock"/>) é adquirida com espera
///   curta antes de qualquer escrita em 3-Trabalho/motion; ocupada = OPERATION_IN_PROGRESS, sem render.
/// </summary>
public sealed class MotionUseCases
{
    public const string MockMode = "mock";
    public const string IdempotencyKeyReusedCode = "IDEMPOTENCY_KEY_REUSED";
    public const string StorageErrorCode = "STORAGE_ERROR";
    public const string RenderInternalErrorCode = "RENDER_INTERNAL_ERROR";
    public const string OperationInProgressCode = "OPERATION_IN_PROGRESS";

    /// <summary>Operação de trava entre processos dos renders de animação (ver <see cref="IProjectOperationLock"/>).</summary>
    public const string RenderOperation = "motion-render";

    public static readonly TimeSpan DefaultLockWait = TimeSpan.FromSeconds(2);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly IMotionRenderer _renderer;
    private readonly IProjectOperationLock _projectLock;
    private readonly TimeProvider _time;
    private readonly TimeSpan _lockWait;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public MotionUseCases(IMotionRenderer renderer, IProjectOperationLock projectLock, TimeProvider? time = null, TimeSpan? lockWait = null)
    {
        _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        _projectLock = projectLock ?? throw new ArgumentNullException(nameof(projectLock));
        _time = time ?? TimeProvider.System;
        _lockWait = lockWait ?? DefaultLockWait;
        ArgumentOutOfRangeException.ThrowIfLessThan(_lockWait, TimeSpan.Zero);
    }

    public string Mode => _renderer.Mode;

    public Task<MotionCapabilitiesDto> DetectCapabilitiesAsync(CancellationToken ct = default) =>
        _renderer.DetectCapabilitiesAsync(ct);

    /// <summary>Valida, grava o manifesto, executa (ou reaproveita) o render e grava result.json.</summary>
    public async Task<MotionRenderResultDto> RenderAsync(MotionRenderRequestDto request, string projectRoot, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        DateTime started = _time.GetUtcNow().UtcDateTime;

        var validation = MotionRequestValidator.Validate(request, projectRoot);
        if (!validation.IsValid)
            return Failure(validation.Code!, validation.Message!, started, manifestRelative: string.Empty);

        string renderId = MotionPaths.RenderIdFor(request.ProjectId, request.IdempotencyKey);
        string manifestRelative = MotionPaths.ManifestRelative(renderId);

        if (!MotionPaths.TryCreateContext(projectRoot, renderId, out var context) || context is null)
            return Failure(MotionRequestValidator.ProjectRootInvalidCode,
                "Pasta do projeto indisponível para o render.", started, string.Empty);

        // Junção ou link em 3-Trabalho/motion ou na pasta do render: recusa antes de qualquer leitura de escrita.
        if (MotionPaths.HasReparsePointBetween(projectRoot, context.OutputDirAbsolute))
            return Failure(MotionRequestValidator.PermissionDeniedCode,
                "Pasta de render em 3-Trabalho/motion contém link, junção ou atalho.", started, string.Empty);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        IAsyncDisposable? projectLock = null;
        try
        {
            try
            {
                projectLock = await _projectLock.TryAcquireAsync(projectRoot, RenderOperation, _lockWait, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return Failure(StorageErrorCode,
                    "Não foi possível obter a trava de render do projeto em 3-Trabalho.", started, string.Empty);
            }

            if (projectLock is null)
                return Failure(OperationInProgressCode,
                    "Outra operação de animação está em andamento neste projeto. Tente novamente em instantes.", started, string.Empty);

            string requestHash = ComputeRequestHash(request);
            string adapterMode = _renderer.Mode;

            try
            {
                if (File.Exists(context.ManifestAbsolutePath))
                {
                    var stored = ReadStoredRequest(context.ManifestAbsolutePath);
                    if (stored is null || !string.Equals(stored.RequestHash, requestHash, StringComparison.Ordinal))
                        return Failure(IdempotencyKeyReusedCode,
                            "A chave de idempotência já foi usada com outro pedido.", started, manifestRelative);

                    bool sameAdapter = string.Equals(stored.AdapterMode, adapterMode, StringComparison.Ordinal);
                    var previous = ReadStoredResult(context.ResultAbsolutePath);
                    if (sameAdapter && previous is not null && IsReusable(previous, context))
                        return previous;

                    if (!sameAdapter)
                    {
                        // Mesmo pedido, outro adaptador: o render será refeito e o manifesto passa a registrar o modo atual.
                        WriteJsonAtomic(context.ManifestAbsolutePath, stored with { AdapterMode = adapterMode });
                    }
                }
                else
                {
                    Directory.CreateDirectory(context.OutputDirAbsolute);
                    WriteJsonAtomic(context.ManifestAbsolutePath, new MotionStoredRequest(
                        MotionSchemaVersions.Request,
                        renderId,
                        requestHash,
                        started,
                        request,
                        adapterMode));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return Failure(StorageErrorCode,
                    "Não foi possível gravar o manifesto do render em 3-Trabalho/motion.", started, manifestRelative);
            }

            MotionRenderResultDto result;
            try
            {
                result = await _renderer.RenderAsync(request, context, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                return Failure(RenderInternalErrorCode,
                    "Falha interna do renderizador de animações.", started, manifestRelative);
            }

            result = result with { ManifestRelativePath = manifestRelative };

            try
            {
                WriteJsonAtomic(context.ResultAbsolutePath, result);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // O resultado segue válido para a chamada atual; sem result.json, o próximo pedido refaz o render.
            }

            return result;
        }
        finally
        {
            try
            {
                if (projectLock is not null)
                    await projectLock.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }
        }
    }

    /// <summary>Hash do conteúdo do pedido (sem a chave, que já compõe o renderId).</summary>
    public static string ComputeRequestHash(MotionRenderRequestDto request)
    {
        var sb = new StringBuilder();
        sb.Append(request.ProjectId.ToString("D")).Append('|')
          .Append(request.TemplateId).Append('|')
          .Append(request.DurationFrames).Append('|')
          .Append(request.FrameRateNum).Append('/').Append(request.FrameRateDen).Append('|')
          .Append(request.Width).Append('x').Append(request.Height).Append('|');

        if (request.Parameters is not null)
        {
            foreach (var kv in request.Parameters.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                sb.Append(kv.Key).Append('=').Append(kv.Value).Append('\n');
            }
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()))).ToLowerInvariant();
    }

    /// <summary>
    /// SIMULATED reaproveita sem arquivo. READY_REAL só é reaproveitado se o output.mp4 ainda existir, não estiver
    /// vazio e não passar por link; caso contrário o render é refeito. ERROR e UNAVAILABLE nunca são reaproveitados.
    /// </summary>
    private static bool IsReusable(MotionRenderResultDto previous, MotionRenderContext context) =>
        previous.Status switch
        {
            MotionRenderStatuses.Simulated => true,
            MotionRenderStatuses.ReadyReal => MotionPaths.IsCompleteOutputFile(context.ProjectRoot, context.OutputFileAbsolute),
            _ => false,
        };

    private MotionRenderResultDto Failure(string code, string message, DateTime started, string manifestRelative)
    {
        DateTime finished = _time.GetUtcNow().UtcDateTime;
        return new MotionRenderResultDto(
            Status: MotionRenderStatuses.Error,
            IsSimulated: _renderer.Mode == MockMode,
            OutputRelativePath: null,
            ManifestRelativePath: manifestRelative,
            ErrorCode: code,
            ErrorMessage: message,
            StartedAtUtc: started,
            FinishedAtUtc: finished);
    }

    private static MotionStoredRequest? ReadStoredRequest(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<MotionStoredRequest>(File.ReadAllText(path, Encoding.UTF8));
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static MotionRenderResultDto? ReadStoredResult(string path)
    {
        if (!File.Exists(path))
            return null;

        try
        {
            return JsonSerializer.Deserialize<MotionRenderResultDto>(File.ReadAllText(path, Encoding.UTF8));
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Grava em arquivo temporário na mesma pasta e troca pelo destino (substituição atômica no mesmo volume).</summary>
    private static void WriteJsonAtomic<T>(string path, T value)
    {
        string tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(tmp, JsonSerializer.Serialize(value, JsonOptions), new UTF8Encoding(false));
            File.Move(tmp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tmp))
            {
                try { File.Delete(tmp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
    }
}
