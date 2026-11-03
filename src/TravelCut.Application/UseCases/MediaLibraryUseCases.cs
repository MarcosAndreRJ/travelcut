using Microsoft.Extensions.Logging;
using TravelCut.Application.Ports;
using TravelCut.Application.Services;
using TravelCut.Contracts.Dtos;

namespace TravelCut.Application.UseCases;

public class ListMediaAssetsUseCase
{
    private readonly IMediaRepository _mediaRepo;

    public ListMediaAssetsUseCase(IMediaRepository mediaRepo)
    {
        _mediaRepo = mediaRepo;
    }

    public async Task<IReadOnlyList<MediaAssetDto>> ExecuteAsync(Guid projectId, CancellationToken ct = default)
    {
        var assets = await _mediaRepo.ListByProjectAsync(projectId, ct);

        return assets.Select(a => new MediaAssetDto(
            MediaId: a.MediaId,
            ProjectId: a.ProjectId,
            RelativePath: a.RelativePath,
            ByteLength: a.ByteLength,
            Kind: a.Kind.ToString(),
            Codec: a.Metadata?.Codec ?? "unknown",
            Width: a.Metadata?.Width ?? 0,
            Height: a.Metadata?.Height ?? 0,
            Fps: a.Metadata?.FrameRate ?? 0,
            DurationSeconds: a.Metadata?.DurationSeconds ?? 0,
            CreationTimeUtc: a.Metadata?.CreationTimeUtc,
            HasTranscript: false,
            HasVAD: false,
            SceneClassification: null,
            ThumbnailUrl: null,
            Tags: (a.Tags ?? new List<string>()).ToArray(),
            MetadataStatus: a.Metadata?.MetadataStatus ?? TravelCut.Domain.Entities.MediaMetadataStatus.NotRequested
        )).ToList();
    }
}

public class GetMediaDetailsUseCase
{
    private readonly IProjectRepository _projectRepo;
    private readonly IMediaRepository _mediaRepo;

    public GetMediaDetailsUseCase(IProjectRepository projectRepo, IMediaRepository mediaRepo)
    {
        _projectRepo = projectRepo;
        _mediaRepo = mediaRepo;
    }

    public async Task<(bool Success, MediaDetailsDto? Details, string? ErrorCode, string? ErrorMessage)> ExecuteAsync(
        Guid mediaId,
        CancellationToken ct = default)
    {
        var asset = await _mediaRepo.GetByIdAsync(mediaId, ct);
        if (asset == null)
        {
            return (false, null, "MEDIA_NOT_FOUND", $"Mídia com ID {mediaId} não foi encontrada.");
        }

        var project = await _projectRepo.GetByIdAsync(asset.ProjectId, ct);
        string rootPath = project?.RootPath ?? "";
        string fullPath = Path.Combine(rootPath, asset.RelativePath);

        var meta = asset.Metadata;

        var dto = new MediaDetailsDto(
            MediaId: asset.MediaId,
            ProjectId: asset.ProjectId,
            RelativePath: asset.RelativePath,
            FullPath: fullPath,
            ByteLength: asset.ByteLength,
            Kind: asset.Kind.ToString(),
            Fingerprint: asset.Fingerprint,
            LastWriteUtc: asset.LastWriteUtc,
            Codec: meta?.Codec ?? "unknown",
            Width: meta?.Width ?? 0,
            Height: meta?.Height ?? 0,
            FrameRate: meta?.FrameRate ?? 0,
            TimeBase: meta?.TimeBase.ToString() ?? "30:1",
            DurationSeconds: meta?.DurationSeconds ?? 0,
            DurationTicks: meta?.DurationTicks ?? 0,
            AudioChannels: meta?.AudioChannels ?? 0,
            AudioSampleRate: meta?.AudioSampleRate ?? 0,
            CreationTimeUtc: meta?.CreationTimeUtc,
            SourceDateEvidence: meta?.SourceDateEvidence ?? "",
            IsVariableFrameRate: meta?.IsVariableFrameRate ?? false
        );

        return (true, dto, null, null);
    }
}

public class GetMediaChronologyUseCase
{
    private readonly IMediaRepository _mediaRepo;
    private readonly IChronologyService _chronologyService;
    private readonly ILogger<GetMediaChronologyUseCase> _logger;

    public GetMediaChronologyUseCase(
        IMediaRepository mediaRepo,
        IChronologyService chronologyService,
        ILogger<GetMediaChronologyUseCase> logger)
    {
        _mediaRepo = mediaRepo;
        _chronologyService = chronologyService;
        _logger = logger;
    }

    public async Task<ChronologyReportDto> ExecuteAsync(
        Guid projectId,
        TimeSpan? sessionGap = null,
        CancellationToken ct = default)
    {
        var assets = await _mediaRepo.ListByProjectAsync(projectId, ct);

        var pairs = assets
            .Where(a => a.Metadata != null)
            .Select(a => (a, a.Metadata!))
            .ToList();

        var report = _chronologyService.BuildChronology(pairs, sessionGap);

        var sessionDtos = report.Sessions.Select(s => new ChronologySessionDto(
            SessionIndex: s.SessionIndex,
            Name: s.Name,
            StartTimeUtc: s.StartTimeUtc,
            EndTimeUtc: s.EndTimeUtc,
            TotalClips: s.TotalClips,
            TotalDurationSeconds: s.TotalDurationSeconds,
            CameraModels: s.CameraModels,
            Clips: s.Clips.Select(c => new ChronologyItemDto(
                MediaId: c.MediaId,
                RelativePath: c.RelativePath,
                Kind: c.Kind.ToString(),
                CameraModel: c.CameraModel,
                RecordedAtUtc: c.RecordedAtUtc,
                DurationSeconds: c.DurationSeconds,
                EstimatedEndAtUtc: c.EstimatedEndAtUtc,
                TimestampSource: c.TimestampSource,
                Confidence: c.Confidence,
                SessionIndex: c.SessionIndex,
                Anomaly: c.Anomaly
            )).ToList()
        )).ToList();

        return new ChronologyReportDto(
            Sessions: sessionDtos,
            TotalClips: report.TotalClips,
            TotalDurationSeconds: report.TotalDurationSeconds,
            EarliestUtc: report.EarliestUtc,
            LatestUtc: report.LatestUtc,
            AnomaliesDetected: report.AnomaliesDetected
        );
    }
}
