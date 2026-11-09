using Microsoft.Extensions.Logging;
using TravelCut.Application.Ports;
using TravelCut.Application.Services;
using TravelCut.Contracts.Dtos;
using TravelCut.Domain.Entities.Editorial;

namespace TravelCut.Application.Editorial;

public interface ITransitionRecommender
{
    Task<IReadOnlyList<EditorialTransitionDto>> GenerateImportSuggestionsAsync(
        Guid projectId,
        string editorialProfile = "natural",
        CancellationToken ct = default);

    Task<bool> ValidateAdjacencyOnTimelineAsync(Guid transitionId, CancellationToken ct = default);
}

public sealed class TransitionRecommender : ITransitionRecommender
{
    private readonly IMediaRepository _mediaRepo;
    private readonly IEditPlanRepository _planRepo;
    private readonly IEditorialTransitionRepository _transitionRepo;
    private readonly ISceneClassificationService _sceneService;
    private readonly ILogger<TransitionRecommender> _logger;
    private readonly IChronologyService _chronology;

    public TransitionRecommender(
        IMediaRepository mediaRepo,
        IEditPlanRepository planRepo,
        IEditorialTransitionRepository transitionRepo,
        ISceneClassificationService sceneService,
        ILogger<TransitionRecommender> logger,
        IChronologyService? chronology = null)
    {
        _chronology = chronology ?? new ChronologyService();
        _mediaRepo = mediaRepo;
        _planRepo = planRepo;
        _transitionRepo = transitionRepo;
        _sceneService = sceneService;
        _logger = logger;
    }

    public async Task<IReadOnlyList<EditorialTransitionDto>> GenerateImportSuggestionsAsync(
        Guid projectId,
        string editorialProfile = "natural",
        CancellationToken ct = default)
    {
        var mediaList = await _mediaRepo.ListByProjectAsync(projectId, ct);
        if (mediaList.Count < 2) return Array.Empty<EditorialTransitionDto>();

        // E05: ordem cronológica real (ChronologyService: creation_time, fallback documentado), nunca LastWriteUtc cru.
        var sorted = OrderChronologically(mediaList);
        if (sorted.Count < 2) return Array.Empty<EditorialTransitionDto>();
        var existing = await _transitionRepo.ListByProjectAsync(projectId, ct);
        var existingPairs = existing.Select(t => (t.MediaIdA, t.MediaIdB)).ToHashSet();

        var newTransitions = new List<EditorialTransition>();

        for (int i = 0; i < sorted.Count - 1; i++)
        {
            var mediaA = sorted[i];
            var mediaB = sorted[i + 1];

            if (existingPairs.Contains((mediaA.MediaId, mediaB.MediaId))) continue;

            var classA = await _sceneService.GetEffectiveClassificationAtAsync(mediaA.MediaId, 0, ct);
            var classB = await _sceneService.GetEffectiveClassificationAtAsync(mediaB.MediaId, 0, ct);

            var type = EditorialTransitionType.VideoDissolve;
            var preset = TransitionPresetDuration.Standard;
            string rationale = "Transição suave sugerida entre mídias adjacentes na importação.";

            if (editorialProfile.Equals("dynamic", StringComparison.OrdinalIgnoreCase))
            {
                preset = TransitionPresetDuration.Short;
                rationale = "Perfil dinâmico: corte ou dissolvência curta recomendada.";
            }
            else if (editorialProfile.Equals("cinematic", StringComparison.OrdinalIgnoreCase))
            {
                preset = TransitionPresetDuration.Smooth;
                rationale = "Perfil cinematográfico: dissolvência gradual entre cenas.";
            }

            var transition = new EditorialTransition
            {
                ProjectId = projectId,
                MediaIdA = mediaA.MediaId,
                MediaIdB = mediaB.MediaId,
                Type = type,
                Preset = preset,
                DurationSeconds = EditorialTransition.PresetToSeconds(preset),
                Status = TransitionStatus.Pending,
                Origin = TransitionOrigin.Import,
                Rationale = rationale,
                IsAdjacentOnTimeline = false,
                InsufficientHandles = false
            };

            newTransitions.Add(transition);
        }

        if (newTransitions.Count > 0)
        {
            await _transitionRepo.SaveBatchAsync(newTransitions, ct);
        }

        var all = await _transitionRepo.ListByProjectAsync(projectId, ct);
        return all.Select(EditorialEffectsService.MapToDto).ToList();
    }

    private List<TravelCut.Domain.Entities.MediaAsset> OrderChronologically(List<TravelCut.Domain.Entities.MediaAsset> media)
    {
        var withMeta = media.Where(m => m.Metadata is not null).ToList();
        var report = _chronology.BuildChronology(withMeta.Select(m => (Asset: m, Metadata: m.Metadata!)));
        var byId = withMeta.ToDictionary(m => m.MediaId);
        // Mídias sem metadados não têm cronologia confiável: ficam de fora (não se inventa ordem).
        return report.Sessions.SelectMany(s => s.Clips)
            .DistinctBy(c => c.MediaId)
            .OrderBy(c => c.RecordedAtUtc).ThenBy(c => c.RelativePath, StringComparer.OrdinalIgnoreCase)
            .Where(c => byId.ContainsKey(c.MediaId))
            .Select(c => byId[c.MediaId]).ToList();
    }

    public async Task<bool> ValidateAdjacencyOnTimelineAsync(Guid transitionId, CancellationToken ct = default)
    {
        var transition = await _transitionRepo.GetByIdAsync(transitionId, ct);
        if (transition == null) return false;

        var latestPlan = await _planRepo.GetLatestByProjectAsync(transition.ProjectId, ct);
        if (latestPlan == null || latestPlan.Clips.Count < 2)
        {
            transition.IsAdjacentOnTimeline = false;
            await _transitionRepo.SaveAsync(transition, ct);
            return false;
        }

        var clips = latestPlan.Clips.OrderBy(c => c.SequenceOrder).ThenBy(c => c.TimelineStartTicks).ToList();
        bool foundAdjacent = false;

        for (int i = 0; i < clips.Count - 1; i++)
        {
            if (clips[i].MediaId == transition.MediaIdA && clips[i + 1].MediaId == transition.MediaIdB)
            {
                foundAdjacent = true;
                transition.TimelineClipIdA = clips[i].ClipId;
                transition.TimelineClipIdB = clips[i + 1].ClipId;
                break;
            }
        }

        transition.IsAdjacentOnTimeline = foundAdjacent;
        await _transitionRepo.SaveAsync(transition, ct);
        return foundAdjacent;
    }
}
