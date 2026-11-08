using Microsoft.Extensions.Logging.Abstractions;
using TravelCut.Application.Editorial;
using TravelCut.Application.Ports;
using TravelCut.Contracts.Dtos;
using TravelCut.Domain.Entities.Editorial;
using Xunit;

namespace TravelCut.UnitTests.Editorial;

public class InMemorySceneClassificationRepo : ISceneClassificationRepository
{
    private readonly List<SceneClassificationEntry> _entries = new();

    public Task<SceneClassificationEntry?> GetByIdAsync(Guid entryId, CancellationToken ct = default)
        => Task.FromResult(_entries.FirstOrDefault(e => e.EntryId == entryId));

    public Task<List<SceneClassificationEntry>> ListByProjectAsync(Guid projectId, CancellationToken ct = default)
        => Task.FromResult(_entries.Where(e => e.ProjectId == projectId).ToList());

    public Task<List<SceneClassificationEntry>> ListByMediaAsync(Guid mediaId, CancellationToken ct = default)
        => Task.FromResult(_entries.Where(e => e.MediaId == mediaId).ToList());

    public Task SaveAsync(SceneClassificationEntry entry, CancellationToken ct = default)
    {
        var existing = _entries.FirstOrDefault(e => e.EntryId == entry.EntryId);
        if (existing != null) _entries.Remove(existing);
        _entries.Add(entry);
        return Task.CompletedTask;
    }

    public Task SaveBatchAsync(IEnumerable<SceneClassificationEntry> entries, CancellationToken ct = default)
    {
        foreach (var entry in entries)
        {
            var existing = _entries.FirstOrDefault(e => e.EntryId == entry.EntryId);
            if (existing != null) _entries.Remove(existing);
            _entries.Add(entry);
        }
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid entryId, CancellationToken ct = default)
    {
        var existing = _entries.FirstOrDefault(e => e.EntryId == entryId);
        if (existing != null) _entries.Remove(existing);
        return Task.CompletedTask;
    }
}

public class SceneClassificationTests
{
    private readonly InMemorySceneClassificationRepo _repo = new();
    private readonly SceneClassificationService _service;

    public SceneClassificationTests()
    {
        _service = new SceneClassificationService(_repo, NullLogger<SceneClassificationService>.Instance);
    }

    [Fact]
    public async Task UpsertFullVideo_SavesAndRetrievesEffectiveClassification()
    {
        var projectId = Guid.NewGuid();
        var mediaId = Guid.NewGuid();

        var request = new UpsertSceneClassificationRequest(
            EntryId: null,
            ProjectId: projectId.ToString(),
            MediaId: mediaId.ToString(),
            StartSeconds: 0,
            EndSeconds: 0,
            IsFullVideo: true,
            Environment: SceneClassificationConstants.EnvInterior,
            Interaction: SceneClassificationConstants.InteractionDirectToCamera,
            Framing: SceneClassificationConstants.FramingMedium,
            Movement: SceneClassificationConstants.MovementStatic,
            EditorialProfile: SceneClassificationConstants.ProfileNatural,
            Notes: "Gravacao em estudio"
        );

        var result = await _service.UpsertClassificationAsync(request);

        Assert.NotNull(result);
        Assert.Equal(SceneClassificationConstants.EnvInterior, result.Environment);
        Assert.Equal(SceneClassificationConstants.InteractionDirectToCamera, result.Interaction);
        Assert.True(result.IsFullVideo);

        var effective = await _service.GetEffectiveClassificationAtAsync(mediaId, 15.5);
        Assert.NotNull(effective);
        Assert.Equal(SceneClassificationConstants.EnvInterior, effective!.Environment);
    }

    [Fact]
    public async Task IntervalClassification_OverridesGeneralClassification()
    {
        var projectId = Guid.NewGuid();
        var mediaId = Guid.NewGuid();

        // 1. Classificacao geral (exterior, paisagem)
        await _service.UpsertClassificationAsync(new UpsertSceneClassificationRequest(
            EntryId: null,
            ProjectId: projectId.ToString(),
            MediaId: mediaId.ToString(),
            StartSeconds: 0,
            EndSeconds: 0,
            IsFullVideo: true,
            Environment: SceneClassificationConstants.EnvExterior,
            Interaction: SceneClassificationConstants.InteractionLandscape,
            Framing: SceneClassificationConstants.FramingWide,
            Movement: SceneClassificationConstants.MovementStatic,
            EditorialProfile: SceneClassificationConstants.ProfileCinematic,
            Notes: "Paisagem montanhosa"
        ));

        // 2. Intervalo de 10s a 20s (fala direta a camera)
        await _service.UpsertClassificationAsync(new UpsertSceneClassificationRequest(
            EntryId: null,
            ProjectId: projectId.ToString(),
            MediaId: mediaId.ToString(),
            StartSeconds: 10,
            EndSeconds: 20,
            IsFullVideo: false,
            Environment: SceneClassificationConstants.EnvExterior,
            Interaction: SceneClassificationConstants.InteractionDirectToCamera,
            Framing: SceneClassificationConstants.FramingCloseUp,
            Movement: SceneClassificationConstants.MovementStatic,
            EditorialProfile: SceneClassificationConstants.ProfileNatural,
            Notes: "Trecho com fala"
        ));

        // Fora do intervalo (aos 5s) -> deve retornar a geral (paisagem)
        var at5 = await _service.GetEffectiveClassificationAtAsync(mediaId, 5.0);
        Assert.NotNull(at5);
        Assert.Equal(SceneClassificationConstants.InteractionLandscape, at5!.Interaction);

        // Dentro do intervalo (aos 15s) -> deve retornar o trecho especifico (fala direta)
        var at15 = await _service.GetEffectiveClassificationAtAsync(mediaId, 15.0);
        Assert.NotNull(at15);
        Assert.Equal(SceneClassificationConstants.InteractionDirectToCamera, at15!.Interaction);
        Assert.Equal(SceneClassificationConstants.FramingCloseUp, at15.Framing);
    }

    [Fact]
    public async Task BatchClassify_AppliesToMultipleMedia()
    {
        var projectId = Guid.NewGuid();
        var media1 = Guid.NewGuid().ToString();
        var media2 = Guid.NewGuid().ToString();
        var media3 = Guid.NewGuid().ToString();

        var request = new BatchClassifyRequest(
            ProjectId: projectId.ToString(),
            MediaIds: new[] { media1, media2, media3 },
            Environment: SceneClassificationConstants.EnvInterior,
            Interaction: SceneClassificationConstants.InteractionInterview,
            Framing: SceneClassificationConstants.FramingMedium,
            Movement: SceneClassificationConstants.MovementStatic,
            EditorialProfile: SceneClassificationConstants.ProfileNatural,
            Notes: "Lote de entrevistas"
        );

        var results = await _service.BatchClassifyAsync(request);

        Assert.Equal(3, results.Count);
        Assert.All(results, r => Assert.Equal(SceneClassificationConstants.InteractionInterview, r.Interaction));
    }

    [Fact]
    public async Task DeleteClassification_RemovesEntry()
    {
        var projectId = Guid.NewGuid();
        var mediaId = Guid.NewGuid();

        var entry = await _service.UpsertClassificationAsync(new UpsertSceneClassificationRequest(
            EntryId: null,
            ProjectId: projectId.ToString(),
            MediaId: mediaId.ToString(),
            StartSeconds: 0,
            EndSeconds: 0,
            IsFullVideo: true,
            Environment: SceneClassificationConstants.EnvInterior,
            Interaction: SceneClassificationConstants.InteractionOther,
            Framing: SceneClassificationConstants.FramingUnspecified,
            Movement: SceneClassificationConstants.MovementUnspecified,
            EditorialProfile: SceneClassificationConstants.ProfileNatural,
            Notes: null
        ));

        var deleted = await _service.DeleteClassificationAsync(Guid.Parse(entry.EntryId));
        Assert.True(deleted);

        var remaining = await _service.GetByMediaAsync(mediaId);
        Assert.Empty(remaining);
    }
}
