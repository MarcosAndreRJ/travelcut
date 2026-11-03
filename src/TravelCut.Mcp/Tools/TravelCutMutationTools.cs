using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TravelCut.Contracts.Dtos;
using TravelCut.Mcp.Facade;

namespace TravelCut.Mcp.Tools;

/// <summary>
/// Ferramentas de mutação. Registradas somente com <c>--allow-mutations</c>. Todas passam por
/// <see cref="TravelCutMcpFacade.ExecuteMutationAsync{T}"/>: modo habilitado, projeto autorizado, chave de idempotência,
/// validação do corpo (antes do cache: erro de validação não consome a chave), cache e logs.
/// O corpo chama o mesmo IPC da UI (decision.submit, pipeline.start, editor.prepareApply, editor.apply).
/// </summary>
[McpServerToolType]
public sealed class TravelCutMutationTools(TravelCutMcpFacade facade)
{
    private const int MaxReviewerLength = TravelCutMcpFacade.MaxReviewerLength;

    [McpServerTool(Name = "submit_review", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Registra a decisão de revisão de uma sugestão (approve, reject, defer ou modify), igual à UI. A sugestão precisa pertencer ao projeto. Exige idempotencyKey. Só aparece com --allow-mutations.")]
    public async Task<CallToolResult> SubmitReview(
        [Description("GUID do projeto autorizado.")] string projectId,
        [Description("GUID da sugestão, que deve pertencer ao projeto.")] string suggestionId,
        [Description("approve | reject | defer | modify. Só modify aceita intervalo ajustado.")] string action,
        [Description("Nome de quem revisa (1 a 100 caracteres).")] string reviewer,
        [Description("Chave de idempotência: [A-Za-z0-9_-], 1 a 100 caracteres.")] string idempotencyKey,
        [Description("Observação opcional (até 2000 caracteres).")] string? notes = null,
        [Description("Início ajustado em segundos da fonte (obrigatório com modify).")] double? modifiedStartSeconds = null,
        [Description("Fim ajustado em segundos da fonte (obrigatório com modify, maior que o início).")] double? modifiedEndSeconds = null,
        CancellationToken cancellationToken = default)
    {
        var outcome = await facade.ExecuteMutationAsync("submit_review", projectId, idempotencyKey,
            validate: () =>
            {
                var sid = TravelCutMcpFacade.ParseGuid(suggestionId, "suggestionId");
                var act = TravelCutMcpFacade.RequireReviewAction(action);
                var who = RequireReviewer(reviewer);
                RequireNotes(notes);
                RequireModifiedRange(act, modifiedStartSeconds, modifiedEndSeconds);

                // Hash com os valores validados e normalizados (revisor sem espaços, ação em minúsculas).
                var canonical = new
                {
                    projectId = TravelCutMcpFacade.Canonical(projectId),
                    suggestionId = sid.ToString("D"),
                    action = act,
                    reviewer = who,
                    notes,
                    modifiedStartSeconds,
                    modifiedEndSeconds,
                };
                return new TravelCutMcpFacade.ValidatedMutation<ReviewInput>(
                    new ReviewInput(sid, act, who, notes, modifiedStartSeconds, modifiedEndSeconds), canonical);
            },
            body: async (pid, input, ct) =>
            {
                // decision.submit não recebe projectId: a pertença da sugestão ao projeto autorizado é verificada aqui.
                var list = await facade.DispatchAsync(TravelCutMcpFacade.IpcTypes.SuggestionsList, new ProjectRequest(pid), ct);
                if (!list.Ok)
                {
                    return McpOutcome.FromResponse(list);
                }
                if (!ContainsSuggestion(list.Payload, input.SuggestionId))
                {
                    throw new McpFailure(McpErrorCodes.SuggestionNotFound, "Sugestão não encontrada neste projeto.");
                }

                var request = new SubmitDecisionRequest(input.SuggestionId, input.Action, input.Reviewer, input.Notes,
                    input.ModifiedStartSeconds, input.ModifiedEndSeconds);
                var response = await facade.DispatchAsync(TravelCutMcpFacade.IpcTypes.DecisionSubmit, request, ct);
                return McpOutcome.FromResponse(response);
            },
            cancellationToken);

        return ToolResults.From(outcome);
    }

    [McpServerTool(Name = "queue_analysis", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Enfileira a análise (transcrição, silêncio, cenas e candidatos) de mídias do projeto, igual à UI. Mídias omitidas = todas as de vídeo/áudio. Exige idempotencyKey; repetir a chave não inicia outra análise.")]
    public async Task<CallToolResult> QueueAnalysis(
        [Description("GUID do projeto autorizado.")] string projectId,
        [Description("Chave de idempotência: [A-Za-z0-9_-], 1 a 100 caracteres.")] string idempotencyKey,
        [Description("GUIDs das mídias do próprio projeto (até 200, sem repetição). Omitido = todas.")] string[]? mediaIds = null,
        CancellationToken cancellationToken = default)
    {
        var outcome = await facade.ExecuteMutationAsync("queue_analysis", projectId, idempotencyKey,
            validate: () =>
            {
                List<Guid>? ids = null;
                if (mediaIds is not null)
                {
                    if (mediaIds.Length == 0 || mediaIds.Length > TravelCutMcpFacade.MaxMediaIdsPerAnalysis)
                    {
                        throw new McpFailure(McpErrorCodes.InvalidArgument,
                            $"mediaIds deve ter de 1 a {TravelCutMcpFacade.MaxMediaIdsPerAnalysis} itens, ou ser omitido.");
                    }
                    ids = mediaIds.Select(m => TravelCutMcpFacade.ParseGuid(m, "mediaIds")).ToList();
                    if (ids.Distinct().Count() != ids.Count)
                    {
                        throw new McpFailure(McpErrorCodes.InvalidArgument, "mediaIds contém GUIDs repetidos.");
                    }
                }

                // Ordem não altera a análise: o hash usa a lista ordenada.
                var canonical = new
                {
                    projectId = TravelCutMcpFacade.Canonical(projectId),
                    mediaIds = ids?.Select(i => i.ToString("D")).OrderBy(s => s, StringComparer.Ordinal).ToArray(),
                };
                return new TravelCutMcpFacade.ValidatedMutation<List<Guid>?>(ids, canonical);
            },
            body: async (pid, ids, ct) =>
            {
                // Mídias de outro projeto são recusadas (MEDIA_NOT_FOUND) antes de iniciar a análise.
                if (ids is not null)
                {
                    await facade.RequireMediaInProjectAsync(pid, ids, ct);
                }

                // Mesmos padrões da UI: sem estágios e idioma explícitos (padrão do host), sem forçar cache.
                var request = new StartPipelineRequest(pid, ids, Stages: null, Language: null, Force: false);
                var response = await facade.DispatchAsync(TravelCutMcpFacade.IpcTypes.PipelineStart, request, ct);
                return McpOutcome.FromResponse(response);
            },
            cancellationToken);

        return ToolResults.From(outcome);
    }

    [McpServerTool(Name = "apply_plan", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("Prepara (dryRun=true, padrão) ou aplica o plano no DaVinci Resolve. Dry-run não altera o editor e devolve assemblyFingerprint. Aplicar exige revision e o fingerprint do dry-run, e confirmProjectSwitch=true somente após o usuário aprovar a troca do projeto aberto. Use chaves de idempotência diferentes para dry-run e aplicação.")]
    public async Task<CallToolResult> ApplyPlan(
        [Description("GUID do projeto autorizado.")] string projectId,
        [Description("Chave de idempotência: [A-Za-z0-9_-], 1 a 100 caracteres. Use uma chave para o dry-run e outra para a aplicação.")] string idempotencyKey,
        [Description("Revisão do plano (inteiro ≥ 1). Obrigatória ao aplicar; omitida no dry-run = mais recente.")] int? revision = null,
        [Description("true = somente preparar (padrão). false = aplicar no editor.")] bool dryRun = true,
        [Description("Fingerprint devolvido pelo dry-run. Obrigatório ao aplicar.")] string? assemblyFingerprint = null,
        [Description("Confirmação da troca do projeto aberto no DaVinci. Obrigatória (true) ao aplicar.")] bool confirmProjectSwitch = false,
        CancellationToken cancellationToken = default)
    {
        var outcome = await facade.ExecuteMutationAsync("apply_plan", projectId, idempotencyKey,
            validate: () =>
            {
                if (revision is < 1)
                {
                    throw new McpFailure(McpErrorCodes.InvalidArgument, "revision deve ser um inteiro ≥ 1.");
                }

                var fingerprint = assemblyFingerprint?.Trim();
                var canonical = new
                {
                    projectId = TravelCutMcpFacade.Canonical(projectId),
                    revision,
                    dryRun,
                    assemblyFingerprint = fingerprint,
                    confirmProjectSwitch,
                };

                if (dryRun)
                {
                    return new TravelCutMcpFacade.ValidatedMutation<ApplyInput>(
                        new ApplyInput(DryRun: true, Revision: revision, Fingerprint: null), canonical);
                }

                if (revision is null)
                {
                    throw new McpFailure(McpErrorCodes.InvalidArgument,
                        "revision é obrigatória para aplicar. Faça antes o dry-run para obtê-la.");
                }
                if (string.IsNullOrEmpty(fingerprint) || fingerprint.Length > 200)
                {
                    throw new McpFailure(McpErrorCodes.InvalidArgument,
                        "assemblyFingerprint do dry-run é obrigatório para aplicar.");
                }
                if (!confirmProjectSwitch)
                {
                    throw new McpFailure(McpErrorCodes.ConfirmationRequired,
                        "Aplicar troca o projeto aberto no DaVinci Resolve. Confirme com o usuário e envie confirmProjectSwitch=true.");
                }

                return new TravelCutMcpFacade.ValidatedMutation<ApplyInput>(
                    new ApplyInput(DryRun: false, Revision: revision, Fingerprint: fingerprint), canonical);
            },
            body: async (pid, input, ct) =>
            {
                if (input.DryRun)
                {
                    var preview = await facade.DispatchAsync(
                        TravelCutMcpFacade.IpcTypes.EditorPrepareApply, new PrepareEditorApplyRequest(pid, input.Revision), ct);
                    return McpOutcome.FromResponse(preview);
                }

                var request = new ApplyEditorPlanRequest(pid, input.Revision!.Value, input.Fingerprint!,
                    ConfirmProjectSwitch: true, IdempotencyKey: idempotencyKey);
                var response = await facade.DispatchAsync(TravelCutMcpFacade.IpcTypes.EditorApply, request, ct);
                var outcome = McpOutcome.FromResponse(response);

                // O IPC devolve sucesso técnico com status ERROR/UNAVAILABLE: não cacheia, para a mesma chave poder tentar de novo.
                return IsFailedApplyStatus(response.Payload) ? outcome.AsNotCacheable() : outcome;
            },
            cancellationToken);

        return ToolResults.From(outcome);
    }

    /// <summary>Entradas validadas de submit_review (já com GUID, ação e revisor normalizados).</summary>
    private sealed record ReviewInput(
        Guid SuggestionId,
        string Action,
        string Reviewer,
        string? Notes,
        double? ModifiedStartSeconds,
        double? ModifiedEndSeconds);

    /// <summary>Entradas validadas de apply_plan (dry-run ou aplicação).</summary>
    private sealed record ApplyInput(bool DryRun, int? Revision, string? Fingerprint);

    // ---- Validações de corpo (feitas antes do cache: erro de validação não consome a chave de idempotência) ----

    private static string RequireReviewer(string? raw)
    {
        var value = raw?.Trim();
        if (string.IsNullOrEmpty(value) || value.Length > MaxReviewerLength || value.Any(char.IsControl))
        {
            throw new McpFailure(McpErrorCodes.InvalidArgument,
                $"reviewer é obrigatório (1 a {MaxReviewerLength} caracteres, sem controles).");
        }
        return value;
    }

    private static void RequireNotes(string? notes)
    {
        if (notes is not null && notes.Length > TravelCutMcpFacade.MaxNotesLength)
        {
            throw new McpFailure(McpErrorCodes.InvalidArgument,
                $"notes deve ter no máximo {TravelCutMcpFacade.MaxNotesLength} caracteres.");
        }
    }

    private static void RequireModifiedRange(string action, double? start, double? end)
    {
        bool any = start is not null || end is not null;
        if (action != "modify")
        {
            if (any)
            {
                throw new McpFailure(McpErrorCodes.InvalidArgument,
                    "modifiedStartSeconds/modifiedEndSeconds só são aceitos com action=modify.");
            }
            return;
        }

        if (start is null || end is null)
        {
            throw new McpFailure(McpErrorCodes.InvalidArgument, "action=modify exige modifiedStartSeconds e modifiedEndSeconds.");
        }
        if (!double.IsFinite(start.Value) || !double.IsFinite(end.Value) || start.Value < 0 || end.Value <= start.Value)
        {
            throw new McpFailure(McpErrorCodes.InvalidArgument,
                "Intervalo inválido: início ≥ 0 e fim maior que o início, em segundos finitos.");
        }
    }

    /// <summary>Resultado de aplicação (EditorApplyResultDto) com status de falha do editor: ERROR ou UNAVAILABLE.</summary>
    private static bool IsFailedApplyStatus(object? payload)
    {
        if (JsonSerializer.SerializeToNode(payload) is not JsonObject node)
        {
            return false;
        }
        var status = node["status"]?.GetValue<string>();
        return status is EditorResultStatus.Error or EditorResultStatus.Unavailable;
    }

    private static bool ContainsSuggestion(object? suggestionsPayload, Guid suggestionId)
    {
        var node = JsonSerializer.SerializeToNode(suggestionsPayload) as JsonObject;
        if (node?["suggestions"] is not JsonArray items)
        {
            return false;
        }
        var expected = suggestionId.ToString("D");
        return items.Any(item =>
            string.Equals(item?["suggestionId"]?.GetValue<string>(), expected, StringComparison.OrdinalIgnoreCase));
    }
}
