using TravelCut.Contracts.Dtos;

namespace TravelCut.Application.Audio;

public interface IPipelineJobManager
{
    // Falha com InvalidOperationException("PIPELINE_IN_PROGRESS") se já houver job ativo no projeto.
    Task<Guid> StartAsync(StartPipelineRequest request, CancellationToken ct = default);
    Task<PipelineStatusDto> GetStatusAsync(Guid projectId, CancellationToken ct = default);
    bool Cancel(Guid jobId);
    Task<AnalysisEnvironmentDto> GetEnvironmentAsync(CancellationToken ct = default);
    Task<MediaAnalysisDto?> GetMediaAnalysisAsync(Guid projectId, Guid mediaId, CancellationToken ct = default);
}
