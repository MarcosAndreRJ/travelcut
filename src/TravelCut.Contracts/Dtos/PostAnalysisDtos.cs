using System.Text.Json.Serialization;

namespace TravelCut.Contracts.Dtos;

// Orquestrador pós-análise (F14.8 / I2b, achado P01) e opções de automação por projeto.
// Status único exposto aditivamente em PipelineStatusDto.postAnalysis (pipeline.status); sem store paralelo.
// Métodos IPC: automation.options.get, automation.options.set.

/// <summary>
/// Estado do processamento automático após uma análise concluída.
/// State: idle | running | completed | completed_with_issues | failed | skipped.
/// Stage (apenas running): plan | proxy. Issues: códigos estáveis (ex.: PLAN_FAILED, PROXY_FAILED); nunca texto de exceção.
/// </summary>
public record PostAnalysisStatusDto(
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("stage")] string? Stage,
    [property: JsonPropertyName("reason")] string? Reason,                 // skipped: auto_process_disabled | no_analysis_results
    [property: JsonPropertyName("autoCutsApplied")] int AutoCutsApplied,   // cortes aplicados automaticamente pela política
    [property: JsonPropertyName("pendingReview")] int PendingReview,       // sugestões aguardando revisão do usuário
    [property: JsonPropertyName("proxiesQueued")] int ProxiesQueued,       // proxies enfileirados por esta execução
    [property: JsonPropertyName("failures")] int Failures,                 // etapas/itens que falharam
    [property: JsonPropertyName("issues")] List<string> Issues,
    [property: JsonPropertyName("planRevision")] int? PlanRevision,
    [property: JsonPropertyName("startedAtUtc")] DateTime? StartedAtUtc,
    [property: JsonPropertyName("finishedAtUtc")] DateTime? FinishedAtUtc
);

public record GetAutomationOptionsRequest(
    [property: JsonPropertyName("projectId")] Guid ProjectId
);

public record SetAutomationOptionsRequest(
    [property: JsonPropertyName("projectId")] Guid ProjectId,
    [property: JsonPropertyName("autoProcess")] bool AutoProcess
);

public record AutomationOptionsDto(
    [property: JsonPropertyName("projectId")] Guid ProjectId,
    [property: JsonPropertyName("autoProcess")] bool AutoProcess,           // padrão true (projeto novo/antigo)
    [property: JsonPropertyName("isDefault")] bool IsDefault                // true = nunca escolhido pelo usuário
);
