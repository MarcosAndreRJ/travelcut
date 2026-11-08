using Microsoft.Extensions.Logging;
using TravelCut.Application.Ports;
using TravelCut.Contracts.Dtos;
using TravelCut.Domain.Entities.Editorial;

namespace TravelCut.Application.Editorial;

public interface IEditorialEffectsService
{
    Task<EditorialTransitionDto> CreateManualTransitionAsync(CreateTransitionRequest request, CancellationToken ct = default);
    Task<EditorialTransitionDto?> UpdateTransitionStatusAsync(Guid transitionId, string status, CancellationToken ct = default);
    Task<bool> DeleteTransitionAsync(Guid transitionId, CancellationToken ct = default);
    Task<IReadOnlyList<EditorialTransitionDto>> GetByProjectAsync(Guid projectId, CancellationToken ct = default);
    Task<ValidateHandlesResultDto> ValidateHandlesAsync(ValidateHandlesRequest request, CancellationToken ct = default);
    IReadOnlyList<EffectPreviewStateDto> GetPreviewStates(Guid projectId);
}

public sealed class EditorialEffectsService : IEditorialEffectsService
{
    private readonly IEditorialTransitionRepository _transitionRepo;
    private readonly IMediaRepository _mediaRepo;
    private readonly ILogger<EditorialEffectsService> _logger;

    public EditorialEffectsService(
        IEditorialTransitionRepository transitionRepo,
        IMediaRepository mediaRepo,
        ILogger<EditorialEffectsService> logger)
    {
        _transitionRepo = transitionRepo;
        _mediaRepo = mediaRepo;
        _logger = logger;
    }

    public async Task<EditorialTransitionDto> CreateManualTransitionAsync(CreateTransitionRequest request, CancellationToken ct = default)
    {
        if (!Guid.TryParse(request.ProjectId, out var projectId)) throw new ArgumentException("ProjectId invalido.");
        if (!Guid.TryParse(request.MediaIdA, out var mediaA)) throw new ArgumentException("MediaIdA invalido.");
        if (!Guid.TryParse(request.MediaIdB, out var mediaB)) throw new ArgumentException("MediaIdB invalido.");

        var type = ParseType(request.Type);
        var preset = ParsePreset(request.Preset ?? TransitionConstants.PresetStandard);
        double duration = request.DurationSeconds ?? EditorialTransition.PresetToSeconds(preset);

        var val = await ValidateHandlesAsync(new ValidateHandlesRequest(request.MediaIdA, request.MediaIdB, duration, request.Type, null, null), ct);

        var transition = new EditorialTransition
        {
            ProjectId = projectId,
            MediaIdA = mediaA,
            MediaIdB = mediaB,
            Type = type,
            Preset = preset,
            DurationSeconds = duration,
            Status = TransitionStatus.Pending,
            Origin = TransitionOrigin.Manual,
            Rationale = request.Rationale ?? $"Transicao manual {request.Type} ({duration:0.00}s).",
            InsufficientHandles = !val.HasEnoughHandles,
            HandleWarningMessage = val.WarningMessage,
            AvailableHandleFramesA = val.FramesAAvailable,
            AvailableHandleFramesB = val.FramesBAvailable
        };

        await _transitionRepo.SaveAsync(transition, ct);
        return MapToDto(transition);
    }

    public async Task<EditorialTransitionDto?> UpdateTransitionStatusAsync(Guid transitionId, string status, CancellationToken ct = default)
    {
        var transition = await _transitionRepo.GetByIdAsync(transitionId, ct);
        if (transition == null) return null;

        var norm = (status ?? string.Empty).Trim().ToLowerInvariant();
        transition.Status = norm switch
        {
            TransitionConstants.StatusApproved or "approved" => TransitionStatus.Approved,
            TransitionConstants.StatusRejected or "rejected" => TransitionStatus.Rejected,
            TransitionConstants.StatusDeferred or "deferred" => TransitionStatus.Deferred,
            _ => TransitionStatus.Pending
        };
        transition.UpdatedAtUtc = DateTime.UtcNow;

        await _transitionRepo.SaveAsync(transition, ct);
        return MapToDto(transition);
    }

    public async Task<bool> DeleteTransitionAsync(Guid transitionId, CancellationToken ct = default)
    {
        var existing = await _transitionRepo.GetByIdAsync(transitionId, ct);
        if (existing == null) return false;
        await _transitionRepo.DeleteAsync(transitionId, ct);
        return true;
    }

    public async Task<IReadOnlyList<EditorialTransitionDto>> GetByProjectAsync(Guid projectId, CancellationToken ct = default)
    {
        var list = await _transitionRepo.ListByProjectAsync(projectId, ct);
        return list.Select(MapToDto).ToList();
    }

    public async Task<ValidateHandlesResultDto> ValidateHandlesAsync(ValidateHandlesRequest request, CancellationToken ct = default)
    {
        double fps = 30.0;
        int requiredFrames = (int)Math.Ceiling(request.DurationSeconds * fps);

        Guid.TryParse(request.MediaIdA, out var mediaIdA);
        Guid.TryParse(request.MediaIdB, out var mediaIdB);

        var mediaA = mediaIdA != Guid.Empty ? await _mediaRepo.GetByIdAsync(mediaIdA, ct) : null;
        var mediaB = mediaIdB != Guid.Empty ? await _mediaRepo.GetByIdAsync(mediaIdB, ct) : null;

        int framesA = 30;
        int framesB = 30;

        if (mediaA != null && request.ClipEndSecondsA.HasValue)
        {
            double durationA = mediaA.Metadata?.DurationSeconds ?? 10.0;
            double tailSeconds = Math.Max(0, durationA - request.ClipEndSecondsA.Value);
            framesA = (int)Math.Floor(tailSeconds * fps);
        }

        if (mediaB != null && request.ClipStartSecondsB.HasValue)
        {
            double headSeconds = Math.Max(0, request.ClipStartSecondsB.Value);
            framesB = (int)Math.Floor(headSeconds * fps);
        }

        bool hasEnough = (framesA + framesB) >= requiredFrames;
        string? warn = null;
        string? alt = null;

        if (!hasEnough)
        {
            warn = $"Material de sobreposicao insuficiente para dissolvencia de {request.DurationSeconds:0.00}s ({requiredFrames} frames necessarios, {framesA + framesB} disponiveis).";
            alt = "Utilize corte seco ou transicao de fade-in/fade-out individual.";
        }

        return new ValidateHandlesResultDto(
            IsValid: true,
            HasEnoughHandles: hasEnough,
            FramesAAvailable: framesA,
            FramesBAvailable: framesB,
            FramesRequired: requiredFrames,
            WarningMessage: warn,
            AlternativeAction: alt
        );
    }

    public IReadOnlyList<EffectPreviewStateDto> GetPreviewStates(Guid projectId)
    {
        return new List<EffectPreviewStateDto>
        {
            new EffectPreviewStateDto("preview-zoom", "zoom", true, true, true, true, null),
            new EffectPreviewStateDto("preview-fade-in", "video_fade_in", true, true, true, true, null),
            new EffectPreviewStateDto("preview-fade-out", "video_fade_out", true, true, true, true, null),
            new EffectPreviewStateDto("preview-dissolve", "video_dissolve", true, true, true, false, "Composicao de dissolvencia entre dois arquivos de video requer exportacao DaVinci ou renderizacao multitrack."),
            new EffectPreviewStateDto("preview-crossfade", "audio_crossfade", true, true, true, true, null)
        };
    }

    public static EditorialTransitionDto MapToDto(EditorialTransition t) =>
        new(
            TransitionId: t.TransitionId.ToString(),
            ProjectId: t.ProjectId.ToString(),
            MediaIdA: t.MediaIdA.ToString(),
            MediaIdB: t.MediaIdB.ToString(),
            Type: FormatType(t.Type),
            DurationSeconds: t.DurationSeconds,
            Preset: FormatPreset(t.Preset),
            Status: FormatStatus(t.Status),
            Origin: t.Origin.ToString().ToLowerInvariant(),
            Rationale: t.Rationale,
            IsAdjacentOnTimeline: t.IsAdjacentOnTimeline,
            InsufficientHandles: t.InsufficientHandles,
            HandleWarningMessage: t.HandleWarningMessage,
            AvailableHandleFramesA: t.AvailableHandleFramesA,
            AvailableHandleFramesB: t.AvailableHandleFramesB,
            TimelineClipIdA: t.TimelineClipIdA?.ToString(),
            TimelineClipIdB: t.TimelineClipIdB?.ToString(),
            CreatedAtUtc: t.CreatedAtUtc.ToString("o"),
            UpdatedAtUtc: t.UpdatedAtUtc.ToString("o")
        );

    private static EditorialTransitionType ParseType(string type) => (type ?? string.Empty).ToLowerInvariant() switch
    {
        TransitionConstants.TypeVideoFadeIn or "video_fade_in" => EditorialTransitionType.VideoFadeIn,
        TransitionConstants.TypeVideoFadeOut or "video_fade_out" => EditorialTransitionType.VideoFadeOut,
        TransitionConstants.TypeVideoDissolve or "video_dissolve" => EditorialTransitionType.VideoDissolve,
        TransitionConstants.TypeAudioFadeIn or "audio_fade_in" => EditorialTransitionType.AudioFadeIn,
        TransitionConstants.TypeAudioFadeOut or "audio_fade_out" => EditorialTransitionType.AudioFadeOut,
        TransitionConstants.TypeAudioCrossfade or "audio_crossfade" => EditorialTransitionType.AudioCrossfade,
        _ => EditorialTransitionType.Cut
    };

    private static string FormatType(EditorialTransitionType t) => t switch
    {
        EditorialTransitionType.VideoFadeIn => TransitionConstants.TypeVideoFadeIn,
        EditorialTransitionType.VideoFadeOut => TransitionConstants.TypeVideoFadeOut,
        EditorialTransitionType.VideoDissolve => TransitionConstants.TypeVideoDissolve,
        EditorialTransitionType.AudioFadeIn => TransitionConstants.TypeAudioFadeIn,
        EditorialTransitionType.AudioFadeOut => TransitionConstants.TypeAudioFadeOut,
        EditorialTransitionType.AudioCrossfade => TransitionConstants.TypeAudioCrossfade,
        _ => TransitionConstants.TypeCut
    };

    private static TransitionPresetDuration ParsePreset(string preset) => (preset ?? string.Empty).ToLowerInvariant() switch
    {
        TransitionConstants.PresetShort or "short" => TransitionPresetDuration.Short,
        TransitionConstants.PresetSmooth or "smooth" => TransitionPresetDuration.Smooth,
        TransitionConstants.PresetCustom or "custom" => TransitionPresetDuration.Custom,
        _ => TransitionPresetDuration.Standard
    };

    private static string FormatPreset(TransitionPresetDuration p) => p switch
    {
        TransitionPresetDuration.Short => TransitionConstants.PresetShort,
        TransitionPresetDuration.Smooth => TransitionConstants.PresetSmooth,
        TransitionPresetDuration.Custom => TransitionConstants.PresetCustom,
        _ => TransitionConstants.PresetStandard
    };

    private static string FormatStatus(TransitionStatus s) => s switch
    {
        TransitionStatus.Approved => TransitionConstants.StatusApproved,
        TransitionStatus.Rejected => TransitionConstants.StatusRejected,
        TransitionStatus.Deferred => TransitionConstants.StatusDeferred,
        _ => TransitionConstants.StatusPending
    };
}
