using Microsoft.Extensions.Logging;
using TravelCut.Application.Ports;
using TravelCut.Application.Services;
using TravelCut.Contracts.Dtos;
using TravelCut.Domain.Entities;

namespace TravelCut.Application.UseCases;

public class ScanMediaLibraryUseCase
{
    private readonly IProjectRepository _projectRepo;
    private readonly IMediaRepository _mediaRepo;
    private readonly IMediaScannerService _scannerService;
    private readonly IFfprobeExtractor _metadataExtractor;
    private readonly ILogger<ScanMediaLibraryUseCase> _logger;

    public ScanMediaLibraryUseCase(
        IProjectRepository projectRepo,
        IMediaRepository mediaRepo,
        IMediaScannerService scannerService,
        IFfprobeExtractor metadataExtractor,
        ILogger<ScanMediaLibraryUseCase> logger)
    {
        _projectRepo = projectRepo;
        _mediaRepo = mediaRepo;
        _scannerService = scannerService;
        _metadataExtractor = metadataExtractor;
        _logger = logger;
    }

    /// <summary>Varredura completa da raiz do projeto (com análise de metadados).</summary>
    public async Task<(bool Success, ScanMediaResultDto? Result, string? ErrorCode, string? ErrorMessage)> ExecuteAsync(
        Guid projectId,
        bool computeFullSha = false,
        CancellationToken ct = default)
    {
        var project = await _projectRepo.GetByIdAsync(projectId, ct);
        if (project == null)
        {
            return (false, null, "PROJECT_NOT_FOUND", $"Projeto com ID {projectId} não foi encontrado.");
        }

        if (!Directory.Exists(project.RootPath))
        {
            return (false, null, "ROOT_NOT_FOUND", $"Diretório raiz do projeto não existe: {project.RootPath}");
        }

        _logger.LogInformation("Iniciando varredura de mídia para o projeto {ProjectId}", projectId);

        IReadOnlyList<DiscoveredMediaFile> discovered;
        try
        {
            discovered = _scannerService.ScanDirectory(project.RootPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao varrer diretório do projeto {ProjectId}", projectId);
            return (false, null, "SCAN_FAILED", $"Falha ao varrer diretório: {ex.Message}");
        }

        return await RegisterDiscoveredAsync(project, discovered, computeFullSha, analyzeMetadata: true, ct);
    }

    /// <summary>
    /// Registra na biblioteca apenas os arquivos informados (caminhos absolutos dentro da raiz do projeto).
    /// Usado pela importação. Com <paramref name="analyzeMetadata"/> = false, nenhum ffprobe é chamado
    /// e os metadados ficam com status "not_requested".
    /// </summary>
    public async Task<(bool Success, ScanMediaResultDto? Result, string? ErrorCode, string? ErrorMessage)> RegisterFilesAsync(
        Guid projectId,
        IReadOnlyList<string> fullPaths,
        bool analyzeMetadata,
        CancellationToken ct = default)
    {
        var project = await _projectRepo.GetByIdAsync(projectId, ct);
        if (project == null)
        {
            return (false, null, "PROJECT_NOT_FOUND", $"Projeto com ID {projectId} não foi encontrado.");
        }

        if (!Directory.Exists(project.RootPath))
        {
            return (false, null, "ROOT_NOT_FOUND", $"Diretório raiz do projeto não existe: {project.RootPath}");
        }

        var discovered = new List<DiscoveredMediaFile>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in fullPaths ?? Array.Empty<string>())
        {
            var described = _scannerService.DescribeFile(project.RootPath, path);
            if (described != null && seen.Add(described.RelativePath))
            {
                discovered.Add(described);
            }
        }

        return await RegisterDiscoveredAsync(project, discovered, computeFullSha: false, analyzeMetadata, ct);
    }

    private async Task<(bool Success, ScanMediaResultDto? Result, string? ErrorCode, string? ErrorMessage)> RegisterDiscoveredAsync(
        Project project,
        IReadOnlyList<DiscoveredMediaFile> discovered,
        bool computeFullSha,
        bool analyzeMetadata,
        CancellationToken ct)
    {
        var projectId = project.ProjectId;
        var existingAssets = await _mediaRepo.ListByProjectAsync(projectId, ct);
        var existingByRelative = existingAssets.ToDictionary(a => a.RelativePath, StringComparer.OrdinalIgnoreCase);

        int newCount = 0;
        int updatedCount = 0;
        int unchangedCount = 0;
        long totalBytes = 0;
        double totalDuration = 0;

        var assetsToSave = new List<MediaAsset>();

        foreach (var file in discovered)
        {
            // Defesa em profundidade: RelativePath com ".." ou rooted nunca entra na biblioteca.
            if (!ImportPathRules.IsSafeLibraryRelativePath(file.RelativePath))
            {
                _logger.LogWarning("Arquivo descoberto ignorado: caminho relativo inseguro para a biblioteca");
                continue;
            }

            totalBytes += file.ByteLength;
            bool isNew = !existingByRelative.TryGetValue(file.RelativePath, out var asset);

            if (!isNew && asset != null)
            {
                // Cache: mesmo tamanho/data e metadados já analisados (quando a análise é pedida).
                bool metadataUsable = asset.Metadata != null
                    && (!analyzeMetadata || asset.Metadata.MetadataStatus == MediaMetadataStatus.Ok);

                if (asset.ByteLength == file.ByteLength &&
                    Math.Abs((asset.LastWriteUtc - file.LastWriteTimeUtc).TotalSeconds) < 1.0 &&
                    metadataUsable)
                {
                    unchangedCount++;
                    // Total de duração: somente metadados "ok" (mídia sem metadados não tem duração conhecida).
                    if (asset.Metadata!.MetadataStatus == MediaMetadataStatus.Ok)
                        totalDuration += asset.Metadata.DurationSeconds;
                    assetsToSave.Add(asset);
                    continue;
                }
            }

            // Novo ou modificado: fingerprint e metadados
            string fingerprint = file.QuickFingerprint;
            if (computeFullSha)
            {
                try
                {
                    string sha = await _scannerService.ComputeSha256StreamingAsync(file.FullPath, ct);
                    fingerprint = $"sha256_{sha}";
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Não foi possível calcular SHA-256 para {File}, usando quick fingerprint", file.RelativePath);
                }
            }

            ExtractedMediaInfo metaInfo;
            if (analyzeMetadata)
            {
                try
                {
                    metaInfo = await _metadataExtractor.ExtractMetadataAsync(file.FullPath, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Falha na extração de metadados de {File}", file.RelativePath);
                    metaInfo = ExtractedMediaInfo.Unavailable(file.FullPath, MediaMetadataStatus.Failed);
                }
            }
            else
            {
                metaInfo = ExtractedMediaInfo.Unavailable(file.FullPath, MediaMetadataStatus.NotRequested);
            }

            Guid mediaId = isNew ? Guid.NewGuid() : asset!.MediaId;

            var newAsset = new MediaAsset
            {
                MediaId = mediaId,
                ProjectId = projectId,
                RelativePath = file.RelativePath,
                ByteLength = file.ByteLength,
                LastWriteUtc = file.LastWriteTimeUtc,
                Fingerprint = fingerprint,
                Kind = file.Kind,
                // Preserva tags e datas de descoberta/arquivamento ao atualizar um asset existente.
                Tags = isNew ? new List<string>() : new List<string>(asset!.Tags),
                DiscoveredAtUtc = isNew ? DateTime.UtcNow : asset!.DiscoveredAtUtc,
                IsArchived = !isNew && asset!.IsArchived,
                Metadata = new MediaMetadata
                {
                    MediaId = mediaId,
                    Codec = metaInfo.Codec,
                    Width = metaInfo.Width,
                    Height = metaInfo.Height,
                    TimeBase = metaInfo.TimeBase,
                    FrameRate = metaInfo.FrameRate,
                    DurationSeconds = metaInfo.DurationSeconds,
                    DurationTicks = metaInfo.DurationTicks,
                    AudioChannels = metaInfo.AudioChannels,
                    AudioSampleRate = metaInfo.AudioSampleRate,
                    CreationTimeUtc = metaInfo.CreationTimeUtc,
                    SourceDateEvidence = metaInfo.SourceDateEvidence,
                    IsVariableFrameRate = metaInfo.IsVariableFrameRate,
                    CameraModel = metaInfo.CameraModel,
                    GpsLocation = metaInfo.GpsLocation,
                    RotationDegrees = metaInfo.RotationDegrees,
                    MetadataStatus = metaInfo.MetadataStatus
                }
            };

            if (isNew)
            {
                newCount++;
            }
            else
            {
                updatedCount++;
            }

            if (metaInfo.MetadataStatus == MediaMetadataStatus.Ok)
                totalDuration += metaInfo.DurationSeconds;
            assetsToSave.Add(newAsset);
        }

        if (assetsToSave.Count > 0)
        {
            await _mediaRepo.SaveBatchAsync(assetsToSave, ct);
        }

        var result = new ScanMediaResultDto(
            TotalFilesFound: discovered.Count,
            NewAssetsAdded: newCount,
            UpdatedAssets: updatedCount,
            UnchangedAssets: unchangedCount,
            TotalBytes: totalBytes,
            DurationSecondsTotal: Math.Round(totalDuration, 2)
        );

        _logger.LogInformation("Registro de mídia concluído para {ProjectId}: {Found} arquivos ({New} novos, {Updated} atualizados, {Unchanged} mantidos)",
            projectId, discovered.Count, newCount, updatedCount, unchangedCount);

        return (true, result, null, null);
    }
}
