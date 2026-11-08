using Microsoft.Extensions.Logging.Abstractions;
using TravelCut.Application.Editorial;
using TravelCut.Application.Ports;
using TravelCut.Contracts.Dtos;
using TravelCut.Domain.Entities;
using TravelCut.Domain.Entities.Editorial;
using TravelCut.Domain.ValueObjects;
using Xunit;

namespace TravelCut.UnitTests.Editorial;

public class InMemoryTransitionRepo : IEditorialTransitionRepository
{
    private readonly List<EditorialTransition> _transitions = new();

    public Task<EditorialTransition?> GetByIdAsync(Guid transitionId, CancellationToken ct = default)
        => Task.FromResult(_transitions.FirstOrDefault(t => t.TransitionId == transitionId));

    public Task<List<EditorialTransition>> ListByProjectAsync(Guid projectId, CancellationToken ct = default)
        => Task.FromResult(_transitions.Where(t => t.ProjectId == projectId).ToList());

    public Task<List<EditorialTransition>> ListByMediaPairAsync(Guid mediaIdA, Guid mediaIdB, CancellationToken ct = default)
        => Task.FromResult(_transitions.Where(t => t.MediaIdA == mediaIdA && t.MediaIdB == mediaIdB).ToList());

    public Task SaveAsync(EditorialTransition transition, CancellationToken ct = default)
    {
        var existing = _transitions.FirstOrDefault(t => t.TransitionId == transition.TransitionId);
        if (existing != null) _transitions.Remove(existing);
        _transitions.Add(transition);
        return Task.CompletedTask;
    }

    public Task SaveBatchAsync(IEnumerable<EditorialTransition> transitions, CancellationToken ct = default)
    {
        foreach (var t in transitions)
        {
            var existing = _transitions.FirstOrDefault(x => x.TransitionId == t.TransitionId);
            if (existing != null) _transitions.Remove(existing);
            _transitions.Add(t);
        }
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid transitionId, CancellationToken ct = default)
    {
        var existing = _transitions.FirstOrDefault(t => t.TransitionId == transitionId);
        if (existing != null) _transitions.Remove(existing);
        return Task.CompletedTask;
    }
}

public class InMemoryMediaRepo : IMediaRepository
{
    private readonly List<MediaAsset> _assets = new();

    public void Add(MediaAsset a) => _assets.Add(a);

    public Task<MediaAsset?> GetByIdAsync(Guid mediaId, CancellationToken ct = default)
        => Task.FromResult(_assets.FirstOrDefault(a => a.MediaId == mediaId));

    public Task<List<MediaAsset>> ListByProjectAsync(Guid projectId, CancellationToken ct = default)
        => Task.FromResult(_assets.Where(a => a.ProjectId == projectId).ToList());

    public Task SaveAsync(MediaAsset media, CancellationToken ct = default) => Task.CompletedTask;
    public Task SaveBatchAsync(IEnumerable<MediaAsset> media, CancellationToken ct = default) => Task.CompletedTask;
    public Task<List<MediaAsset>> FindByRelativePathsAsync(Guid projectId, IEnumerable<string> paths, CancellationToken ct = default) => Task.FromResult(new List<MediaAsset>());
    public Task<List<MediaAsset>> FindByFingerprintsAsync(Guid projectId, IEnumerable<string> fingerprints, CancellationToken ct = default) => Task.FromResult(new List<MediaAsset>());
    public Task<bool> DeleteAsync(Guid mediaId, CancellationToken ct = default) => Task.FromResult(true);
}

public class EditorialTransitionTests
{
    private readonly InMemoryTransitionRepo _transitionRepo = new();
    private readonly InMemoryMediaRepo _mediaRepo = new();
    private readonly EditorialEffectsService _effectsService;

    public EditorialTransitionTests()
    {
        _effectsService = new EditorialEffectsService(_transitionRepo, _mediaRepo, NullLogger<EditorialEffectsService>.Instance);
    }

    [Fact]
    public async Task CreateManualTransition_WithSufficientHandles_Succeeds()
    {
        var projectId = Guid.NewGuid();
        var mediaA = new MediaAsset
        {
            MediaId = Guid.NewGuid(),
            ProjectId = projectId,
            RelativePath = "cam_a.mp4"
        };
        var mediaB = new MediaAsset
        {
            MediaId = Guid.NewGuid(),
            ProjectId = projectId,
            RelativePath = "cam_b.mp4"
        };
        _mediaRepo.Add(mediaA);
        _mediaRepo.Add(mediaB);

        var req = new CreateTransitionRequest(
            ProjectId: projectId.ToString(),
            MediaIdA: mediaA.MediaId.ToString(),
            MediaIdB: mediaB.MediaId.ToString(),
            Type: TransitionConstants.TypeVideoDissolve,
            DurationSeconds: 0.4,
            Preset: TransitionConstants.PresetStandard,
            Origin: "manual",
            Rationale: "Transicao suave entre tomadas"
        );

        var result = await _effectsService.CreateManualTransitionAsync(req);

        Assert.NotNull(result);
        Assert.Equal(TransitionConstants.TypeVideoDissolve, result.Type);
        Assert.Equal(0.4, result.DurationSeconds);
        Assert.Equal(TransitionConstants.PresetStandard, result.Preset);
        Assert.Equal(TransitionConstants.StatusPending, result.Status);
    }

    [Fact]
    public async Task ValidateHandles_WhenInsufficientFrames_ReturnsWarningAndAlternative()
    {
        var guidA = Guid.NewGuid();
        var guidB = Guid.NewGuid();
        _mediaRepo.Add(new MediaAsset
        {
            MediaId = guidA,
            Metadata = new MediaMetadata { DurationSeconds = 100.0 }
        });
        _mediaRepo.Add(new MediaAsset
        {
            MediaId = guidB,
            Metadata = new MediaMetadata { DurationSeconds = 100.0 }
        });

        // 1.0s de dissolvencia a 30fps = 30 frames necessarios
        // Se clipA termina rente ao fim do arquivo (0 frames disponiveis)
        var req = new ValidateHandlesRequest(
            MediaIdA: guidA.ToString(),
            MediaIdB: guidB.ToString(),
            DurationSeconds: 1.0,
            Type: TransitionConstants.TypeVideoDissolve,
            ClipEndSecondsA: 100.0,
            ClipStartSecondsB: 0.0
        );

        var validation = await _effectsService.ValidateHandlesAsync(req);

        Assert.NotNull(validation);
        Assert.False(validation.HasEnoughHandles);
        Assert.False(string.IsNullOrEmpty(validation.WarningMessage));
        Assert.Contains("corte seco", validation.AlternativeAction);
    }

    [Fact]
    public void Presets_ReturnStandardEditorialDurations()
    {
        Assert.Equal(0.2, EditorialTransition.PresetToSeconds(TransitionPresetDuration.Short));
        Assert.Equal(0.4, EditorialTransition.PresetToSeconds(TransitionPresetDuration.Standard));
        Assert.Equal(0.8, EditorialTransition.PresetToSeconds(TransitionPresetDuration.Smooth));
        Assert.Equal(1.5, EditorialTransition.PresetToSeconds(TransitionPresetDuration.Custom, 1.5));
    }
}
