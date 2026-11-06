using TravelCut.Contracts.Dtos;

namespace TravelCut.Application.Audio;

public record WhisperProbeResult(
    bool HasPython,
    bool? HasCuda,          // null = desconhecido
    string? Device,
    bool HasWhisper,
    string? Engine,         // reportado pelo worker; null se indisponível
    string DefaultModel,    // "large-v3"
    string StatusSummary
);

public interface IWhisperTranscriptionService
{
    Task<WhisperProbeResult> ProbeEnvironmentAsync(CancellationToken ct = default);
    Task<string?> FindPythonExecutableAsync(CancellationToken ct = default);

    // Nunca sintetiza texto: sem Python/Whisper/modelo retorna Status "unavailable" com Reason.
    Task<TranscriptionOutcome> TranscribeAsync(
        string mediaPath,
        Guid mediaId,
        string model = "large-v3",
        string language = "pt",
        CancellationToken ct = default
    );
}
