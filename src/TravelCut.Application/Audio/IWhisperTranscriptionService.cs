using TravelCut.Contracts.Dtos;

namespace TravelCut.Application.Audio;

public record WhisperProbeResult(
    bool HasPython,
    bool? HasCuda,          // null = desconhecido
    string? Device,
    bool HasWhisper,
    string? Engine,         // reportado pelo worker; null se indisponível
    string DefaultModel,    // "large-v3"
    string StatusSummary,
    bool? ModelCached = null,   // modelo DefaultModel no cache local do worker; null = desconhecido (não bloqueia)
    bool? FfmpegOnPath = null   // ffmpeg visível no PATH do worker (o Whisper o invoca); null = desconhecido
);

/// <summary>
/// Telemetria real do worker. Stage: loading_audio | loading_model | transcribing.
/// Percent só existe quando o motor informou avanço; nunca é estimado por tempo.
/// </summary>
public sealed record TranscriptionProgressUpdate(string Stage, double? Percent);

/// <summary>Entrega o progresso na própria thread: Progress&lt;T&gt; enfileira e um relato tardio poderia sobrescrever o estado final.</summary>
public sealed class SynchronousProgress<T>(Action<T> handler) : IProgress<T>
{
    public void Report(T value) => handler(value);
}

/// <summary>Item de um lote de transcrição (P10). Progress/Execution recebem só a telemetria e o diagnóstico deste item.</summary>
public sealed record TranscriptionBatchItem(
    string MediaPath,
    Guid MediaId,
    IProgress<TranscriptionProgressUpdate>? Progress = null,
    IWhisperExecutionObserver? Execution = null);

public interface IWhisperTranscriptionService
{
    /// <summary>
    /// P10: transcreve várias mídias num único processo do worker, que carrega o modelo uma só vez. Devolve uma Task por item, na
    /// ordem dos itens; cada uma conclui assim que o worker responde aquele item (os itens seguem a ordem do lote). Nunca lança:
    /// falha de item vira outcome "failed"/"unavailable"/"canceled" (o chamador faz o recovery por item). Retorna null quando a
    /// implementação não suporta lote (o chamador usa <see cref="TranscribeAsync"/> mídia a mídia).
    /// </summary>
    IReadOnlyList<Task<TranscriptionOutcome>>? TranscribeBatch(
        IReadOnlyList<TranscriptionBatchItem> items, string model, string language, CancellationToken ct) => null;

    Task<WhisperProbeResult> ProbeEnvironmentAsync(CancellationToken ct = default);
    Task<string?> FindPythonExecutableAsync(CancellationToken ct = default);

    /// <summary>Descarta o probe em cache: a próxima consulta reflete instalação ou configuração feita agora.</summary>
    void InvalidateEnvironment() { }

    /// <summary>Incrementa a cada <see cref="InvalidateEnvironment"/>: consumidores com cache próprio detectam a invalidação.</summary>
    long EnvironmentGeneration => 0;

    // Nunca sintetiza texto: sem Python/Whisper/modelo retorna Status "unavailable" com Reason.
    Task<TranscriptionOutcome> TranscribeAsync(
        string mediaPath,
        Guid mediaId,
        string model = "large-v3",
        string language = "pt",
        CancellationToken ct = default,
        IProgress<TranscriptionProgressUpdate>? progress = null,
        IWhisperExecutionObserver? execution = null
    );

    /// <summary>
    /// Transcreve forçando o dispositivo (ex.: "cpu" no fallback após falta de memória da GPU, P12). O modelo nunca muda.
    /// Implementação padrão (sem controle de dispositivo): igual a <see cref="TranscribeAsync"/>.
    /// </summary>
    Task<TranscriptionOutcome> TranscribeOnDeviceAsync(
        string mediaPath,
        Guid mediaId,
        string model,
        string language,
        string device,
        CancellationToken ct = default,
        IProgress<TranscriptionProgressUpdate>? progress = null,
        IWhisperExecutionObserver? execution = null)
        => TranscribeAsync(mediaPath, mediaId, model, language, ct, progress, execution);
}
