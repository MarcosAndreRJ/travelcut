using TravelCut.Contracts.Dtos;

namespace TravelCut.Application.Audio;

public record CutCandidateGenerationOptions(
    double LongSilenceThresholdSeconds = 2.0,
    double ShortSilenceThresholdSeconds = 0.8,
    bool IncludeHesitations = true,
    bool PreserveHumorousPauses = true
);

public interface ICutCandidateService
{
    List<EditSuggestionDto> GenerateCutCandidates(
        Guid mediaId,
        TranscriptDto? transcript,
        List<VadSilenceSegmentDto> silences,
        List<HesitationCandidateDto> hesitations,
        CutCandidateGenerationOptions? options = null
    );
}
