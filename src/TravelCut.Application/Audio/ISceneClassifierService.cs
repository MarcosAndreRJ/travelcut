using TravelCut.Contracts.Dtos;

namespace TravelCut.Application.Audio;

public interface ISceneClassifierService
{
    SceneClassificationDto ClassifyScene(
        MediaDetailsDto mediaDetails,
        TranscriptDto? transcript,
        List<VadSilenceSegmentDto>? silences
    );
}
