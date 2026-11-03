using Microsoft.Extensions.Logging;
using TravelCut.Application.Ports;
using TravelCut.Application.Services;
using TravelCut.Contracts.Dtos;
using TravelCut.Domain.Entities;
using TravelCut.Domain.Enums;

namespace TravelCut.Application.UseCases;

/// <summary>
/// media.getThumbnail: resolve o instante (padrão: 10% da duração quando conhecida, senão 1 s)
/// e delega ao IThumbnailService. Falhas viram status no DTO, não exceções.
/// </summary>
public class GetThumbnailUseCase
{
    public const double DefaultSeconds = 1.0;

    private readonly IProjectRepository _projectRepo;
    private readonly IMediaRepository _mediaRepo;
    private readonly IThumbnailService _thumbnails;
    private readonly ILogger<GetThumbnailUseCase> _logger;

    public GetThumbnailUseCase(
        IProjectRepository projectRepo,
        IMediaRepository mediaRepo,
        IThumbnailService thumbnails,
        ILogger<GetThumbnailUseCase> logger)
    {
        _projectRepo = projectRepo;
        _mediaRepo = mediaRepo;
        _thumbnails = thumbnails;
        _logger = logger;
    }

    public async Task<(bool Success, ThumbnailResponse? Response, string? ErrorCode, string? ErrorMessage)> ExecuteAsync(
        GetThumbnailRequest request,
        CancellationToken ct = default)
    {
        var project = await _projectRepo.GetByIdAsync(request.ProjectId, ct);
        if (project == null)
        {
            return (false, null, "PROJECT_NOT_FOUND", $"Projeto com ID {request.ProjectId} não foi encontrado.");
        }

        if (string.IsNullOrWhiteSpace(request.Path))
        {
            return (false, null, "INVALID_ARGUMENT", "Caminho da mídia não informado.");
        }

        string full;
        try
        {
            full = Path.GetFullPath(request.Path.Trim());
        }
        catch
        {
            return (false, null, "INVALID_PATH", "Caminho inválido.");
        }

        if (!File.Exists(full))
        {
            return (true, new ThumbnailResponse(ThumbnailStatus.Failed, null, "Arquivo não encontrado."), null, null);
        }

        var kind = MediaScannerService.ClassifyExtension(Path.GetExtension(full));
        if (kind is null or MediaKind.Audio or MediaKind.Other)
        {
            return (true, new ThumbnailResponse(ThumbnailStatus.NotSupported, null, "Este tipo de arquivo não gera miniatura visual."), null, null);
        }

        double atSeconds;
        if (kind == MediaKind.Image)
        {
            atSeconds = 0.0;
        }
        else if (request.AtSeconds is { } requested && double.IsFinite(requested) && requested >= 0)
        {
            atSeconds = requested;
        }
        else
        {
            atSeconds = await DefaultInstantAsync(project, full, ct);
        }

        var result = await _thumbnails.GenerateAsync(project.RootPath, full, atSeconds, ct);
        _logger.LogDebug("Miniatura de {File}: {Status}", Path.GetFileName(full), result.Status);

        return (true, new ThumbnailResponse(result.Status, result.DataUrl, result.Reason), null, null);
    }

    private async Task<double> DefaultInstantAsync(Project project, string fullPath, CancellationToken ct)
    {
        string relative = ImportPathRules.IsInsideDirectory(project.RootPath, fullPath)
            ? ImportPathRules.ToLibraryRelativePath(project.RootPath, fullPath)
            : string.Empty;

        if (relative.Length == 0)
            return DefaultSeconds;

        var asset = (await _mediaRepo.ListByProjectAsync(project.ProjectId, ct))
            .FirstOrDefault(a => string.Equals(a.RelativePath, relative, StringComparison.OrdinalIgnoreCase));

        var meta = asset?.Metadata;
        if (meta != null && meta.MetadataStatus == MediaMetadataStatus.Ok && meta.DurationSeconds > 0)
        {
            return meta.DurationSeconds * 0.10;
        }

        return DefaultSeconds;
    }
}
