using System.Text.Json.Serialization;

namespace TravelCut.Contracts.Dtos;

// Decisões (F07): append-only, estado efetivo = última decisão por Version.
// "modify" = aprovar com intervalo ajustado (modifiedStart/EndSeconds obrigatórios, dentro da mídia, não vazio).
// "undo" é gravado como decisão; "redo" regrava a ação desfeita (com o mesmo intervalo, notes "refeito") — nunca há exclusões.
// Toda decisão registra a revisão do plano vigente (baseRevision).
// Depois de decidir, se o projeto já tem plano, o host regenera o plano; nova revisão só é criada se o conteúdo mudou.

public record EditSuggestionDto(
    [property: JsonPropertyName("suggestionId")] Guid SuggestionId,
    [property: JsonPropertyName("mediaId")] Guid MediaId,
    [property: JsonPropertyName("startSeconds")] double StartSeconds,
    [property: JsonPropertyName("endSeconds")] double EndSeconds,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("rationale")] string Rationale,
    [property: JsonPropertyName("confidenceBand")] string ConfidenceBand,
    [property: JsonPropertyName("needsHumanReview")] bool NeedsHumanReview,
    [property: JsonPropertyName("suggestedAction")] string SuggestedAction,
    [property: JsonPropertyName("status")] string Status,                 // efetivo: Proposed | Approved | Rejected | Deferred
    [property: JsonPropertyName("author")] string Author,
    [property: JsonPropertyName("effectiveStartSeconds")] double? EffectiveStartSeconds = null, // intervalo ajustado por "modify"; null = original
    [property: JsonPropertyName("effectiveEndSeconds")] double? EffectiveEndSeconds = null,
    [property: JsonPropertyName("canUndo")] bool CanUndo = false,        // há decisão de usuário ativa
    [property: JsonPropertyName("canRedo")] bool CanRedo = false,        // última decisão é um undo
    // F14.8 (aditivo, todos opcionais/null em dados antigos):
    // origin: "human" = há decisão de usuário ativa; "auto" = autoaplicada pela política no plano vigente; null = nenhuma das duas.
    [property: JsonPropertyName("origin")] string? Origin = null,
    [property: JsonPropertyName("policyVersion")] string? PolicyVersion = null,   // só origin "auto" (ex.: "auto-editorial-policy/2")
    [property: JsonPropertyName("policyReason")] string? PolicyReason = null,     // só origin "auto": razão legível da política
    // Parâmetros EFETIVOS (decisão humana ativa; senão os da própria sugestão, ex.: zoom do recomendador). Null = nenhum.
    [property: JsonPropertyName("parameters")] DecisionParametersDto? Parameters = null
);

/// <summary>
/// F14.8: parâmetros opcionais de uma decisão (aditivo). Hoje só "zoom" (sugestões de zoom): decisão humana vence a política.
/// scale limitada a 1,01–1,12×; focalPointX/Y em 0–1; durationSeconds ≥ 0. Persistido na decisão (coluna ParametersJson).
/// </summary>
public sealed record DecisionParametersDto(
    [property: JsonPropertyName("zoom")] SmartZoomParametersDto? Zoom = null
);

public record SubmitDecisionRequest(
    [property: JsonPropertyName("suggestionId")] Guid SuggestionId,
    [property: JsonPropertyName("action")] string Action, // approve | reject | defer | modify
    [property: JsonPropertyName("reviewer")] string Reviewer,
    [property: JsonPropertyName("notes")] string? Notes,
    [property: JsonPropertyName("modifiedStartSeconds")] double? ModifiedStartSeconds = null,
    [property: JsonPropertyName("modifiedEndSeconds")] double? ModifiedEndSeconds = null,
    // F14.8 (aditivo): parâmetros da decisão (zoom). Só em approve/modify de sugestões de zoom; ausente = herda os efetivos atuais.
    [property: JsonPropertyName("parameters")] DecisionParametersDto? Parameters = null
);

public record UndoDecisionRequest(
    [property: JsonPropertyName("suggestionId")] Guid SuggestionId
);

public record RedoDecisionRequest(
    [property: JsonPropertyName("suggestionId")] Guid SuggestionId
);

public record BatchDecideRequest(
    [property: JsonPropertyName("suggestionIds")] List<Guid> SuggestionIds,
    [property: JsonPropertyName("action")] string Action,                 // approve | reject | defer (modify não é permitido em lote)
    [property: JsonPropertyName("reviewer")] string Reviewer,
    [property: JsonPropertyName("notes")] string? Notes,
    // F14.8 (aditivo): parâmetros aplicados a todas as sugestões do lote (devem ser todas de zoom; senão INVALID_ARGUMENT).
    [property: JsonPropertyName("parameters")] DecisionParametersDto? Parameters = null
);

public record CreateManualSuggestionRequest(
    [property: JsonPropertyName("projectId")] Guid ProjectId,
    [property: JsonPropertyName("mediaId")] Guid MediaId,
    [property: JsonPropertyName("startSeconds")] double StartSeconds,
    [property: JsonPropertyName("endSeconds")] double EndSeconds,
    [property: JsonPropertyName("rationale")] string? Rationale = null
);

public record EditDecisionDto(
    [property: JsonPropertyName("decisionId")] Guid DecisionId,
    [property: JsonPropertyName("suggestionId")] Guid SuggestionId,
    [property: JsonPropertyName("action")] string Action,                 // approve | reject | defer | modify | undo (redo regrava a ação original)
    [property: JsonPropertyName("reviewer")] string Reviewer,
    [property: JsonPropertyName("notes")] string Notes,
    [property: JsonPropertyName("timestampUtc")] DateTime TimestampUtc,
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("baseRevision")] int? BaseRevision = null,       // revisão do plano vigente no momento da decisão
    [property: JsonPropertyName("sourceStartSeconds")] double? SourceStartSeconds = null, // intervalo de origem decidido (modify: ajustado)
    [property: JsonPropertyName("sourceEndSeconds")] double? SourceEndSeconds = null,
    [property: JsonPropertyName("parameters")] DecisionParametersDto? Parameters = null // F14.8 (aditivo)
);

// Resposta de decision.submit / decision.undo / decision.redo / suggestions.batchDecide.
public record DecisionResultDto(
    [property: JsonPropertyName("suggestions")] List<EditSuggestionDto> Suggestions, // estado efetivo atualizado das sugestões afetadas
    [property: JsonPropertyName("recordedCount")] int RecordedCount,
    [property: JsonPropertyName("missingSuggestionIds")] List<Guid> MissingSuggestionIds,
    [property: JsonPropertyName("planRevision")] int? PlanRevision,       // revisão vigente após a decisão (null = projeto sem plano)
    [property: JsonPropertyName("planChanged")] bool PlanChanged,         // true = nova revisão criada por esta decisão
    [property: JsonPropertyName("planError")] string? PlanError           // código se a regeneração falhou (decisão continua gravada)
);
