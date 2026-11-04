using System.ComponentModel;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TravelCut.Contracts.Dtos;
using TravelCut.Mcp.Facade;

namespace TravelCut.Mcp.Tools;

/// <summary>
/// Ferramentas somente leitura. Cada uma chama o IpcDispatcher com o mesmo tipo de requisição da UI.
/// Sempre registradas; não alteram projeto, banco, arquivos nem editor.
/// </summary>
[McpServerToolType]
public sealed class TravelCutReadTools(TravelCutMcpFacade facade)
{
    [McpServerTool(Name = "list_projects", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Lista os projetos autorizados entre os 10 mais recentes do TravelCut. Somente leitura. Sem projetos autorizados, a lista vem vazia.")]
    public async Task<CallToolResult> ListProjects(CancellationToken cancellationToken = default)
    {
        var outcome = await facade.ExecuteReadAsync("list_projects", async ct =>
        {
            var response = await facade.DispatchAsync(TravelCutMcpFacade.IpcTypes.ProjectRecent, null, ct);
            return McpOutcome.FromResponse(response, payload => payload is RecentProjectsResponse recents
                ? new RecentProjectsResponse(recents.Projects.Where(p => facade.IsAuthorized(p.ProjectId)).ToList())
                : payload);
        }, cancellationToken);
        return ToolResults.From(outcome);
    }

    [McpServerTool(Name = "get_inventory", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Inventário de mídias de um projeto autorizado: id, caminho relativo, tipo, duração e status de metadados. Somente leitura.")]
    public async Task<CallToolResult> GetInventory(
        [Description("GUID do projeto autorizado.")] string projectId,
        CancellationToken cancellationToken = default)
    {
        var outcome = await facade.ExecuteReadAsync("get_inventory", async ct =>
        {
            var id = facade.RequireAuthorizedProject(projectId);
            var response = await facade.DispatchAsync(TravelCutMcpFacade.IpcTypes.MediaList, new ProjectRequest(id), ct);
            return McpOutcome.FromResponse(response, payload => payload is IEnumerable<MediaAssetDto> list
                ? new { media = list }
                : payload is not null && payload.GetType().GetProperty("media") is null
                    ? new { media = payload }
                    : payload);
        }, cancellationToken);
        return ToolResults.From(outcome);
    }

    [McpServerTool(Name = "get_transcript", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Transcrição de uma mídia do projeto: status, motivo e palavras com tempos em segundos da fonte. Somente leitura. Conteúdo sensível: não copie para logs.")]
    public async Task<CallToolResult> GetTranscript(
        [Description("GUID do projeto autorizado.")] string projectId,
        [Description("GUID da mídia dentro do projeto.")] string mediaId,
        CancellationToken cancellationToken = default)
    {
        var outcome = await facade.ExecuteReadAsync("get_transcript", async ct =>
        {
            var pid = facade.RequireAuthorizedProject(projectId);
            var mid = TravelCutMcpFacade.ParseGuid(mediaId, "mediaId");
            // Mídia de outro projeto: MEDIA_NOT_FOUND (o IPC devolveria uma análise vazia "not_run" para ela).
            await facade.RequireMediaInProjectAsync(pid, [mid], ct);
            var response = await facade.DispatchAsync(
                TravelCutMcpFacade.IpcTypes.AnalysisGetMedia, new MediaAnalysisRequest(pid, mid), ct);
            return McpOutcome.FromResponse(response, payload => payload is MediaAnalysisDto analysis
                ? new
                {
                    // Nomes JSON explícitos: propriedades de tipo anônimo não recebem a política de nomes do serializador.
                    mediaId = analysis.MediaId,
                    transcriptionStatus = analysis.TranscriptionStatus,
                    transcriptionReason = analysis.TranscriptionReason,
                    transcript = analysis.Transcript,
                }
                : payload);
        }, cancellationToken);
        return ToolResults.From(outcome);
    }

    [McpServerTool(Name = "get_suggestions", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Sugestões de corte de um projeto com o estado efetivo (Proposed, Approved, Rejected, Deferred). Filtro opcional por status. Somente leitura.")]
    public async Task<CallToolResult> GetSuggestions(
        [Description("GUID do projeto autorizado.")] string projectId,
        [Description("Filtro opcional: Proposed, Approved, Rejected ou Deferred.")] string? status = null,
        CancellationToken cancellationToken = default)
    {
        var outcome = await facade.ExecuteReadAsync("get_suggestions", async ct =>
        {
            var pid = facade.RequireAuthorizedProject(projectId);
            var filter = TravelCutMcpFacade.RequireStatusFilter(status);
            var response = await facade.DispatchAsync(
                TravelCutMcpFacade.IpcTypes.SuggestionsList, new ProjectRequest(pid), ct);
            return McpOutcome.FromResponse(response, payload => filter is null ? payload : FilterByStatus(payload, filter));
        }, cancellationToken);
        return ToolResults.From(outcome);
    }

    [McpServerTool(Name = "get_edit_plan", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Plano editorial de um projeto (EditPlanDto, schema editplan/1). Sem revisão, devolve a mais recente. Sem plano gerado, devolve null (não é erro). Somente leitura.")]
    public async Task<CallToolResult> GetEditPlan(
        [Description("GUID do projeto autorizado.")] string projectId,
        [Description("Revisão desejada (inteiro ≥ 1). Omitida = mais recente.")] int? revision = null,
        CancellationToken cancellationToken = default)
    {
        var outcome = await facade.ExecuteReadAsync("get_edit_plan", async ct =>
        {
            var pid = facade.RequireAuthorizedProject(projectId);
            if (revision is < 1)
            {
                throw new McpFailure(McpErrorCodes.InvalidArgument, "revision deve ser um inteiro ≥ 1.");
            }
            var response = await facade.DispatchAsync(
                TravelCutMcpFacade.IpcTypes.PlanGet, new GetPlanRequest(pid, revision), ct);
            return McpOutcome.FromResponse(response);
        }, cancellationToken);
        return ToolResults.From(outcome);
    }

    [McpServerTool(Name = "job_status", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Estado da análise (pipeline) do projeto: etapas, mídias e estado geral. Com jobId, confere se é a análise atual do projeto. Somente leitura.")]
    public async Task<CallToolResult> JobStatus(
        [Description("GUID do projeto autorizado.")] string projectId,
        [Description("GUID da análise (opcional). Se diferente da atual, retorna JOB_NOT_FOUND.")] string? jobId = null,
        CancellationToken cancellationToken = default)
    {
        var outcome = await facade.ExecuteReadAsync("job_status", async ct =>
        {
            var pid = facade.RequireAuthorizedProject(projectId);
            Guid? expected = jobId is null ? null : TravelCutMcpFacade.ParseGuid(jobId, "jobId");
            var response = await facade.DispatchAsync(
                TravelCutMcpFacade.IpcTypes.PipelineStatus, new ProjectRequest(pid), ct);
            return McpOutcome.FromResponse(response, payload =>
            {
                if (expected is not null && payload is PipelineStatusDto status && status.JobId != expected)
                {
                    throw new McpFailure(McpErrorCodes.JobNotFound, "Esta análise não é a análise atual do projeto.");
                }
                return payload;
            });
        }, cancellationToken);
        return ToolResults.From(outcome);
    }

    [McpServerTool(Name = "get_editor_capabilities", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Capacidades do editor (DaVinci Resolve ou simulador): estado do ambiente, operações e nível de verificação. Não cria nem altera projetos. Somente leitura.")]
    public async Task<CallToolResult> GetEditorCapabilities(CancellationToken cancellationToken = default)
    {
        var outcome = await facade.ExecuteReadAsync("get_editor_capabilities", async ct =>
        {
            var response = await facade.DispatchAsync(TravelCutMcpFacade.IpcTypes.EditorCapabilities, null, ct);
            return McpOutcome.FromResponse(response);
        }, cancellationToken);
        return ToolResults.From(outcome);
    }

    [McpServerTool(Name = "get_motion_capabilities", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Capacidades de animação (HyperFrames ou simulador): estado do ambiente (Node/CLI), templates e nível de verificação. Não renderiza nada. Somente leitura.")]
    public async Task<CallToolResult> GetMotionCapabilities(CancellationToken cancellationToken = default)
    {
        var outcome = await facade.ExecuteReadAsync("get_motion_capabilities", async ct =>
        {
            var response = await facade.DispatchAsync(TravelCutMcpFacade.IpcTypes.MotionCapabilities, null, ct);
            return McpOutcome.FromResponse(response);
        }, cancellationToken);
        return ToolResults.From(outcome);
    }

    /// <summary>Mantém apenas sugestões com o status efetivo pedido. Projeção sobre o payload do dispatcher.</summary>
    private static JsonNode? FilterByStatus(object? payload, string status)
    {
        var node = System.Text.Json.JsonSerializer.SerializeToNode(payload) as JsonObject;
        if (node?["suggestions"] is JsonArray suggestions)
        {
            var kept = suggestions
                .Where(s => string.Equals(s?["status"]?.GetValue<string>(), status, StringComparison.OrdinalIgnoreCase))
                .Select(s => s!.DeepClone())
                .ToArray();
            node["suggestions"] = new JsonArray(kept);
        }
        return node;
    }
}

/// <summary>Conversão do resultado da fachada para o formato do SDK (texto JSON + isError).</summary>
internal static class ToolResults
{
    public static CallToolResult From(McpOutcome outcome) => new()
    {
        IsError = outcome.IsError,
        Content = [new TextContentBlock { Text = outcome.Text }],
    };
}
