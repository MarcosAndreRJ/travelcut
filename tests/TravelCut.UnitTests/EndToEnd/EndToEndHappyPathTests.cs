using System.Text.Json;
using TravelCut.Contracts.Dtos;
using Xunit;
using static TravelCut.UnitTests.EndToEnd.E2EScenarioData;

namespace TravelCut.UnitTests.EndToEnd;

/// <summary>
/// Cenário 1: fluxo feliz do projeto ao editor simulado, dirigido só por IPC.
/// Fixtures de porta: ffprobe, Whisper e silencedetect (ver E2EPortFixtures). Não é prova de mídia real.
/// </summary>
public sealed class EndToEndHappyPathTests : IDisposable
{
    private readonly E2EWorkspace _ws = new();

    public void Dispose() => _ws.Dispose();

    [Fact]
    public async Task FluxoFeliz_DoProjetoAoEditorSimulado_OrigemIntactaEArtefatosSoEmTrabalho()
    {
        var root = _ws.ProjectRoot("Viagem Feliz");
        using var host = E2EHost.Start(_ws.DatabasePath, Happy());

        // Projeto e registro das mídias em 1-Bruto (cópia do usuário).
        var projectId = await E2ESteps.CreateProjectAsync(host, root, "Viagem Feliz");
        E2ESteps.PlaceMedia(root, HappyFiles);
        var originBefore = E2EWorkspace.Tree(Path.Combine(root, "1-Bruto"));
        var assetsBefore = E2EWorkspace.Tree(Path.Combine(root, "2-Assets"));
        var projectBefore = E2EWorkspace.Tree(root);

        // Inventário.
        var scan = await E2ESteps.ScanAsync(host, projectId);
        Assert.Equal(2, scan.GetProperty("newAssetsAdded").GetInt32());
        var names = await E2ESteps.MediaNamesAsync(host, projectId);
        Assert.Equal(2, names.Count);
        var mediaA = names.Single(kv => kv.Value.EndsWith(HappyClipA, StringComparison.Ordinal)).Key;

        // Análise (transcrição e silêncios pelas fixtures; candidatos e heurísticas pelo código real).
        var status = await E2ESteps.AnalyzeAsync(host, projectId);
        Assert.Equal("completed", status.GetProperty("state").GetString());

        var analysis = await host.Ok("analysis.getMedia", new MediaAnalysisRequest(projectId, mediaA));
        Assert.Equal("completed", analysis.GetProperty("transcriptionStatus").GetString());
        Assert.Equal("completed", analysis.GetProperty("silenceStatus").GetString());

        // Sugestões: 2 silêncios em clip01 + 1 em clip02.
        var suggestions = await E2ESteps.SuggestionsAsync(host, projectId);
        Assert.Equal(3, suggestions.Count);
        var silenceA = E2ESteps.Find(suggestions, names, HappyClipA, 1.0);
        var silenceB = E2ESteps.Find(suggestions, names, HappyClipA, 6.0);
        var silenceIdA = silenceA.GetProperty("suggestionId").GetGuid();
        var silenceIdB = silenceB.GetProperty("suggestionId").GetGuid();

        // Decisões: aprovar um silêncio, rejeitar o outro.
        var approve = await E2ESteps.DecideAsync(host, silenceIdA, "approve");
        Assert.Equal(1, approve.GetProperty("recordedCount").GetInt32());
        var reject = await E2ESteps.DecideAsync(host, silenceIdB, "reject");
        Assert.Equal(1, reject.GetProperty("recordedCount").GetInt32());
        var decisions = await host.Ok("decision.list", new { projectId });
        Assert.Equal(2, decisions.GetProperty("decisions").GetArrayLength());

        // Plano: só o silêncio aprovado vira corte, com a validação sem erros.
        var generated = await E2ESteps.GeneratePlanAsync(host, projectId);
        Assert.Equal(1, generated.GetProperty("revision").GetInt32());
        var plan = await E2ESteps.LatestPlanAsync(host, projectId);
        Assert.Equal(1, plan.GetProperty("summary").GetProperty("approvedCutsApplied").GetInt32());
        Assert.Equal(0, plan.GetProperty("summary").GetProperty("autoCutsApplied").GetInt32());

        var appliedCut = Assert.Single(plan.GetProperty("appliedCuts").EnumerateArray().ToList());
        Assert.Equal(silenceIdA, appliedCut.GetProperty("suggestionId").GetGuid());
        Assert.Equal("approved_by_user", appliedCut.GetProperty("origin").GetString());
        // Silêncio A = 1,0–3,6 s (µs). Tolerância de um quadro para arredondamento de timeline.
        Assert.InRange(appliedCut.GetProperty("sourceInUs").GetInt64(), 1_000_000 - 40_000, 1_000_000 + 40_000);
        Assert.InRange(appliedCut.GetProperty("sourceOutUs").GetInt64(), 3_600_000 - 40_000, 3_600_000 + 40_000);

        var validation = plan.GetProperty("validation");
        Assert.True(validation.GetProperty("isValid").GetBoolean());
        Assert.DoesNotContain(validation.GetProperty("issues").EnumerateArray(),
            i => i.GetProperty("severity").GetString() == "error");

        // Editor simulado: dry-run, aplicação e histórico.
        var preview = await E2ESteps.PrepareApplyAsync(host, projectId);
        Assert.True(preview.GetProperty("canApply").GetBoolean());
        Assert.Equal(0, preview.GetProperty("blockingReasons").GetArrayLength());

        var applied = await E2ESteps.ApplyAsync(host, projectId, preview, "e2e-feliz-01");
        Assert.Equal("SIMULATED", applied.GetProperty("status").GetString());
        Assert.True(applied.GetProperty("isSimulated").GetBoolean());
        Assert.Equal("mock", applied.GetProperty("adapterMode").GetString());
        Assert.Equal(JsonValueKind.Null, applied.GetProperty("snapshotRelativePath").ValueKind);

        var recordRelative = applied.GetProperty("recordRelativePath").GetString()!;
        Assert.StartsWith("3-Trabalho", recordRelative);
        Assert.True(File.Exists(Path.Combine(root, recordRelative)), "O registro da aplicação deve existir em 3-Trabalho/editor.");

        var applications = (await E2ESteps.ApplicationsAsync(host, projectId)).GetProperty("applications");
        var record = Assert.Single(applications.EnumerateArray().ToList());
        Assert.Equal(applied.GetProperty("applicationId").GetGuid(), record.GetProperty("applicationId").GetGuid());
        Assert.Equal("SIMULATED", record.GetProperty("status").GetString());

        // Origem: 1-Bruto e 2-Assets com o mesmo conteúdo (tamanho, SHA-256 e data) de antes.
        Assert.Equal(originBefore, E2EWorkspace.Tree(Path.Combine(root, "1-Bruto")));
        Assert.Equal(assetsBefore, E2EWorkspace.Tree(Path.Combine(root, "2-Assets")));

        // Artefatos: tudo o que apareceu no projeto está em 3-Trabalho; nada removido ou alterado fora dele.
        var (added, removed, changed) = E2EWorkspace.Diff(projectBefore, E2EWorkspace.Tree(root));
        Assert.NotEmpty(added);
        Assert.All(added, p => Assert.Equal("3-Trabalho", E2EWorkspace.TopSegment(p)));
        Assert.Empty(removed);
        Assert.Empty(changed);
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(root, "4-Final")));
    }
}
