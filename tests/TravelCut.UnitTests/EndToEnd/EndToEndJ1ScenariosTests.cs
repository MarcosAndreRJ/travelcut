using System.Text.Json;
using TravelCut.Contracts.Dtos;
using Xunit;
using Xunit.Abstractions;
using static TravelCut.UnitTests.EndToEnd.EndToEndPreEditAcceptanceTests;

namespace TravelCut.UnitTests.EndToEnd;

/// <summary>
/// Cenários J1 2, 5 e 6 do aceite (docs/ajustes/05). FIXTURES DE PORTA (ffprobe, Whisper, silencedetect): não são prova de mídia real.
/// </summary>
public sealed class EndToEndJ1ScenariosTests : IDisposable
{
    private const string RepClip = "rep01.mp4";
    private const string OkClip = "ok01.mp4";
    private const string NoSpeechClip = "semfala02.mp4";

    private readonly E2EWorkspace _ws = new();
    private readonly ITestOutputHelper _out;

    public EndToEndJ1ScenariosTests(ITestOutputHelper output) => _out = output;

    public void Dispose() => _ws.Dispose();

    /// <summary>10 s: "Não, não foi isso" 0–1,8; silêncio evidente 3,0–5,8 (2,8 s); fala 6,4–7,2.</summary>
    private static E2EPortFixtures RepFixtures() => new E2EPortFixtures().Add(new E2EMediaSpec(RepClip,
        E2EPortFixtures.Cfr2997(10),
        [
            E2EPortFixtures.Word(1, 0.0, 0.4, "Não,"),
            E2EPortFixtures.Word(2, 0.5, 0.9, "não"),
            E2EPortFixtures.Word(3, 1.0, 1.3, "foi"),
            E2EPortFixtures.Word(4, 1.3, 1.8, "isso"),
            E2EPortFixtures.Word(5, 6.4, 6.8, "Mas"),
            E2EPortFixtures.Word(6, 6.8, 7.2, "vamos"),
        ],
        [HighSilence(3.0, 5.8)]));

    private async Task<(E2EHost Host, Guid ProjectId, Guid MediaId, Dictionary<Guid, string> Names)> OpenRepAsync(string folder)
    {
        var root = _ws.ProjectRoot(folder);
        var host = E2EHost.Start(_ws.DatabasePath, RepFixtures());
        var projectId = await E2ESteps.CreateProjectAsync(host, root, folder);
        E2ESteps.PlaceMedia(root, [RepClip]);
        await E2ESteps.ScanAsync(host, projectId);
        var names = await E2ESteps.MediaNamesAsync(host, projectId);
        var status = await E2ESteps.AnalyzeAsync(host, projectId);
        Assert.Equal("completed", status.GetProperty("state").GetString());
        return (host, projectId, names.Single().Key, names);
    }

    [Fact]
    public async Task J1_2_RepeticaoComSignificado_NaoFoiIsso_NuncaEAutoCortada()
    {
        var (host, projectId, mediaId, _) = await OpenRepAsync("Viagem Repeticao");
        using var _ = host;

        var suggestions = await E2ESteps.SuggestionsAsync(host, projectId);
        var inPhrase = suggestions.Where(s => s.GetProperty("startSeconds").GetDouble() < 1.8).ToList();
        foreach (var s in inPhrase)
        {
            // Se o analisador a sugeriu, vai para Review: pendente, sem origem automática.
            Assert.Equal("Proposed", s.GetProperty("status").GetString());
            Assert.True(s.GetProperty("needsHumanReview").GetBoolean());
            Assert.NotEqual("auto", s.GetProperty("origin").GetString());
        }
        _out.WriteLine($"J1.2 sugestões na frase: {inPhrase.Count} ({string.Join(",", inPhrase.Select(s => s.GetProperty("kind").GetString()))}) -> Review; sem sugestão = preservada");

        var plan = await E2ESteps.LatestPlanAsync(host, projectId);
        Assert.All(plan.GetProperty("appliedCuts").EnumerateArray(), c => Assert.True(c.GetProperty("sourceInUs").GetInt64() >= 1_800_000));
        // A frase inteira permanece nos clipes, a partir do quadro 0.
        Assert.True(V1Covers(plan, mediaId, 0, 1_800_000));
        Assert.Contains(plan.GetProperty("clips").EnumerateArray(), c => c.GetProperty("sourceInUs").GetInt64() == 0 && c.GetProperty("sourceOutUs").GetInt64() >= 1_800_000);
        // O silêncio evidente vizinho continua sendo cortado (a política distingue os dois casos).
        Assert.Equal(1, plan.GetProperty("summary").GetProperty("autoCutsApplied").GetInt32());
        await AcceptanceMetrics.RecordAsync(_out, host, projectId, "J1-2 repeticao Nao,nao foi isso", restored: 0);
    }

    [Fact]
    public async Task J1_5_Reprocessar_NaoDuplicaDecisoesNemDestroiRestauracaoManual()
    {
        var (host, projectId, mediaId, names) = await OpenRepAsync("Viagem Reprocesso");
        using var _ = host;

        var silence = E2ESteps.Find(await E2ESteps.SuggestionsAsync(host, projectId), names, RepClip, 3.0);
        var silenceId = silence.GetProperty("suggestionId").GetGuid();
        Assert.Equal("auto", silence.GetProperty("origin").GetString());

        // Restauração manual do corte automático (rejeitar).
        await E2ESteps.DecideAsync(host, silenceId, "reject");
        var before = await E2ESteps.LatestPlanAsync(host, projectId);
        Assert.True(V1Covers(before, mediaId, 3_100_000, 5_700_000));
        var suggestionsBefore = await E2ESteps.SuggestionsAsync(host, projectId);
        var decisionsBefore = (await host.Ok("decision.list", new { projectId })).GetProperty("decisions").GetArrayLength();
        Assert.Equal(1, decisionsBefore);

        // Reprocessa sem cache (force) e depois com cache.
        Assert.Equal("completed", (await E2ESteps.AnalyzeAsync(host, projectId, force: true)).GetProperty("state").GetString());
        Assert.Equal("completed", (await E2ESteps.AnalyzeAsync(host, projectId)).GetProperty("state").GetString());

        var suggestionsAfter = await E2ESteps.SuggestionsAsync(host, projectId);
        Assert.Equal(suggestionsBefore.Count, suggestionsAfter.Count);
        // Nenhuma duplicata (tipo + início) após reprocessar. Com force, sugestões SEM decisão podem ser regeradas com novo id
        // (observado: a hesitação proposta); a sugestão com decisão mantém o mesmo id (verificado abaixo).
        Assert.Equal(suggestionsAfter.Count, suggestionsAfter
            .Select(s => (s.GetProperty("kind").GetString(), s.GetProperty("startSeconds").GetDouble())).Distinct().Count());
        Assert.Equal(1, (await host.Ok("decision.list", new { projectId })).GetProperty("decisions").GetArrayLength());

        var restored = suggestionsAfter.Single(s => s.GetProperty("suggestionId").GetGuid() == silenceId);
        Assert.Equal("Rejected", restored.GetProperty("status").GetString());

        var after = await E2ESteps.LatestPlanAsync(host, projectId);
        Assert.True(V1Covers(after, mediaId, 3_100_000, 5_700_000), "A restauração manual sobrevive ao reprocessamento.");
        Assert.Equal(0, after.GetProperty("summary").GetProperty("autoCutsApplied").GetInt32());
        Assert.Empty(after.GetProperty("appliedCuts").EnumerateArray());
        await AcceptanceMetrics.RecordAsync(_out, host, projectId, "J1-5 reprocesso (force) com restauracao manual", restored: 1);
    }

    [Fact]
    public async Task J1_6_FalhaParcialEmUmaMidia_AsOutrasSeguem()
    {
        var fixtures = new E2EPortFixtures()
            .Add(new E2EMediaSpec(OkClip, E2EPortFixtures.Cfr2997(12),
            [
                E2EPortFixtures.Word(1, 0.0, 0.5, "Olá"),
                E2EPortFixtures.Word(2, 0.5, 1.0, "pessoal"),
                E2EPortFixtures.Word(3, 5.0, 5.5, "chegamos"),
                E2EPortFixtures.Word(4, 5.5, 6.0, "hoje"),
            ],
            [HighSilence(1.6, 4.4)]))
            // Sem palavras: a transcrição desta mídia fica "unavailable" (falha parcial); o silêncio é detectado.
            .Add(new E2EMediaSpec(NoSpeechClip, E2EPortFixtures.Cfr2997(8), [], [HighSilence(1.0, 4.0)]));

        var root = _ws.ProjectRoot("Viagem Falha Parcial");
        using var host = E2EHost.Start(_ws.DatabasePath, fixtures);
        var projectId = await E2ESteps.CreateProjectAsync(host, root, "Viagem Falha Parcial");
        E2ESteps.PlaceMedia(root, [OkClip, NoSpeechClip]);
        await E2ESteps.ScanAsync(host, projectId);
        var names = await E2ESteps.MediaNamesAsync(host, projectId);
        var okId = names.Single(kv => kv.Value.EndsWith(OkClip, StringComparison.Ordinal)).Key;
        var badId = names.Single(kv => kv.Value.EndsWith(NoSpeechClip, StringComparison.Ordinal)).Key;

        var status = await E2ESteps.AnalyzeAsync(host, projectId);
        _out.WriteLine("J1.6 estado do job: " + status.GetProperty("state").GetString());
        Assert.NotEqual("failed", status.GetProperty("state").GetString());

        var okAnalysis = await host.Ok("analysis.getMedia", new MediaAnalysisRequest(projectId, okId));
        var badAnalysis = await host.Ok("analysis.getMedia", new MediaAnalysisRequest(projectId, badId));
        Assert.Equal("completed", okAnalysis.GetProperty("transcriptionStatus").GetString());
        Assert.NotEqual("completed", badAnalysis.GetProperty("transcriptionStatus").GetString());

        var suggestions = await E2ESteps.SuggestionsAsync(host, projectId);
        var bad = E2ESteps.Find(suggestions, names, NoSpeechClip, 1.0);
        Assert.True(bad.GetProperty("needsHumanReview").GetBoolean(), "Sem transcrição, ausência de fala não é verificável: Review.");
        Assert.NotEqual("auto", bad.GetProperty("origin").GetString());

        var plan = await E2ESteps.LatestPlanAsync(host, projectId);
        var cut = Assert.Single(plan.GetProperty("appliedCuts").EnumerateArray().ToList());
        Assert.Equal(okId, cut.GetProperty("mediaId").GetGuid());
        Assert.Equal("auto_trivial", cut.GetProperty("origin").GetString());
        Assert.False(V1Covers(plan, okId, 1_650_000, 4_350_000));
        await AcceptanceMetrics.RecordAsync(_out, host, projectId, "J1-6 falha parcial (1 de 2 sem transcricao)", restored: 0);
        Assert.True(plan.GetProperty("validation").GetProperty("isValid").GetBoolean());
    }
}
