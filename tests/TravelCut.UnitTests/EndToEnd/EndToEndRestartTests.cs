using System.Text.Json;
using TravelCut.Contracts.Dtos;
using Xunit;
using static TravelCut.UnitTests.EndToEnd.E2EScenarioData;

namespace TravelCut.UnitTests.EndToEnd;

/// <summary>
/// FIXTURES DE PORTA (ffprobe, Whisper, silencedetect): não são prova de mídia real (ver E2EPortFixtures).
/// Cenário 2: o ServiceProvider é descartado e outro é criado sobre o MESMO banco e o MESMO projeto.
/// Decisões, plano (revisão e fingerprint) e histórico do editor devem ser iguais, e plan.generate sem
/// mudanças deve devolver a mesma revisão (idempotente). Fixtures de porta são novas no reinício.
/// </summary>
public sealed class EndToEndRestartTests : IDisposable
{
    private readonly E2EWorkspace _ws = new();

    public void Dispose() => _ws.Dispose();

    [Fact]
    public async Task Reinicio_PreservaDecisoesPlanoEHistoricoDoEditor_E_PlanGenerateEIdempotente()
    {
        var root = _ws.ProjectRoot("Viagem Reinicio");
        var databasePath = _ws.DatabasePath;

        Guid projectId;
        StateSnapshot before;
        using (var host = E2EHost.Start(databasePath, Happy()))
        {
            projectId = await E2ESteps.CreateProjectAsync(host, root, "Viagem Reinicio");
            E2ESteps.PlaceMedia(root, HappyFiles);
            await E2ESteps.ScanAsync(host, projectId);
            await E2ESteps.AnalyzeAsync(host, projectId);

            var names = await E2ESteps.MediaNamesAsync(host, projectId);
            var suggestions = await E2ESteps.SuggestionsAsync(host, projectId);
            await E2ESteps.DecideAsync(host, E2ESteps.Find(suggestions, names, HappyClipA, 1.0).GetProperty("suggestionId").GetGuid(), "approve");
            await E2ESteps.DecideAsync(host, E2ESteps.Find(suggestions, names, HappyClipA, 6.0).GetProperty("suggestionId").GetGuid(), "reject");

            await E2ESteps.GeneratePlanAsync(host, projectId);
            var preview = await E2ESteps.PrepareApplyAsync(host, projectId);
            await E2ESteps.ApplyAsync(host, projectId, preview, "reinicio-01");

            before = await CaptureAsync(host, projectId, names);
        }

        // Reinício: novo DI sobre o mesmo arquivo de banco; nenhum estado em memória é reaproveitado.
        using var restarted = E2EHost.Start(databasePath, Happy());
        var restartedNames = await E2ESteps.MediaNamesAsync(restarted, projectId);
        var after = await CaptureAsync(restarted, projectId, restartedNames);

        Assert.Equal(before.Decisions, after.Decisions);
        Assert.Equal(before.Suggestions, after.Suggestions);
        Assert.Equal(before.Revision, after.Revision);
        Assert.Equal(before.PlanFingerprint, after.PlanFingerprint);
        Assert.Equal(before.AppliedCuts, after.AppliedCuts);
        Assert.Equal(before.Applications, after.Applications);
        Assert.Equal(before.Analysis, after.Analysis);

        // plan.generate sem mudanças: mesma revisão e mesmo fingerprint; nenhuma revisão nova.
        var again = await E2ESteps.GeneratePlanAsync(restarted, projectId);
        Assert.Equal(before.Revision, again.GetProperty("revision").GetInt32());
        Assert.Equal(before.PlanFingerprint, again.GetProperty("planFingerprint").GetString());

        var list = await restarted.Ok("plan.list", new { projectId });
        Assert.Equal(1, list.GetProperty("revisions").GetArrayLength());
    }

    /// <summary>Estado observável pelo IPC, normalizado (listas ordenadas; sem campos com data de validação).</summary>
    private sealed record StateSnapshot(
        string Decisions,
        string Suggestions,
        int Revision,
        string PlanFingerprint,
        string AppliedCuts,
        string Applications,
        string Analysis);

    private static async Task<StateSnapshot> CaptureAsync(E2EHost host, Guid projectId, IReadOnlyDictionary<Guid, string> names)
    {
        var decisions = await host.Ok("decision.list", new { projectId });
        var suggestions = await host.Ok("suggestions.list", new { projectId });
        var plan = await E2ESteps.LatestPlanAsync(host, projectId);
        var applications = (await E2ESteps.ApplicationsAsync(host, projectId)).GetProperty("applications");
        var mediaA = names.Single(kv => kv.Value.EndsWith(HappyClipA, StringComparison.Ordinal)).Key;
        var analysis = await host.Ok("analysis.getMedia", new MediaAnalysisRequest(projectId, mediaA));

        return new StateSnapshot(
            Decisions: Sorted(decisions.GetProperty("decisions")),
            Suggestions: Sorted(suggestions.GetProperty("suggestions")),
            Revision: plan.GetProperty("revision").GetInt32(),
            PlanFingerprint: plan.GetProperty("planFingerprint").GetString()!,
            AppliedCuts: plan.GetProperty("appliedCuts").GetRawText(),
            Applications: Sorted(applications),
            Analysis: analysis.GetRawText());
    }

    private static string Sorted(JsonElement array) =>
        string.Join("\n", array.EnumerateArray().Select(e => e.GetRawText()).OrderBy(s => s, StringComparer.Ordinal));
}
