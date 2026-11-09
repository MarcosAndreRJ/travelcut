using System.Text.Json;
using TravelCut.Application.Audio;
using TravelCut.Contracts.Dtos;
using Xunit;
using Xunit.Abstractions;

namespace TravelCut.UnitTests.EndToEnd;

/// <summary>
/// Cenário de aceite (docs/ajustes/05). FIXTURES DE PORTA (ffprobe, Whisper, silencedetect): não são prova de mídia real.
/// Marca apenas integration_pass; host_pass depende de mídia e DaVinci reais no host.
/// Silêncios "high" com fala a mais de 0,5 s das bordas são os únicos que a política autoaplica.
/// </summary>
public sealed class EndToEndPreEditAcceptanceTests : IDisposable
{
    private const string AutoClip = "auto01.mp4";
    private readonly E2EWorkspace _ws = new();
    private readonly ITestOutputHelper _out;

    public EndToEndPreEditAcceptanceTests(ITestOutputHelper output) => _out = output;

    public void Dispose() => _ws.Dispose();

    /// <summary>Silêncio de confiança "high" (elegível à autoaplicação quando a política confirma).</summary>
    internal static VadSilenceSegmentDto HighSilence(double start, double end) => new(start, end, end - start, -30.0, "high");

    /// <summary>12 s: fala 0–1,0; silêncio evidente 1,6–4,4 (2,8 s); fala 5,0–6,0.</summary>
    internal static E2EPortFixtures AutoFixtures() => new E2EPortFixtures().Add(new E2EMediaSpec(AutoClip,
        E2EPortFixtures.Cfr2997(12),
        [
            E2EPortFixtures.Word(1, 0.0, 0.5, "Olá"),
            E2EPortFixtures.Word(2, 0.5, 1.0, "pessoal"),
            E2EPortFixtures.Word(3, 5.0, 5.5, "chegamos"),
            E2EPortFixtures.Word(4, 5.5, 6.0, "hoje"),
        ],
        [HighSilence(1.6, 4.4)]));

    /// <summary>True se algum clipe de V1 da mídia cobre parte do intervalo de fonte (µs).</summary>
    internal static bool V1Covers(JsonElement plan, Guid mediaId, long startUs, long endUs) =>
        plan.GetProperty("clips").EnumerateArray().Any(c =>
            c.GetProperty("trackId").GetString() == "V1"
            && c.GetProperty("mediaId").GetGuid() == mediaId
            && c.GetProperty("sourceInUs").GetInt64() < endUs
            && c.GetProperty("sourceOutUs").GetInt64() > startUs);

    [Fact]
    public async Task Cenario1_ImportarProcessarPreEditarDesfazerRefazerReabrir_OriginaisIntactos()
    {
        var root = _ws.ProjectRoot("Viagem Aceite");
        var databasePath = _ws.DatabasePath;
        const long cutIn = 1_600_000, cutOut = 4_400_000;

        Guid projectId, mediaId, suggestionId;
        string fingerprintAfterRedo;
        SortedDictionary<string, string> originBefore;
        using (var host = E2EHost.Start(databasePath, AutoFixtures()))
        {
            projectId = await E2ESteps.CreateProjectAsync(host, root, "Viagem Aceite");
            E2ESteps.PlaceMedia(root, [AutoClip]);
            originBefore = E2EWorkspace.Tree(Path.Combine(root, "1-Bruto"));
            await E2ESteps.ScanAsync(host, projectId);
            var names = await E2ESteps.MediaNamesAsync(host, projectId);
            mediaId = names.Single().Key;

            var status = await E2ESteps.AnalyzeAsync(host, projectId);
            Assert.Equal("completed", status.GetProperty("state").GetString());

            // Candidato + corte automático.
            var suggestions = await E2ESteps.SuggestionsAsync(host, projectId);
            var silence = E2ESteps.Find(suggestions, names, AutoClip, 1.6);
            suggestionId = silence.GetProperty("suggestionId").GetGuid();
            Assert.False(silence.GetProperty("needsHumanReview").GetBoolean());
            Assert.Equal("auto", silence.GetProperty("origin").GetString());

            var plan = await E2ESteps.LatestPlanAsync(host, projectId);
            Assert.Equal(1, plan.GetProperty("summary").GetProperty("autoCutsApplied").GetInt32());
            var cut = Assert.Single(plan.GetProperty("appliedCuts").EnumerateArray().ToList());
            Assert.Equal("auto_trivial", cut.GetProperty("origin").GetString());
            Assert.Equal(suggestionId, cut.GetProperty("suggestionId").GetGuid());
            Assert.False(V1Covers(plan, mediaId, cutIn + 50_000, cutOut - 50_000), "O trecho removido não pode estar nos clipes do plano.");
            Assert.True(V1Covers(plan, mediaId, 0, 1_000_000), "A fala inicial deve permanecer.");
            Assert.True(V1Covers(plan, mediaId, 5_000_000, 6_000_000), "A fala final deve permanecer.");
            long removedAuto = plan.GetProperty("summary").GetProperty("removedSourceUs").GetInt64();
            Assert.InRange(removedAuto, 2_700_000, 2_900_000);

            // Aprovar manualmente, desfazer: o trecho volta e a automação NÃO reaplica (decisão explícita prevalece, E07).
            await E2ESteps.DecideAsync(host, suggestionId, "approve");
            plan = await E2ESteps.LatestPlanAsync(host, projectId);
            Assert.Equal("approved_by_user", Assert.Single(plan.GetProperty("appliedCuts").EnumerateArray().ToList()).GetProperty("origin").GetString());
            Assert.False(V1Covers(plan, mediaId, cutIn + 50_000, cutOut - 50_000));

            await host.Ok("decision.undo", new UndoDecisionRequest(suggestionId));
            plan = await E2ESteps.LatestPlanAsync(host, projectId);
            Assert.Empty(plan.GetProperty("appliedCuts").EnumerateArray());
            Assert.True(V1Covers(plan, mediaId, cutIn + 50_000, cutOut - 50_000), "Desfazer devolve o trecho.");
            Assert.Equal(0, plan.GetProperty("summary").GetProperty("removedSourceUs").GetInt64());
            Assert.Contains(plan.GetProperty("pending").EnumerateArray(), p => p.GetProperty("suggestionId").GetGuid() == suggestionId);

            // Refazer: removido de novo.
            await host.Ok("decision.redo", new RedoDecisionRequest(suggestionId));
            plan = await E2ESteps.LatestPlanAsync(host, projectId);
            Assert.Single(plan.GetProperty("appliedCuts").EnumerateArray().ToList());
            Assert.False(V1Covers(plan, mediaId, cutIn + 50_000, cutOut - 50_000), "Refazer remove o trecho de novo.");
            Assert.True(plan.GetProperty("validation").GetProperty("isValid").GetBoolean());
            fingerprintAfterRedo = plan.GetProperty("planFingerprint").GetString()!;
            await AcceptanceMetrics.RecordAsync(_out, host, projectId, "G7-1 silencio auto + undo/redo", restored: 1);
        }

        // Reabrir: novo escopo de DI/DbContext sobre o mesmo banco.
        using (var reopened = E2EHost.Start(databasePath, AutoFixtures()))
        {
            var decisions = (await reopened.Ok("decision.list", new { projectId })).GetProperty("decisions").EnumerateArray().ToList();
            Assert.Equal(3, decisions.Count); // approve, undo, redo (append-only)
            var plan = await E2ESteps.LatestPlanAsync(reopened, projectId);
            Assert.Equal(fingerprintAfterRedo, plan.GetProperty("planFingerprint").GetString());
            Assert.False(V1Covers(plan, mediaId, cutIn + 50_000, cutOut - 50_000));
            var again = await E2ESteps.GeneratePlanAsync(reopened, projectId);
            Assert.Equal(fingerprintAfterRedo, again.GetProperty("planFingerprint").GetString());
        }

        // Originais: mesmo tamanho, SHA-256 e data.
        Assert.Equal(originBefore, E2EWorkspace.Tree(Path.Combine(root, "1-Bruto")));
    }
}
