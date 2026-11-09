using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using TravelCut.Application.Editorial;
using TravelCut.Application.Ports;
using TravelCut.Contracts.Dtos;
using TravelCut.Domain.Entities;
using TravelCut.Domain.Enums;
using TravelCut.Domain.ValueObjects;
using Xunit;

namespace TravelCut.UnitTests.Editorial;

/// <summary>E04b: geração única e idempotente de sugestões de zoom no pós-análise.</summary>
public class ZoomSuggestionGeneratorTests
{
    /// <summary>Dublê mínimo: só os métodos usados pelo gerador; qualquer outro lança.</summary>
    private class Fake<T> : DispatchProxy where T : class
    {
        public Dictionary<string, Func<object?[], object?>> Handlers { get; } = new();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod is not null && Handlers.TryGetValue(targetMethod.Name, out var h)) return h(args ?? []);
            throw new NotSupportedException(targetMethod?.Name);
        }

        public static (T Proxy, Fake<T> Handlers) Create()
        {
            var proxy = DispatchProxy.Create<T, Fake<T>>();
            return (proxy, (Fake<T>)(object)proxy);
        }
    }

    private static readonly Guid ProjectId = Guid.NewGuid();
    private readonly Guid _mediaId = Guid.NewGuid();
    private readonly List<EditSuggestion> _store = new();
    private readonly ZoomSuggestionGenerator _generator;

    public ZoomSuggestionGeneratorTests()
    {
        var mediaRepo = new InMemoryMediaRepo();
        mediaRepo.Add(new MediaAsset { MediaId = _mediaId, ProjectId = ProjectId, RelativePath = "a.mp4", Kind = MediaKind.Video });

        // Três trechos de fala (0-3 s, 5-8 s, 10-13 s); o recomendador só sugere nos índices ímpares (5-8 s).
        var transcript = new Transcript { MediaId = _mediaId, Words = Words((0, 3), (5, 8), (10, 13)) };
        var (transcripts, th) = Fake<ITranscriptRepository>.Create();
        th.Handlers[nameof(ITranscriptRepository.GetByMediaIdAsync)] = _ => Task.FromResult<Transcript?>(transcript);

        var (plans, ph) = Fake<IEditPlanRepository>.Create();
        ph.Handlers[nameof(IEditPlanRepository.ListSuggestionsByProjectAsync)] = _ => Task.FromResult(_store.ToList());
        ph.Handlers[nameof(IEditPlanRepository.SaveSuggestionsBatchAsync)] = a =>
        {
            _store.AddRange((IEnumerable<EditSuggestion>)a[0]!);
            return Task.CompletedTask;
        };

        var (scenes, sh) = Fake<ISceneClassificationService>.Create();
        sh.Handlers[nameof(ISceneClassificationService.GetEffectiveClassificationAtAsync)] = _ =>
            Task.FromResult<EditorialSceneClassificationDto?>(new EditorialSceneClassificationDto(
                Guid.NewGuid().ToString(), ProjectId.ToString(), _mediaId.ToString(), 0, 13, true,
                SceneClassificationConstants.EnvInterior, SceneClassificationConstants.InteractionDialogue,
                SceneClassificationConstants.FramingMedium, SceneClassificationConstants.MovementStatic,
                SceneClassificationConstants.ProfileNatural, "", DateTime.UtcNow.ToString("O"), DateTime.UtcNow.ToString("O")));

        _generator = new ZoomSuggestionGenerator(mediaRepo, transcripts, plans, scenes,
            new SmartZoomRecommender(NullLogger<SmartZoomRecommender>.Instance), NullLogger<ZoomSuggestionGenerator>.Instance);
    }

    private static List<TranscriptWord> Words(params (double Start, double End)[] spans) =>
        spans.SelectMany((s, i) => new[]
        {
            new TranscriptWord { Seq = i * 2, StartSeconds = s.Start, EndSeconds = s.Start + 1.5, Text = "a" },
            new TranscriptWord { Seq = i * 2 + 1, StartSeconds = s.Start + 1.5, EndSeconds = s.End, Text = "b" },
        }).ToList();

    [Fact]
    public async Task Generate_CreatesZoomSuggestionInMilliseconds_AndIsIdempotent()
    {
        int first = await _generator.GenerateAsync(ProjectId);

        Assert.Equal(1, first);
        var z = Assert.Single(_store);
        Assert.Equal("zoom", z.SuggestedAction);
        Assert.Equal(SuggestionStatus.Proposed, z.Status);
        Assert.Equal(new TimeRange(5000, 8000), z.SourceInterval); // ms, não ticks

        Assert.Equal(0, await _generator.GenerateAsync(ProjectId)); // reexecutar não duplica
        Assert.Single(_store);
    }

    [Fact]
    public async Task Generate_DoesNotResurrectRejectedOrDecidedZoom()
    {
        await _generator.GenerateAsync(ProjectId);
        _store[0].Status = SuggestionStatus.Rejected; // estado/decisão existente continua bloqueando a regeneração

        Assert.Equal(0, await _generator.GenerateAsync(ProjectId));
        Assert.Single(_store);
    }
}
