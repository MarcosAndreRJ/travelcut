using System.Security.Cryptography;
using System.Text.Json;
using TravelCut.Application.Planning;
using TravelCut.Contracts.Dtos;
using TravelCut.UnitTests.Planning;
using Xunit;
using static TravelCut.UnitTests.EndToEnd.E2EScenarioData;

namespace TravelCut.UnitTests.EndToEnd;

/// <summary>
/// FIXTURES DE PORTA (ffprobe, Whisper, silencedetect): não são prova de mídia real (ver E2EPortFixtures).
/// Aceite F14.7/04 por IPC: importar/analisar, aprovar sugestões, editar a timeline manualmente (plan.saveManualRevision),
/// aprovar sugestão não relacionada e rodar a pós-análise; o plano mantém a edição manual; fechar e reabrir (novo DI/DbContext)
/// dá a mesma montagem (fingerprint do plano e da montagem idênticos); os originais continuam com o mesmo SHA-256.
/// </summary>
public sealed class EndToEndManualOverlayAcceptanceTests : IDisposable
{
    private readonly E2EWorkspace _ws = new();

    public void Dispose() => _ws.Dispose();

    private static EditPlanDto ToPlan(JsonElement payload)
    {
        Assert.True(PlanJson.TryDeserialize(payload.GetRawText(), out var plan), "plan.get deveria devolver um plano legível");
        return plan!;
    }

    private static string Sha256(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static bool Has(EditPlanDto p, Guid media, long inUs, long outUs) =>
        p.Clips.Any(c => c.MediaId == media && c.SourceInUs == inUs && c.SourceOutUs == outUs);

    [Fact]
    public async Task EdicaoManual_SobreviveAAprovacaoNaoRelacionadaEPosAnalise_E_AoReinicio_OriginaisIntactos()
    {
        var root = _ws.ProjectRoot("Viagem Overlay");
        var databasePath = _ws.DatabasePath;

        Guid projectId;
        EditPlanDto manualPlan;
        (Guid Media, long In, long Out) dropped;
        EditPlanDto before;
        string assemblyBefore;
        Dictionary<string, string> originalsBefore;

        using (var host = E2EHost.Start(databasePath, Happy()))
        {
            projectId = await E2ESteps.CreateProjectAsync(host, root, "Viagem Overlay");
            E2ESteps.PlaceMedia(root, HappyFiles);
            originalsBefore = HappyFiles.ToDictionary(f => f, f => Sha256(Path.Combine(root, "1-Bruto", f)));
            await E2ESteps.ScanAsync(host, projectId);
            await E2ESteps.AnalyzeAsync(host, projectId);

            var names = await E2ESteps.MediaNamesAsync(host, projectId);
            var suggestions = await E2ESteps.SuggestionsAsync(host, projectId);
            await E2ESteps.DecideAsync(host, E2ESteps.Find(suggestions, names, HappyClipA, 1.0).GetProperty("suggestionId").GetGuid(), "approve");

            // edição manual da timeline: remove o último par de clipes (clip02), baseada na revisão vigente
            var current = ToPlan(await E2ESteps.LatestPlanAsync(host, projectId));
            var last = current.Clips.OrderBy(c => c.TimelineStartFrame).Last();
            dropped = (last.MediaId, last.SourceInUs, last.SourceOutUs);
            var saved = await host.Ok("plan.saveManualRevision", new SavePlanRevisionRequest(projectId, current.Revision, ManualPlanEdits.DropLastPair(current), null, "user"));
            manualPlan = ToPlan(saved);
            Assert.Equal(current.Revision + 1, manualPlan.Revision);
            Assert.False(Has(manualPlan, dropped.Media, dropped.In, dropped.Out));
            Assert.NotEmpty(manualPlan.ManualEdits!);

            // sugestão não relacionada (clip01, pausa 6,0 s; a edição manual é em clip02) + pós-análise (reanálise em cache + generate)
            await E2ESteps.DecideAsync(host, E2ESteps.Find(suggestions, names, HappyClipA, 6.0).GetProperty("suggestionId").GetGuid(), "approve");
            await E2ESteps.AnalyzeAsync(host, projectId);
            await E2ESteps.GeneratePlanAsync(host, projectId);

            before = ToPlan(await E2ESteps.LatestPlanAsync(host, projectId));
            Assert.True(before.Revision > manualPlan.Revision, "a decisão não relacionada deve gerar uma revisão nova");
            Assert.Equal("planner-rules/1+manual", before.Author);
            Assert.False(Has(before, dropped.Media, dropped.In, dropped.Out), "plan.get deve manter a edição manual");
            Assert.Equal(manualPlan.ManualEdits!.Select(e => e.EditId), before.ManualEdits!.Select(e => e.EditId));
            assemblyBefore = (await E2ESteps.PrepareApplyAsync(host, projectId)).GetProperty("assemblyFingerprint").GetString()!;
        }

        // Reinício: novo DI/DbContext sobre o mesmo banco; nada em memória é reaproveitado.
        using var reopened = E2EHost.Start(databasePath, Happy());
        var after = ToPlan(await E2ESteps.LatestPlanAsync(reopened, projectId));

        Assert.Equal(before.Revision, after.Revision);
        Assert.Equal(before.PlanFingerprint, after.PlanFingerprint);
        Assert.Equal(before.Author, after.Author);
        Assert.False(Has(after, dropped.Media, dropped.In, dropped.Out));
        Assert.Equal(before.ManualEdits!.Select(e => e.EditId), after.ManualEdits!.Select(e => e.EditId));
        Assert.Equal(
            before.Clips.Select(c => (c.MediaId, c.SourceInUs, c.SourceOutUs, c.TimelineStartFrame)),
            after.Clips.Select(c => (c.MediaId, c.SourceInUs, c.SourceOutUs, c.TimelineStartFrame)));
        Assert.Equal(assemblyBefore, (await E2ESteps.PrepareApplyAsync(reopened, projectId)).GetProperty("assemblyFingerprint").GetString());

        // regenerar após o reinício é idempotente (mesma revisão, mesmo fingerprint)
        var again = await E2ESteps.GeneratePlanAsync(reopened, projectId);
        Assert.Equal(before.Revision, again.GetProperty("revision").GetInt32());
        Assert.Equal(before.PlanFingerprint, again.GetProperty("planFingerprint").GetString());

        // originais intactos
        foreach (var (file, sha) in originalsBefore)
            Assert.Equal(sha, Sha256(Path.Combine(root, "1-Bruto", file)));
    }
}
