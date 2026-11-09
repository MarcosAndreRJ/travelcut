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

    /// <summary>
    /// E05: validação real de handles. Dissolve/crossfade centrados no corte exigem metade da duração em CADA lado
    /// (cauda do clipe A e cabeça do clipe B), medida na taxa de quadros real de cada mídia. Bordas ou taxa desconhecidas
    /// nunca são presumidas: IsValid=false e HasEnoughHandles=false. Fades simples e corte não consomem handle.
    /// </summary>
    public async Task<ValidateHandlesResultDto> ValidateHandlesAsync(ValidateHandlesRequest request, CancellationToken ct = default)
    {
        var type = ParseType(request.Type);
        bool needsOverlap = type is EditorialTransitionType.VideoDissolve or EditorialTransitionType.AudioCrossfade;
        double duration = request.DurationSeconds;
        var problems = new List<string>();

        if (double.IsNaN(duration) || double.IsInfinity(duration) || duration < 0 || (needsOverlap && duration <= 0))
        {
            problems.Add("duração da transição inválida");
            duration = 0;
        }

        if (!needsOverlap)
        {
            return new ValidateHandlesResultDto(
                IsValid: problems.Count == 0, HasEnoughHandles: problems.Count == 0,
                FramesAAvailable: 0, FramesBAvailable: 0, FramesRequired: 0,
                WarningMessage: problems.Count == 0 ? null : string.Join("; ", problems) + ".",
                AlternativeAction: null);
        }

        Guid.TryParse(request.MediaIdA, out var mediaIdA);
        Guid.TryParse(request.MediaIdB, out var mediaIdB);
        var mediaA = mediaIdA != Guid.Empty ? await _mediaRepo.GetByIdAsync(mediaIdA, ct) : null;
        var mediaB = mediaIdB != Guid.Empty ? await _mediaRepo.GetByIdAsync(mediaIdB, ct) : null;
        if (mediaA is null) problems.Add("mídia A não encontrada");
        if (mediaB is null) problems.Add("mídia B não encontrada");

        // Cauda de A: duração real do arquivo menos o ponto de saída do clipe. Cabeça de B: ponto de entrada do clipe.
        double? tailA = null;
        double? durationA = mediaA?.Metadata?.DurationSeconds;
        if (mediaA is not null)
        {
            if (request.ClipEndSecondsA is double endA && durationA is > 0 && endA >= 0 && endA <= durationA.Value + 1e-6)
                tailA = Math.Max(0, durationA.Value - endA);
            else
                problems.Add("saída do clipe A ou duração da mídia A desconhecida");
        }
        double? headB = null;
        if (mediaB is not null)
        {
            if (request.ClipStartSecondsB is double startB && startB >= 0 && !double.IsNaN(startB) && !double.IsInfinity(startB))
                headB = startB;
            else
                problems.Add("entrada do clipe B desconhecida");
        }

        bool audioOnly = type == EditorialTransitionType.AudioCrossfade;
        double? fpsA = ValidFps(mediaA?.Metadata?.FrameRate);
        double? fpsB = ValidFps(mediaB?.Metadata?.FrameRate);
        if (!audioOnly)
        {
            if (mediaA is not null && fpsA is null) problems.Add("taxa de quadros da mídia A desconhecida");
            if (mediaB is not null && fpsB is null) problems.Add("taxa de quadros da mídia B desconhecida");
        }

        double perSide = duration / 2.0;
        int reqA = fpsA is double fa ? (int)Math.Ceiling(perSide * fa - 1e-9) : 0;
        int reqB = fpsB is double fb ? (int)Math.Ceiling(perSide * fb - 1e-9) : 0;
        int availA = tailA is double ta && fpsA is double fa2 ? (int)Math.Floor(ta * fa2 + 1e-9) : 0;
        int availB = headB is double hb && fpsB is double fb2 ? (int)Math.Floor(hb * fb2 + 1e-9) : 0;

        bool measurable = problems.Count == 0;
        bool enough = false;
        if (measurable)
        {
            enough = audioOnly
                ? tailA!.Value + 1e-9 >= perSide && headB!.Value + 1e-9 >= perSide
                : availA >= reqA && availB >= reqB;
        }

        string? warn = null;
        string? alt = null;
        if (!measurable)
        {
            warn = $"Handles não verificáveis: {string.Join("; ", problems)}.";
            alt = "Informe as bordas dos clipes ou utilize corte seco / fade simples.";
        }
        else if (!enough)
        {
            warn = audioOnly
                ? $"Material insuficiente para crossfade de {duration:0.00}s (necessário {perSide:0.00}s em cada lado; disponível {tailA!.Value:0.00}s em A e {headB!.Value:0.00}s em B)."
                : $"Material de sobreposição insuficiente para dissolvência de {duration:0.00}s (necessário {reqA} quadro(s) em A e {reqB} em B; disponível {availA} em A e {availB} em B).";
            alt = "Utilize corte seco ou transição de fade-in/fade-out individual.";
        }

        return new ValidateHandlesResultDto(
            IsValid: measurable,
            HasEnoughHandles: enough,
            FramesAAvailable: availA,
            FramesBAvailable: availB,
            FramesRequired: reqA + reqB,
            WarningMessage: warn,
            AlternativeAction: alt);
    }

    private static double? ValidFps(double? fps) => fps is > 0 and < 1000 ? fps : null;

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
