using System.Text.Json;
using TravelCut.Contracts.Dtos;
using Xunit;

namespace TravelCut.UnitTests.EndToEnd;

/// <summary>
/// Passos do fluxo do usuário, todos por IPC (mesmos tipos de mensagem da UI).
/// Não há atalho para repositórios: o teste só observa o estado pelas próprias respostas IPC e pelo disco.
/// </summary>
public static class E2ESteps
{
    private static readonly string[] TerminalStates = ["completed", "completed_with_issues", "failed", "canceled"];

    public static async Task<Guid> CreateProjectAsync(E2EHost host, string root, string name)
    {
        var payload = await host.Ok("project.create", new CreateProjectRequest(RootPath: root, Name: name));
        return payload.GetProperty("project").GetProperty("projectId").GetGuid();
    }

    /// <summary>Simula a cópia do usuário para 1-Bruto (bytes sintéticos, não é mídia real).</summary>
    public static void PlaceMedia(string root, IEnumerable<string> fileNames)
    {
        int seed = 1;
        foreach (var name in fileNames)
        {
            E2EWorkspace.WriteSyntheticMedia(Path.Combine(root, "1-Bruto", name), seed++);
        }
    }

    public static async Task<JsonElement> ScanAsync(E2EHost host, Guid projectId) =>
        await host.Ok("media.scan", new { projectId, computeFullSha = false });

    /// <summary>mediaId → caminho relativo (como a biblioteca registra).</summary>
    public static async Task<Dictionary<Guid, string>> MediaNamesAsync(E2EHost host, Guid projectId)
    {
        var payload = await host.Ok("media.list", new { projectId });
        var array = payload.ValueKind == JsonValueKind.Array ? payload : payload.GetProperty("media");
        return array.EnumerateArray()
            .ToDictionary(m => m.GetProperty("mediaId").GetGuid(), m => m.GetProperty("relativePath").GetString()!);
    }

    /// <summary>Inicia a análise (todas as etapas, sem force) e aguarda o estado final. Devolve o status final.</summary>
    public static async Task<JsonElement> AnalyzeAsync(E2EHost host, Guid projectId, bool force = false)
    {
        var started = await host.Ok("pipeline.start", new StartPipelineRequest(projectId, null, null, null, force));
        var jobId = started.GetProperty("jobId").GetGuid();

        var status = await WaitTerminalAsync(host, projectId);
        Assert.Equal(jobId, status.GetProperty("jobId").GetGuid());
        await WaitPostAnalysisSettledAsync(host, projectId, status);
        return status;
    }

    /// <summary>
    /// F14.8/P01: ao fim de uma análise completed/completed_with_issues o orquestrador gera o plano em segundo plano.
    /// Espera essa execução terminar (finishedAtUtc >= fim do pipeline) para que o teste seja determinístico.
    /// </summary>
    public static async Task WaitPostAnalysisSettledAsync(E2EHost host, Guid projectId, JsonElement pipelineStatus, TimeSpan? timeout = null)
    {
        var state = pipelineStatus.GetProperty("state").GetString();
        if (state is not ("completed" or "completed_with_issues")) return;
        var pipelineFinished = pipelineStatus.GetProperty("finishedAtUtc").GetDateTime();

        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(30));
        while (true)
        {
            var status = await host.Ok("pipeline.status", new ProjectRequest(projectId));
            if (status.TryGetProperty("postAnalysis", out var post) && post.ValueKind == JsonValueKind.Object
                && post.GetProperty("state").GetString() is not ("idle" or "running")
                && post.GetProperty("finishedAtUtc").ValueKind == JsonValueKind.String
                && post.GetProperty("finishedAtUtc").GetDateTime() >= pipelineFinished)
            {
                return;
            }
            if (DateTime.UtcNow > deadline) Assert.Fail("O pós-análise não terminou no prazo: " + status.GetRawText());
            await Task.Delay(20);
        }
    }

    public static async Task<JsonElement> WaitTerminalAsync(E2EHost host, Guid projectId, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(30));
        while (true)
        {
            var status = await host.Ok("pipeline.status", new ProjectRequest(projectId));
            var state = status.GetProperty("state").GetString();
            if (state is not null && TerminalStates.Contains(state)) return status;
            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail("A análise não terminou no prazo: " + status.GetRawText());
            }
            await Task.Delay(20);
        }
    }

    public static async Task<List<JsonElement>> SuggestionsAsync(E2EHost host, Guid projectId)
    {
        var payload = await host.Ok("suggestions.list", new { projectId });
        return payload.GetProperty("suggestions").EnumerateArray().Select(s => s.Clone()).ToList();
    }

    /// <summary>Localiza uma sugestão pelo arquivo (sufixo do caminho) e pelo início do intervalo.</summary>
    public static JsonElement Find(IEnumerable<JsonElement> suggestions, IReadOnlyDictionary<Guid, string> mediaNames,
        string fileName, double startSeconds) =>
        suggestions.Single(s =>
            mediaNames[s.GetProperty("mediaId").GetGuid()].EndsWith(fileName, StringComparison.Ordinal)
            && Math.Abs(s.GetProperty("startSeconds").GetDouble() - startSeconds) < 0.001);

    public static async Task<JsonElement> DecideAsync(E2EHost host, Guid suggestionId, string action) =>
        await host.Ok("decision.submit", new SubmitDecisionRequest(suggestionId, action, "revisor-e2e", "decisão do teste E2E"));

    public static async Task<JsonElement> GeneratePlanAsync(E2EHost host, Guid projectId) =>
        await host.Ok("plan.generate", new GeneratePlanRequest(projectId, null, null, null));

    public static async Task<JsonElement> LatestPlanAsync(E2EHost host, Guid projectId) =>
        await host.Ok("plan.get", new GetPlanRequest(projectId, null));

    public static async Task<JsonElement> PrepareApplyAsync(E2EHost host, Guid projectId) =>
        await host.Ok("editor.prepareApply", new PrepareEditorApplyRequest(projectId, null));

    public static async Task<JsonElement> ApplyAsync(E2EHost host, Guid projectId, JsonElement preview, string idempotencyKey) =>
        await host.Ok("editor.apply", new ApplyEditorPlanRequest(
            ProjectId: projectId,
            Revision: preview.GetProperty("revision").GetInt32(),
            AssemblyFingerprint: preview.GetProperty("assemblyFingerprint").GetString()!,
            ConfirmProjectSwitch: true,
            IdempotencyKey: idempotencyKey));

    public static async Task<JsonElement> ApplicationsAsync(E2EHost host, Guid projectId) =>
        await host.Ok("editor.applications", new ListEditorApplicationsRequest(projectId));
}
