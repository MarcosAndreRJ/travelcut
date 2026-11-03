using Microsoft.Extensions.Logging;
using TravelCut.Application.Ports;
using TravelCut.Application.Services;
using TravelCut.Contracts.Dtos;
using TravelCut.Domain.Entities;
using TravelCut.Domain.Enums;

namespace TravelCut.Application.UseCases;

/// <summary>
/// media.inspectCandidates: expande caminhos e lê metadados (quando o ffprobe existe).
/// Valores que não estão "ok" viram null no DTO: nunca estimados.
/// </summary>
public class InspectCandidatesUseCase
{
    private const int MetadataConcurrency = 2;

    private readonly IProjectRepository _projectRepo;
    private readonly IMediaRepository _mediaRepo;
    private readonly IFfprobeExtractor _ffprobe;
    private readonly ILogger<InspectCandidatesUseCase> _logger;

    public InspectCandidatesUseCase(
        IProjectRepository projectRepo,
        IMediaRepository mediaRepo,
        IFfprobeExtractor ffprobe,
        ILogger<InspectCandidatesUseCase> logger)
    {
        _projectRepo = projectRepo;
        _mediaRepo = mediaRepo;
        _ffprobe = ffprobe;
        _logger = logger;
    }

    public async Task<(bool Success, InspectCandidatesResponse? Response, string? ErrorCode, string? ErrorMessage)> ExecuteAsync(
        InspectCandidatesRequest request,
        CancellationToken ct = default)
    {
        var project = await _projectRepo.GetByIdAsync(request.ProjectId, ct);
        if (project == null)
        {
            return (false, null, "PROJECT_NOT_FOUND", $"Projeto com ID {request.ProjectId} não foi encontrado.");
        }

        if (request.Paths == null || request.Paths.Count == 0)
        {
            return (false, null, "INVALID_ARGUMENT", "Nenhum caminho informado.");
        }

        var expansion = await Task.Run(() => MediaFileExpander.Expand(request.Paths, ImportPathRules.MaxFilesPerImport, ct), ct);

        var libraryPaths = (await _mediaRepo.ListByProjectAsync(project.ProjectId, ct))
            .Select(a => a.RelativePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        bool ffprobeAvailable = _ffprobe.IsFfprobeAvailable();
        using var gate = new SemaphoreSlim(MetadataConcurrency, MetadataConcurrency);

        var tasks = expansion.Files
            .Select(f => BuildCandidateAsync(f, project.RootPath, libraryPaths, ffprobeAvailable, gate, ct))
            .ToList();

        var items = await Task.WhenAll(tasks);

        _logger.LogInformation("Inspeção de candidatos: {Count} arquivo(s), ffprobe={Ffprobe}, truncado={Truncated}",
            items.Length, ffprobeAvailable, expansion.Truncated);

        var response = new InspectCandidatesResponse(
            Items: items,
            FfprobeAvailable: ffprobeAvailable,
            Truncated: expansion.Truncated,
            Errors: expansion.Errors.ToList());

        return (true, response, null, null);
    }

    private async Task<ImportCandidateDto> BuildCandidateAsync(
        ExpandedMediaFile file,
        string projectRoot,
        HashSet<string> libraryPaths,
        bool ffprobeAvailable,
        SemaphoreSlim gate,
        CancellationToken ct)
    {
        string status;
        ExtractedMediaInfo? info = null;

        if (!ffprobeAvailable)
        {
            status = MediaMetadataStatus.FfprobeUnavailable;
        }
        else
        {
            await gate.WaitAsync(ct);
            try
            {
                info = await _ffprobe.ExtractMetadataAsync(file.FullPath, ct);
                status = info.MetadataStatus;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Falha ao analisar metadados de {File}", file.FileName);
                status = MediaMetadataStatus.Failed;
            }
            finally
            {
                gate.Release();
            }
        }

        bool ok = status == MediaMetadataStatus.Ok && info != null;
        bool insideProject = ImportPathRules.IsInsideDirectory(projectRoot, file.FullPath);
        bool inLibrary = insideProject
            && libraryPaths.Contains(ImportPathRules.ToLibraryRelativePath(projectRoot, file.FullPath));

        return new ImportCandidateDto(
            FullPath: file.FullPath,
            FileName: file.FileName,
            OriginFolder: file.OriginFolder,
            Kind: ListDirectoryUseCase.KindName(file.Kind),
            Container: file.Container,
            ByteLength: file.ByteLength,
            DurationSeconds: ok && info!.DurationSeconds > 0 ? info.DurationSeconds : null,
            Width: ok && info!.Width > 0 ? info.Width : null,
            Height: ok && info!.Height > 0 ? info.Height : null,
            Codec: ok && info!.Codec != "unknown" ? info.Codec : null,
            MetadataStatus: status,
            InsideProject: insideProject,
            AlreadyInLibrary: inLibrary);
    }
}
