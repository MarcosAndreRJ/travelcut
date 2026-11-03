using TravelCut.Contracts.Dtos;

namespace TravelCut.Application.Ports;

public sealed record ImportStartResult(bool Success, Guid? JobId, string? ErrorCode, string? ErrorMessage);

/// <summary>
/// Importação de mídia para o projeto (Tela 07). A implementação roda jobs em background;
/// o estado é consultado por snapshot imutável.
/// </summary>
public interface IMediaImportService
{
    Task<ImportStartResult> StartAsync(StartImportRequest request, CancellationToken ct = default);
    ImportJobStatusDto? GetStatus(Guid jobId);
    CancelImportResponse Cancel(Guid jobId);
}
