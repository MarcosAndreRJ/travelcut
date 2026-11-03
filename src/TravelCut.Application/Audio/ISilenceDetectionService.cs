using TravelCut.Contracts.Dtos;

namespace TravelCut.Application.Audio;

public record SilenceDetectionOptions(
    double MinSilenceDurationSeconds = 0.6,
    double NoiseToleranceDb = -30.0
);

public interface ISilenceDetectionService
{
    bool IsFfmpegAvailable();
    string? GetFfmpegVersion();

    // Nunca estima silêncios: sem FFmpeg retorna Status "unavailable".
    Task<SilenceDetectionOutcome> DetectSilenceAsync(
        string mediaPath,
        SilenceDetectionOptions? options = null,
        CancellationToken ct = default
    );

    // Heurística lexical sobre a transcrição real (requer palavras com timestamps).
    List<HesitationCandidateDto> DetectHesitationsAndFillers(
        TranscriptDto transcript,
        IReadOnlyList<VadSilenceSegmentDto> silences
    );
}
