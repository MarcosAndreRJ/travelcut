using Microsoft.Extensions.Logging;
using TravelCut.Application.Ports;
using TravelCut.Application.UseCases;
using TravelCut.Contracts.Dtos;
using TravelCut.Domain.Entities;
using TravelCut.Domain.Enums;

namespace TravelCut.Application.Planning;

/// <summary>
/// Decisões do usuário sobre sugestões (decision.submit / undo / redo e suggestions.batchDecide).
/// Regras:
/// - Append-only: nenhuma decisão é alterada ou apagada. Undo e redo também são linhas novas.
/// - Undo/redo seguem a pilha do ADR-014: undo desfaz a última decisão de usuário ativa; redo refaz a última desfeita
///   (regravada com a mesma ação e intervalo, IsRedo = true). Validados DENTRO da transação da gravação.
/// - Toda decisão grava BaseRevision (revisão mais recente do projeto; null sem plano).
/// - Depois de gravar, se o projeto já tem plano, o plano é regenerado (idempotente: sem mudança de conteúdo não cria revisão).
///   Falha na regeneração não desfaz a decisão: a resposta traz planError (PLAN_REGENERATION_FAILED para exceções).
/// - Lote é tudo ou nada (uma transação) e não aceita modify.
/// - Reviewer até 100 caracteres e notas até 2000 (INVALID_ARGUMENT acima).
/// </summary>
public sealed partial class PlanningUseCases
{
    public const string NothingToUndoCode = "NOTHING_TO_UNDO";
    public const string NothingToRedoCode = "NOTHING_TO_REDO";
    public const string InvalidIntervalCode = "INVALID_INTERVAL";
    public const string InvalidActionCode = "INVALID_ACTION";
    public const string SuggestionNotFoundCode = "SUGGESTION_NOT_FOUND";

    /// <summary>planError quando a regeneração do plano lança exceção depois da decisão já gravada.</summary>
    public const string PlanRegenerationFailedCode = "PLAN_REGENERATION_FAILED";

    /// <summary>Limite de sugestões por lote (mesmo valor de IpcDispatcher.MaxBatchSuggestionIds).</summary>
    public const int MaxBatchDecisionIds = 500;

    /// <summary>Limite de caracteres do revisor (campo reviewer).</summary>
    public const int MaxReviewerLength = 100;

    /// <summary>Limite de caracteres das notas (campo notes).</summary>
    public const int MaxNotesLength = 2000;

    /// <summary>Menor intervalo ajustável: 1 quadro a 120 fps (1/120 s = 8,33 ms), arredondado para cima em ms.</summary>
    public const long MinModifiedIntervalMs = 9;

    private const string UserReviewer = "user";
    private const string UndoNotes = "desfeito";
    private const string RedoNotes = "refeito";

    private sealed record ModifiedInterval(long StartMs, long EndMs);

    /// <summary>Resultado da regeneração disparada por uma decisão (a decisão já está gravada).</summary>
    private sealed record PlanReaction(int? PlanRevision, bool PlanChanged, string? PlanError);

    /// <summary>Aprova, rejeita, adia ou aprova com intervalo ajustado (modify). Intervalo do modify é validado contra a mídia.</summary>
    public async Task<PlanOutcome<DecisionResultDto>> SubmitDecisionAsync(SubmitDecisionRequest request, CancellationToken ct = default)
    {
        var action = NormalizeAction(request.Action, allowModify: true);
        if (action is null)
            return PlanOutcome<DecisionResultDto>.Fail(InvalidActionCode, "Ação inválida: use approve, reject, defer, reopen ou modify.");

        var textError = ValidateTextFields(request.Reviewer, request.Notes);
        if (textError is not null) return textError;

        var suggestion = await FindSuggestionAsync(request.SuggestionId, ct);
        if (suggestion is null)
            return PlanOutcome<DecisionResultDto>.Fail(SuggestionNotFoundCode, "Sugestão não encontrada.");

        // F14.8: parâmetros (zoom) só em approve/modify; ausentes = herda os efetivos atuais (como o intervalo).
        bool mayCarryParameters = action is EffectiveSuggestionStatus.ActionApprove or EffectiveSuggestionStatus.ActionModify;
        if (request.Parameters?.Zoom is not null && !mayCarryParameters)
            return PlanOutcome<DecisionResultDto>.Fail("INVALID_ARGUMENT", "parameters só é aceito em approve ou modify.");
        var (parametersJson, parametersError) = DecisionParameters.Normalize(request.Parameters, suggestion.SuggestedAction);
        if (parametersError is not null)
            return PlanOutcome<DecisionResultDto>.Fail("INVALID_ARGUMENT", parametersError);

        var media = await _media.GetByIdAsync(suggestion.MediaId, ct);
        var currentState = await CurrentStateAsync(suggestion, ct);
        parametersJson ??= currentState.ParametersJson;
        long startMs, endMs;
        if (action == EffectiveSuggestionStatus.ActionModify)
        {
            var interval = ValidateModifiedInterval(media, request.ModifiedStartSeconds, request.ModifiedEndSeconds);
            if (!interval.Success)
                return PlanOutcome<DecisionResultDto>.Fail(interval.ErrorCode!, interval.ErrorMessage!);
            startMs = interval.Value!.StartMs;
            endMs = interval.Value.EndMs;
        }
        else
        {
            // approve/reject/defer gravam o intervalo efetivo atual (um modify anterior é preservado).
            startMs = currentState.StartMs;
            endMs = currentState.EndMs;
        }

        var projectId = media?.ProjectId ?? Guid.Empty;
        var baseRevision = await CurrentBaseRevisionAsync(projectId, ct);
        var decision = NewDecision(suggestion.SuggestionId, action, request.Reviewer, request.Notes, baseRevision, startMs, endMs,
            parametersJson: parametersJson);
        await _suggestionsAndDecisions.SaveDecisionsAtomicAsync([decision], ct);

        return await ReactAndRespondAsync(suggestion, projectId, decisionsRecorded: 1, ct);
    }

    /// <summary>
    /// Desfaz a decisão ativa mais recente (topo da pilha ativa, ADR-014) gravando uma linha "undo". A validação da pilha
    /// é refeita dentro da transação da gravação. Sem decisão ativa: NOTHING_TO_UNDO.
    /// </summary>
    public async Task<PlanOutcome<DecisionResultDto>> UndoDecisionAsync(UndoDecisionRequest request, CancellationToken ct = default)
    {
        var suggestion = await FindSuggestionAsync(request.SuggestionId, ct);
        if (suggestion is null)
            return PlanOutcome<DecisionResultDto>.Fail(SuggestionNotFoundCode, "Sugestão não encontrada.");

        var media = await _media.GetByIdAsync(suggestion.MediaId, ct);
        var projectId = media?.ProjectId ?? Guid.Empty;
        var baseRevision = await CurrentBaseRevisionAsync(projectId, ct);

        var saved = await _suggestionsAndDecisions.SaveDecisionIfAsync(suggestion.SuggestionId, history =>
        {
            if (EffectiveSuggestionStatus.ComputeStacks(history).Active.Count == 0) return null;
            return NewDecision(suggestion.SuggestionId, EffectiveSuggestionStatus.ActionUndo, UserReviewer, UndoNotes,
                baseRevision, null, null);
        }, ct);

        if (saved is null)
            return PlanOutcome<DecisionResultDto>.Fail(NothingToUndoCode, "Nenhuma decisão ativa para desfazer nesta sugestão.");

        return await ReactAndRespondAsync(suggestion, projectId, decisionsRecorded: 1, ct);
    }

    /// <summary>
    /// Refaz a decisão desfeita mais recente (topo da pilha de desfeitas, ADR-014): grava a mesma ação e o mesmo intervalo
    /// de origem, com <see cref="EditDecision.IsRedo"/> = true e Notes "refeito". Sem decisão desfeita: NOTHING_TO_REDO.
    /// </summary>
    public async Task<PlanOutcome<DecisionResultDto>> RedoDecisionAsync(RedoDecisionRequest request, CancellationToken ct = default)
    {
        var suggestion = await FindSuggestionAsync(request.SuggestionId, ct);
        if (suggestion is null)
            return PlanOutcome<DecisionResultDto>.Fail(SuggestionNotFoundCode, "Sugestão não encontrada.");

        var media = await _media.GetByIdAsync(suggestion.MediaId, ct);
        var projectId = media?.ProjectId ?? Guid.Empty;
        var baseRevision = await CurrentBaseRevisionAsync(projectId, ct);

        var saved = await _suggestionsAndDecisions.SaveDecisionIfAsync(suggestion.SuggestionId, history =>
        {
            var undone = EffectiveSuggestionStatus.ComputeStacks(history).Undone;
            if (undone.Count == 0) return null;
            var target = undone[^1];
            return NewDecision(suggestion.SuggestionId, target.Action, UserReviewer, RedoNotes, baseRevision,
                target.SourceStartMs, target.SourceEndMs, isRedo: true, parametersJson: target.ParametersJson);
        }, ct);

        if (saved is null)
            return PlanOutcome<DecisionResultDto>.Fail(NothingToRedoCode, "Nenhuma decisão desfeita para refazer nesta sugestão.");

        return await ReactAndRespondAsync(suggestion, projectId, decisionsRecorded: 1, ct);
    }

    /// <summary>
    /// Lote de approve/reject/defer em UMA transação. Ids inexistentes são listados em missingSuggestionIds e não gravam nada.
    /// Falha em qualquer sugestão desfaz o lote inteiro (a exceção é relançada ao chamador).
    /// </summary>
    public async Task<PlanOutcome<DecisionResultDto>> BatchDecideAsync(BatchDecideRequest request, CancellationToken ct = default)
    {
        if (request.SuggestionIds is null || request.SuggestionIds.Count == 0)
            return PlanOutcome<DecisionResultDto>.Fail("INVALID_BATCH", "Nenhuma sugestão informada para decisão em lote.");
        if (request.SuggestionIds.Count > MaxBatchDecisionIds)
            return PlanOutcome<DecisionResultDto>.Fail("INVALID_ARGUMENT", $"Limite de {MaxBatchDecisionIds} sugestões por chamada.");

        var action = NormalizeAction(request.Action, allowModify: false);
        if (action is null)
            return PlanOutcome<DecisionResultDto>.Fail(InvalidActionCode,
                "Ação inválida em lote: use approve, reject ou defer (modify não é permitido em lote).");

        var textError = ValidateTextFields(request.Reviewer, request.Notes);
        if (textError is not null) return textError;

        var requested = request.SuggestionIds.Distinct().ToList();
        var found = await _suggestionsAndDecisions.ListSuggestionsByIdsAsync(
            requested.Where(id => id != Guid.Empty).ToList(), ct);
        var byId = found.ToDictionary(s => s.SuggestionId);
        var missing = requested.Where(id => !byId.ContainsKey(id)).ToList();
        var existing = requested.Where(byId.ContainsKey).Select(id => byId[id]).ToList();

        if (existing.Count == 0)
            return PlanOutcome<DecisionResultDto>.Ok(new DecisionResultDto([], 0, missing, null, false, null));

        // Um lote pertence a um único projeto (a revisão do plano é do projeto).
        var projectIds = new HashSet<Guid>();
        foreach (var suggestion in existing)
        {
            var media = await _media.GetByIdAsync(suggestion.MediaId, ct);
            projectIds.Add(media?.ProjectId ?? Guid.Empty);
        }
        if (projectIds.Count != 1)
            return PlanOutcome<DecisionResultDto>.Fail("INVALID_ARGUMENT", "Lote deve conter sugestões de um único projeto.");
        var projectId = projectIds.Single();

        // F14.8: parâmetros em lote só com approve e apenas para sugestões de zoom (tudo ou nada).
        if (request.Parameters?.Zoom is not null && action != EffectiveSuggestionStatus.ActionApprove)
            return PlanOutcome<DecisionResultDto>.Fail("INVALID_ARGUMENT", "parameters em lote só é aceito com approve.");
        foreach (var suggestion in existing)
        {
            var (_, batchParamError) = DecisionParameters.Normalize(request.Parameters, suggestion.SuggestedAction);
            if (batchParamError is not null)
                return PlanOutcome<DecisionResultDto>.Fail("INVALID_ARGUMENT", batchParamError);
        }

        var baseRevision = await CurrentBaseRevisionAsync(projectId, ct);
        var decisions = new List<EditDecision>(existing.Count);
        foreach (var suggestion in existing)
        {
            var current = await CurrentStateAsync(suggestion, ct);
            var (json, _) = DecisionParameters.Normalize(request.Parameters, suggestion.SuggestedAction);
            decisions.Add(NewDecision(suggestion.SuggestionId, action, request.Reviewer, request.Notes,
                baseRevision, current.StartMs, current.EndMs, parametersJson: json ?? current.ParametersJson));
        }

        await _suggestionsAndDecisions.SaveDecisionsAtomicAsync(decisions, ct);

        var reaction = await ReactToDecisionsAsync(projectId, ct);
        var affected = await AffectedSuggestionsAsync(existing, projectId, ct);
        return PlanOutcome<DecisionResultDto>.Ok(new DecisionResultDto(
            affected, decisions.Count, missing, reaction.PlanRevision, reaction.PlanChanged, reaction.PlanError));
    }

    // ------------------------------------------------------------------ Reação do plano e resposta

    /// <summary>Regenera o plano (se existir) e monta a resposta com o estado efetivo atualizado da sugestão.</summary>
    private async Task<PlanOutcome<DecisionResultDto>> ReactAndRespondAsync(
        EditSuggestion suggestion, Guid projectId, int decisionsRecorded, CancellationToken ct)
    {
        var reaction = await ReactToDecisionsAsync(projectId, ct);
        var affected = await AffectedSuggestionsAsync([suggestion], projectId, ct);
        return PlanOutcome<DecisionResultDto>.Ok(new DecisionResultDto(
            affected, decisionsRecorded, [], reaction.PlanRevision, reaction.PlanChanged, reaction.PlanError));
    }

    /// <summary>
    /// F14.8: efeitos aplicados do plano vigente por sugestão (origem auto | human, política e razão). Leitura sem validação;
    /// sem plano, plano ilegível ou erro: vazio (a origem "auto" simplesmente não aparece).
    /// </summary>
    public async Task<IReadOnlyDictionary<Guid, PlanAppliedEffectDto>> LoadAppliedEffectsAsync(Guid projectId, CancellationToken ct = default)
    {
        var empty = new Dictionary<Guid, PlanAppliedEffectDto>();
        if (projectId == Guid.Empty) return empty;
        try
        {
            var project = await _projects.GetByIdAsync(projectId, ct);
            if (project is null) return empty;
            var entry = await _revisions.GetLatestAsync(projectId, ct);
            if (entry is null) return empty;
            var read = await ReadPlanAsync(project.RootPath, entry, ct);
            if (read.Plan?.AppliedEffects is not { } effects) return empty;
            var map = new Dictionary<Guid, PlanAppliedEffectDto>();
            foreach (var e in effects) map.TryAdd(e.SuggestionId, e);
            return map;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Não foi possível ler os efeitos aplicados do plano (projeto {ProjectId})", projectId);
            return empty;
        }
    }

    /// <summary>
    /// Regenera o plano após uma decisão. Sem revisão no projeto, nada é criado. Regenera com o mesmo perfil, base de tempo
    /// e fuso da revisão mais recente. Não lança (exceto cancelamento): falhas viram PlanError, pois a decisão já está gravada.
    /// </summary>
    private async Task<PlanReaction> ReactToDecisionsAsync(Guid projectId, CancellationToken ct)
    {
        if (projectId == Guid.Empty) return new PlanReaction(null, false, null);

        int? revisionBefore = null;
        try
        {
            var before = await _revisions.GetLatestAsync(projectId, ct);
            if (before is null) return new PlanReaction(null, false, null);
            revisionBefore = before.Revision;

            var current = await GetAsync(new GetPlanRequest(projectId, before.Revision), ct);
            if (!current.Success || current.Value is null)
            {
                _logger.LogWarning("Decisão gravada, mas a revisão {Revision} do plano não pôde ser lida ({Code})",
                    before.Revision, current.ErrorCode);
                return new PlanReaction(before.Revision, false, current.ErrorCode ?? "PLAN_NOT_FOUND");
            }

            var basis = current.Value;
            var regenerated = await GenerateAsync(
                new GeneratePlanRequest(projectId, basis.Profile, basis.TimeBase, basis.TimeZoneId), ct);
            if (!regenerated.Success)
            {
                _logger.LogWarning("Decisão gravada, mas a regeneração do plano falhou ({Code})", regenerated.ErrorCode);
                return new PlanReaction(before.Revision, false, regenerated.ErrorCode);
            }

            var after = await _revisions.GetLatestAsync(projectId, ct);
            int? revision = after?.Revision ?? before.Revision;
            return new PlanReaction(revision, revision != before.Revision, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Decisão gravada, mas a regeneração do plano lançou exceção (projeto {ProjectId})", projectId);
            return new PlanReaction(revisionBefore, false, PlanRegenerationFailedCode);
        }
    }

    // ------------------------------------------------------------------ Auxiliares

    private async Task<EditSuggestion?> FindSuggestionAsync(Guid suggestionId, CancellationToken ct)
    {
        if (suggestionId == Guid.Empty) return null;
        var found = await _suggestionsAndDecisions.ListSuggestionsByIdsAsync([suggestionId], ct);
        return found.FirstOrDefault();
    }

    /// <summary>Estado efetivo de uma sugestão a partir do seu histórico (intervalo, status, canUndo/canRedo).</summary>
    private async Task<EffectiveSuggestion> CurrentStateAsync(EditSuggestion suggestion, CancellationToken ct)
    {
        var history = await _suggestionsAndDecisions.ListDecisionsBySuggestionAsync(suggestion.SuggestionId, ct);
        return EffectiveSuggestionStatus.ResolveDetailed([suggestion], history)[suggestion.SuggestionId];
    }

    private async Task<List<EditSuggestionDto>> AffectedSuggestionsAsync(IReadOnlyList<EditSuggestion> suggestions, Guid projectId, CancellationToken ct)
    {
        var dtos = new List<EditSuggestionDto>(suggestions.Count);
        var effects = await LoadAppliedEffectsAsync(projectId, ct);
        foreach (var suggestion in suggestions)
        {
            var effective = await CurrentStateAsync(suggestion, ct);
            dtos.Add(SuggestionEffectiveMapper.ToDto(suggestion, effective,
                effects.TryGetValue(suggestion.SuggestionId, out var fx) ? fx : null));
        }
        return dtos;
    }

    private async Task<int?> CurrentBaseRevisionAsync(Guid projectId, CancellationToken ct)
    {
        if (projectId == Guid.Empty) return null;
        return (await _revisions.GetLatestAsync(projectId, ct))?.Revision;
    }

    private EditDecision NewDecision(
        Guid suggestionId, string action, string? reviewer, string? notes, int? baseRevision, long? startMs, long? endMs,
        bool isRedo = false, string? parametersJson = null) => new()
    {
        DecisionId = Guid.NewGuid(),
        SuggestionId = suggestionId,
        Action = action,
        Reviewer = string.IsNullOrWhiteSpace(reviewer) ? UserReviewer : reviewer,
        Notes = notes ?? string.Empty,
        TimestampUtc = _clock.GetUtcNow().UtcDateTime,
        BaseRevision = baseRevision,
        SourceStartMs = startMs,
        SourceEndMs = endMs,
        IsRedo = isRedo,
        ParametersJson = parametersJson,
    };

    /// <summary>Reviewer e notas dentro dos limites (INVALID_ARGUMENT). Null quando válido.</summary>
    private static PlanOutcome<DecisionResultDto>? ValidateTextFields(string? reviewer, string? notes)
    {
        if (reviewer is not null && reviewer.Length > MaxReviewerLength)
            return PlanOutcome<DecisionResultDto>.Fail("INVALID_ARGUMENT", $"Revisor com mais de {MaxReviewerLength} caracteres.");
        if (notes is not null && notes.Length > MaxNotesLength)
            return PlanOutcome<DecisionResultDto>.Fail("INVALID_ARGUMENT", $"Notas com mais de {MaxNotesLength} caracteres.");
        return null;
    }

    /// <summary>Ação normalizada (trim + minúsculas) ou null. Modify só existe em decision.submit.</summary>
    private static string? NormalizeAction(string? raw, bool allowModify)
    {
        var normalized = raw?.Trim().ToLowerInvariant();
        var allowed = allowModify ? EffectiveSuggestionStatus.SubmitActions : EffectiveSuggestionStatus.BatchActions;
        return normalized is not null && allowed.Contains(normalized, StringComparer.Ordinal) ? normalized : null;
    }

    /// <summary>
    /// Valida o intervalo ajustado: obrigatório, finito, não negativo, fim após início, mínimo de 1 quadro (1/120 s),
    /// dentro da duração da mídia. Duração desconhecida (sem metadados ok ou zero) recusa o ajuste.
    /// </summary>
    private static PlanOutcome<ModifiedInterval> ValidateModifiedInterval(MediaAsset? media, double? startSeconds, double? endSeconds)
    {
        if (startSeconds is null || endSeconds is null)
            return PlanOutcome<ModifiedInterval>.Fail(InvalidIntervalCode,
                "Intervalo ajustado obrigatório: informe modifiedStartSeconds e modifiedEndSeconds.");

        bool metadataOk = media?.Metadata is { } meta && meta.MetadataStatus == MediaMetadataStatus.Ok;
        long durationUs = media is not null && metadataOk ? PlanEligibility.DurationUs(media) : 0;
        if (durationUs <= 0)
            return PlanOutcome<ModifiedInterval>.Fail(InvalidIntervalCode,
                "Não é possível validar o intervalo: duração da mídia desconhecida.");

        double s = startSeconds.Value;
        double e = endSeconds.Value;
        if (!double.IsFinite(s) || !double.IsFinite(e) || s < 0 || e < 0)
            return PlanOutcome<ModifiedInterval>.Fail(InvalidIntervalCode,
                "Intervalo ajustado inválido: valores devem ser finitos e não negativos.");
        if (e <= s)
            return PlanOutcome<ModifiedInterval>.Fail(InvalidIntervalCode,
                "Intervalo vazio: o fim deve ser maior que o início.");

        double durationSeconds = durationUs / 1_000_000d;
        if (e > durationSeconds + 0.0005)
            return PlanOutcome<ModifiedInterval>.Fail(InvalidIntervalCode,
                "Intervalo ajustado fora da duração da mídia.");

        long startMs = SuggestionStorageMapper.ToTicks(s);
        long endMs = SuggestionStorageMapper.ToTicks(e);
        if (endMs - startMs < MinModifiedIntervalMs)
            return PlanOutcome<ModifiedInterval>.Fail(InvalidIntervalCode,
                "Intervalo menor que 1 quadro (1/120 s).");
        if (endMs * 1000 > durationUs)
            return PlanOutcome<ModifiedInterval>.Fail(InvalidIntervalCode,
                "Intervalo ajustado fora da duração da mídia.");

        return PlanOutcome<ModifiedInterval>.Ok(new ModifiedInterval(startMs, endMs));
    }

    /// <summary>
    /// Cria uma sugestão manual de corte (F14.1 Checkpoint C) originada pelo usuário na Biblioteca.
    /// Valida limites da mídia, evita duplicatas equivalentes e registra com estado inicial Proposed.
    /// </summary>
    public async Task<PlanOutcome<EditSuggestionDto>> CreateManualSuggestionAsync(CreateManualSuggestionRequest request, CancellationToken ct = default)
    {
        if (request.ProjectId == Guid.Empty)
            return PlanOutcome<EditSuggestionDto>.Fail("INVALID_PROJECT", "ID de projeto inválido.");
        if (request.MediaId == Guid.Empty)
            return PlanOutcome<EditSuggestionDto>.Fail("INVALID_MEDIA", "ID de mídia inválido.");

        var media = await _media.GetByIdAsync(request.MediaId, ct);
        if (media == null || media.ProjectId != request.ProjectId)
            return PlanOutcome<EditSuggestionDto>.Fail("MEDIA_NOT_FOUND", "Mídia não encontrada no projeto.");

        long durationUs = media.Metadata != null && media.Metadata.DurationSeconds > 0
            ? (long)(media.Metadata.DurationSeconds * 1_000_000d)
            : (media.Metadata?.DurationTicks ?? 0) * 10;

        var interval = ValidateModifiedInterval(media, request.StartSeconds, request.EndSeconds);
        if (!interval.Success)
            return PlanOutcome<EditSuggestionDto>.Fail(interval.ErrorCode!, interval.ErrorMessage!);

        long startMs = interval.Value!.StartMs;
        long endMs = interval.Value.EndMs;

        // Evita duplicatas equivalentes (mesma mídia e mesmo intervalo aproximado dentro de 100ms)
        var existing = await _suggestionsAndDecisions.ListSuggestionsByProjectAsync(request.ProjectId, ct);
        var duplicate = existing.FirstOrDefault(s => s.MediaId == request.MediaId
            && Math.Abs(s.SourceInterval.StartTicks - startMs) <= 100
            && Math.Abs(s.SourceInterval.EndTicks - endMs) <= 100);

        if (duplicate != null)
        {
            var existingHistory = await _suggestionsAndDecisions.ListDecisionsBySuggestionAsync(duplicate.SuggestionId, ct);
            var existingEffective = EffectiveSuggestionStatus.ResolveDetailed([duplicate], existingHistory)[duplicate.SuggestionId];
            return PlanOutcome<EditSuggestionDto>.Ok(SuggestionEffectiveMapper.ToDto(duplicate, existingEffective));
        }

        var suggestion = new EditSuggestion
        {
            SuggestionId = Guid.NewGuid(),
            MediaId = request.MediaId,
            SourceInterval = new TravelCut.Domain.ValueObjects.TimeRange(startMs, endMs),
            Kind = SuggestionKind.Silence,
            SuggestedAction = "cut",
            Rationale = string.IsNullOrWhiteSpace(request.Rationale) ? "Pré-corte manual criado na Biblioteca" : request.Rationale.Trim(),
            EvidenceRefs = "manual",
            ConfidenceBand = "high",
            NeedsHumanReview = true,
            Status = SuggestionStatus.Proposed,
            Author = "user",
            CreatedAtUtc = _clock.GetUtcNow().UtcDateTime
        };

        await _suggestionsAndDecisions.SaveSuggestionsBatchAsync([suggestion], ct);

        var effective = new EffectiveSuggestion(
            suggestion.SuggestionId,
            SuggestionStatus.Proposed,
            startMs,
            endMs,
            Adjusted: false,
            CanUndo: false,
            CanRedo: false
        );

        return PlanOutcome<EditSuggestionDto>.Ok(SuggestionEffectiveMapper.ToDto(suggestion, effective));
    }
}
