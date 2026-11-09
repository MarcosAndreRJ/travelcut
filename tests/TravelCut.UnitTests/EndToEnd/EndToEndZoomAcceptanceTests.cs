using System.Text.Json;
using TravelCut.Contracts.Dtos;
using Xunit;
using Xunit.Abstractions;
using static TravelCut.UnitTests.EndToEnd.EndToEndPreEditAcceptanceTests;

namespace TravelCut.UnitTests.EndToEnd;

/// <summary>
/// Aceite J1 4: zoom automático Equilibrado em fala direta à câmera vira clip.zoom e o desfazer reverte.
/// FIXTURES DE PORTA (ffprobe, Whisper, silencedetect): não são prova de mídia real. A classificação de cena
/// (direct_to_camera, enquadramento médio) é gravada pelo IPC "scene.upsert", como faria o usuário.
/// </summary>
public sealed class EndToEndZoomAcceptanceTests : IDisposable
{
    private const string ZoomClip = "zoom01.mp4";
    private readonly E2EWorkspace _ws = new();
    private readonly ITestOutputHelper _out;

    public EndToEndZoomAcceptanceTests(ITestOutputHelper output) => _out = output;

    public void Dispose() => _ws.Dispose();

    /// <summary>8 s: fala 0–1,0; silêncio evidente 1,6–4,6 (cortado); fala contínua 5,2–8,0 (trecho de fala 2, vira zoom).</summary>
    internal static E2EPortFixtures ZoomFixtures() => new E2EPortFixtures().Add(new E2EMediaSpec(ZoomClip,
        E2EPortFixtures.Cfr2997(8.0),
        [
            E2EPortFixtures.Word(1, 0.0, 0.5, "Olá"),
            E2EPortFixtures.Word(2, 0.5, 1.0, "pessoal"),
            E2EPortFixtures.Word(3, 5.2, 5.8, "hoje"),
            E2EPortFixtures.Word(4, 5.8, 6.6, "vamos"),
            E2EPortFixtures.Word(5, 6.6, 7.3, "conhecer"),
            E2EPortFixtures.Word(6, 7.3, 8.0, "Lisboa"),
        ],
        [HighSilence(1.6, 4.6)]));

    private static List<JsonElement> V1Clips(JsonElement plan) =>
        plan.GetProperty("clips").EnumerateArray().Where(c => c.GetProperty("trackId").GetString() == "V1").ToList();

    private static double? MaxZoom(JsonElement plan) =>
        V1Clips(plan).Select(c => c.GetProperty("zoom")).Where(z => z.ValueKind == JsonValueKind.Number).Select(z => (double?)z.GetDouble()).Max();

    [Fact]
    public async Task ZoomAutomaticoEquilibrado_ViraClipZoom_DesfazerReverte()
    {
        var root = _ws.ProjectRoot("Viagem Zoom");
        using var host = E2EHost.Start(_ws.DatabasePath, ZoomFixtures());
        var projectId = await E2ESteps.CreateProjectAsync(host, root, "Viagem Zoom");
        E2ESteps.PlaceMedia(root, [ZoomClip]);
        var originBefore = E2EWorkspace.Tree(Path.Combine(root, "1-Bruto"));
        await E2ESteps.ScanAsync(host, projectId);
        var mediaId = (await E2ESteps.MediaNamesAsync(host, projectId)).Single().Key;

        // Cena: fala direta à câmera em plano médio (ponto focal conhecido), gravada antes da análise.
        await host.Ok("scene.upsert", new UpsertSceneClassificationRequest(null, projectId.ToString(), mediaId.ToString(),
            null, null, true, SceneClassificationConstants.EnvInterior, SceneClassificationConstants.InteractionDirectToCamera,
            SceneClassificationConstants.FramingMedium, SceneClassificationConstants.MovementStatic,
            SceneClassificationConstants.ProfileNatural, "fixture do teste de aceite"));

        Assert.Equal("completed", (await E2ESteps.AnalyzeAsync(host, projectId)).GetProperty("state").GetString());

        var suggestions = await E2ESteps.SuggestionsAsync(host, projectId);
        var zoom = Assert.Single(suggestions, s => s.GetProperty("suggestedAction").GetString() == "zoom");
        var zoomId = zoom.GetProperty("suggestionId").GetGuid();
        Assert.Equal("auto", zoom.GetProperty("origin").GetString());

        // Plano: o clipe de fala 2 recebe clip.zoom (Equilibrado: <= 1,06x); o silêncio foi cortado.
        var plan = await E2ESteps.LatestPlanAsync(host, projectId);
        Assert.Equal("balanced", plan.GetProperty("profile").GetProperty("silencePolicy").GetString());
        var scale = MaxZoom(plan);
        Assert.NotNull(scale);
        Assert.InRange(scale!.Value, 1.01, 1.06);
        var zoomed = Assert.Single(V1Clips(plan), c => c.GetProperty("zoom").ValueKind == JsonValueKind.Number);
        Assert.True(zoomed.GetProperty("sourceInUs").GetInt64() >= 4_600_000, "O zoom está no clipe de fala 2.");
        Assert.Contains(zoomId, zoomed.GetProperty("suggestionIds").EnumerateArray().Select(e => e.GetGuid()));
        var effect = Assert.Single(plan.GetProperty("appliedEffects").EnumerateArray(), e => e.GetProperty("kind").GetString() == "zoom");
        Assert.Equal("auto", effect.GetProperty("origin").GetString());
        Assert.Equal(zoomId, effect.GetProperty("suggestionId").GetGuid());
        Assert.False(V1Covers(plan, mediaId, 1_700_000, 4_500_000));
        Assert.Equal(1, plan.GetProperty("summary").GetProperty("autoCutsApplied").GetInt32());

        // Reverter o zoom automático (rejeitar): clip.zoom some. Desfazer a decisão: sugestão volta a pendente e NÃO é reaplicada (E07).
        await E2ESteps.DecideAsync(host, zoomId, "reject");
        plan = await E2ESteps.LatestPlanAsync(host, projectId);
        Assert.Null(MaxZoom(plan));
        await host.Ok("decision.undo", new UndoDecisionRequest(zoomId));
        plan = await E2ESteps.LatestPlanAsync(host, projectId);
        Assert.Null(MaxZoom(plan));
        Assert.Contains(plan.GetProperty("pending").EnumerateArray(), p => p.GetProperty("suggestionId").GetGuid() == zoomId);

        // Aprovar manualmente aplica o zoom (origem humana); desfazer reverte; refazer reaplica.
        await E2ESteps.DecideAsync(host, zoomId, "approve");
        plan = await E2ESteps.LatestPlanAsync(host, projectId);
        Assert.NotNull(MaxZoom(plan));
        Assert.Equal("human", Assert.Single(plan.GetProperty("appliedEffects").EnumerateArray(), e => e.GetProperty("kind").GetString() == "zoom").GetProperty("origin").GetString());
        await host.Ok("decision.undo", new UndoDecisionRequest(zoomId));
        Assert.Null(MaxZoom(await E2ESteps.LatestPlanAsync(host, projectId)));
        await host.Ok("decision.redo", new RedoDecisionRequest(zoomId));
        Assert.NotNull(MaxZoom(await E2ESteps.LatestPlanAsync(host, projectId)));

        Assert.Equal(originBefore, E2EWorkspace.Tree(Path.Combine(root, "1-Bruto")));
    }
}
