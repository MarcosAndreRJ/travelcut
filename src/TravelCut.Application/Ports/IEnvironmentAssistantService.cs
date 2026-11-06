using TravelCut.Contracts.Dtos;

namespace TravelCut.Application.Ports;

public interface IEnvironmentAssistantService
{
    Task<EnvironmentAssistantDiagnosticDto> DiagnoseAsync(CancellationToken ct = default);
    Task<ToolDiagnosticDto> DiagnoseToolAsync(string toolKey, CancellationToken ct = default);
    Task<EnvironmentInstallPlanDto> GetInstallPlanAsync(string toolKey, CancellationToken ct = default);
    Task<Guid> StartInstallAsync(StartToolInstallRequest request, CancellationToken ct = default);
    ToolInstallProgressDto? GetInstallProgress(Guid jobId);
    bool CancelInstall(Guid jobId);
    Task<Guid> StartDownloadModelAsync(StartModelDownloadRequest request, CancellationToken ct = default);
    ToolInstallProgressDto? GetModelDownloadProgress(Guid jobId);
    bool CancelModelDownload(Guid jobId);
    Task<TestToolResponse> TestToolAsync(string toolKey, CancellationToken ct = default);
    Task<bool> ConfigureToolPathAsync(string toolKey, string path, CancellationToken ct = default);
    Task<AutoLocateResponse> AutoLocateToolAsync(string toolKey, CancellationToken ct = default);
}
