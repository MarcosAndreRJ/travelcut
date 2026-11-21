using Microsoft.Extensions.Logging;
using TravelCut.Contracts.Dtos;
using TravelCut.Domain.Entities;
using TravelCut.Domain.Enums;

namespace TravelCut.Application.Planning;

/// <summary>
/// Corte direto (edição por transcrição, Incremento 1; IPC "cut.apply"): cria (ou reaproveita) uma sugestão manual "cut",
/// grava a decisão approve e materializa o corte no plano, inclusive em projeto sem revisão. O resultado é EFETIVO: applied
/// só é true quando o plano vigente lido de volta contém o corte (AppliedCuts/AppliedEffects), nunca por presunção.
/// Regras:
/// - Idempotência: o requestId é gravado em <see cref="EditSuggestion.EvidenceRefs"/> ("manual;cut:&lt;guid&gt;"); repetir o mesmo
///   requestId, ou a mesma mídia com intervalo equivalente (≤ 100 ms) já aprovado e aplicado, converge para o mesmo resultado
///   (alreadyApplied = true) sem nova sugestão, decisão nem revisão.
/// - baseRevision (opcional): revisão vigente que a UI viu (0 = sem plano). Divergente: PLAN_STALE_BASE e nada é gravado.
/// - Criação da sugestão e da decisão acontecem sob o lock do projeto (idempotência sem corrida). A geração/reação do plano
///   acontece DEPOIS de soltar o lock (o lock não é reentrante e <see cref="GenerateAsync"/> o toma).
/// - Plano protegido (PLAN_MANUAL_*) ou falha de regeneração: a decisão fica gravada, applied = false e o estado parcial é
///   exposto em planSkippedReason / planError; não lança. Plano gerado mas sem o corte: planError = CUT_NOT_APPLIED.
/// - Desfazer/refazer: decision.undo / decision.redo por suggestionId (não reimplementados aqui).
/// </summary>
public sealed partial class PlanningUseCases
{
    /// <summary>planError quando o plano foi gerado/lido normalmente mas não contém o corte (ex.: mídia excluída do plano).</summary>
    public const string CutNotAppliedCode = "CUT_NOT_APPLIED";

    private const string DirectCutDefaultRationale = "Corte direto criado na Biblioteca";
    private const string DirectCutNotes = "corte direto";
    private const string DirectCutAction = "cut";
    private const string DirectCutEvidencePrefix = "manual";
    private const long EquivalentIntervalToleranceMs = 100;

    /// <summary>Aplicação do corte no plano vigente (lida do artefato): Applied só com o corte presente.</summary>
    private sealed record CutApplication(bool Applied, int? Revision, double? SourceStartSeconds, double? SourceEndSeconds);

    /// <summary>Resultado da fase sob lock: sugestão alvo e, quando já resolvida, o estado lido do plano.</summary>
    private sealed record DirectCutPrepared(EditSuggestion Suggestion, bool Settled, bool AlreadyApplied, CutApplication? State);

    public async Task<PlanOutcome<DirectCutResultDto>> ApplyDirectCutAsync(DirectCutRequest request, CancellationToken ct = default)
    {
        if (request.ProjectId == Guid.Empty)
            return PlanOutcome<DirectCutResultDto>.Fail("INVALID_PROJECT", "ID de projeto inválido.");
        if (request.MediaId == Guid.Empty)
            return PlanOutcome<DirectCutResultDto>.Fail("INVALID_MEDIA", "ID de mídia inválido.");
        if (request.BaseRevision is < 0)
            return PlanOutcome<DirectCutResultDto>.Fail("INVALID_ARGUMENT", "Revisão base inválida.");
        if (request.Rationale is not null && request.Rationale.Length > MaxNotesLength)
            return PlanOutcome<DirectCutResultDto>.Fail("INVALID_ARGUMENT", $"Justificativa com mais de {MaxNotesLength} caracteres.");

        var media = await _media.GetByIdAsync(request.MediaId, ct);
        if (media is null || media.ProjectId != request.ProjectId)
            return PlanOutcome<DirectCutResultDto>.Fail("MEDIA_NOT_FOUND", "Mídia não encontrada no projeto.");

        var interval = ValidateModifiedInterval(media, request.StartSeconds, request.EndSeconds);
        if (!interval.Success)
            return PlanOutcome<DirectCutResultDto>.Fail(interval.ErrorCode!, interval.ErrorMessage!);

        var project = await _projects.GetByIdAsync(request.ProjectId, ct);
        if (project is null || string.IsNullOrWhiteSpace(project.RootPath))
            return PlanOutcome<DirectCutResultDto>.Fail("PROJECT_NOT_FOUND", NoProjectMessage);

        // Fase 1 (sob lock): idempotência, revisão base, sugestão e decisão approve.
        var prepared = await WithProjectLockAsync(project.ProjectId,
            () => PrepareDirectCutAsync(project, request, interval.Value!, ct), ct);
        if (!prepared.Success)
            return PlanOutcome<DirectCutResultDto>.Fail(prepared.ErrorCode!, prepared.ErrorMessage!);

        var p = prepared.Value!;
        if (p.Settled)
        {
            var state = p.State ?? new CutApplication(false, null, null, null);
            return PlanOutcome<DirectCutResultDto>.Ok(await BuildDirectCutResultAsync(
                project.ProjectId, p.Suggestion, state, p.AlreadyApplied, planChanged: false, planError: null, skippedReason: null, ct));
        }

        // Fase 2 (fora do lock): materializa o corte no plano e confirma lendo o plano vigente.
        var reaction = await MaterializeDirectCutAsync(project.ProjectId, ct);
        var applied = await ReadCutApplicationAsync(project, p.Suggestion.SuggestionId, ct);

        var planError = reaction.PlanError;
        if (!applied.Applied && planError is null && reaction.SkippedReason is null)
            planError = CutNotAppliedCode;

        return PlanOutcome<DirectCutResultDto>.Ok(await BuildDirectCutResultAsync(
            project.ProjectId, p.Suggestion, applied, alreadyApplied: false, reaction.PlanChanged, planError, reaction.SkippedReason, ct));
    }

    /// <summary>Fase sob lock: acha ou cria a sugestão e grava a decisão approve (no máximo uma, atômica com a checagem).</summary>
    private async Task<PlanOutcome<DirectCutPrepared>> PrepareDirectCutAsync(
        Project project, DirectCutRequest request, ModifiedInterval interval, CancellationToken ct)
    {
        var suggestions = await _suggestionsAndDecisions.ListSuggestionsByProjectAsync(project.ProjectId, ct);
        var decisions = await _suggestionsAndDecisions.ListDecisionsByProjectAsync(project.ProjectId, ct);
        var effective = EffectiveSuggestionStatus.ResolveDetailed(suggestions, decisions);
        var latest = await _revisions.GetLatestAsync(project.ProjectId, ct);
        int currentRevision = latest?.Revision ?? 0;

        EditSuggestion? existing = null;
        bool foundByRequestId = false;
        if (request.RequestId is { } requestId && requestId != Guid.Empty)
        {
            var token = CutToken(requestId);
            existing = suggestions.FirstOrDefault(s => s.SuggestedAction == DirectCutAction && HasCutToken(s.EvidenceRefs, token));
            foundByRequestId = existing is not null;
        }
        existing ??= FindEquivalentCut(suggestions, effective, request.MediaId, interval);

        if (existing is not null)
        {
            var state = effective[existing.SuggestionId];
            bool approved = state.Status == SuggestionStatus.Approved;
            bool hasHistory = decisions.Any(d => d.SuggestionId == existing.SuggestionId);

            if (approved)
            {
                var plan = await ReadCutApplicationAsync(project, existing.SuggestionId, ct);
                if (plan.Applied) return PlanOutcome<DirectCutPrepared>.Ok(new DirectCutPrepared(existing, true, true, plan));
            }
            else if (foundByRequestId && hasHistory)
            {
                // Mesma requisição, mas a sugestão já teve decisão e hoje não está aprovada (desfeita/rejeitada depois):
                // não reaplica um corte que o usuário desfez. Estado atual devolvido sem gravar nada.
                var plan = await ReadCutApplicationAsync(project, existing.SuggestionId, ct);
                return PlanOutcome<DirectCutPrepared>.Ok(new DirectCutPrepared(existing, true, false, plan));
            }
        }

        if (request.BaseRevision is int baseRevision && baseRevision != currentRevision)
        {
            return PlanOutcome<DirectCutPrepared>.Fail(PlanStaleBaseCode,
                $"O plano mudou desde a leitura da interface (revisão {currentRevision}, esperada {baseRevision}). Recarregue e tente novamente.");
        }

        long startMs = interval.StartMs;
        long endMs = interval.EndMs;
        EditSuggestion suggestion;
        if (existing is not null)
        {
            suggestion = existing;
            var state = effective[existing.SuggestionId];
            startMs = state.StartMs;
            endMs = state.EndMs;
        }
        else
        {
            suggestion = new EditSuggestion
            {
                SuggestionId = Guid.NewGuid(),
                MediaId = request.MediaId,
                SourceInterval = new TravelCut.Domain.ValueObjects.TimeRange(startMs, endMs),
                Kind = SuggestionKind.Silence,
                SuggestedAction = DirectCutAction,
                Rationale = string.IsNullOrWhiteSpace(request.Rationale) ? DirectCutDefaultRationale : request.Rationale.Trim(),
                EvidenceRefs = request.RequestId is { } rid && rid != Guid.Empty
                    ? $"{DirectCutEvidencePrefix};{CutToken(rid)}"
                    : DirectCutEvidencePrefix,
                ConfidenceBand = "high",
                NeedsHumanReview = false,
                Status = SuggestionStatus.Proposed,
                Author = AuthorUser,
                CreatedAtUtc = _clock.GetUtcNow().UtcDateTime,
            };
            await _suggestionsAndDecisions.SaveSuggestionsBatchAsync([suggestion], ct);
        }

        // Atômico com a releitura do histórico: se já está aprovada (corrida/retomada), não grava outra decisão.
        int? baseRev = latest?.Revision;
        await _suggestionsAndDecisions.SaveDecisionIfAsync(suggestion.SuggestionId, history =>
        {
            var current = EffectiveSuggestionStatus.ResolveDetailed([suggestion], history)[suggestion.SuggestionId];
            if (current.Status == SuggestionStatus.Approved) return null;
            return NewDecision(suggestion.SuggestionId, EffectiveSuggestionStatus.ActionApprove, UserReviewer, DirectCutNotes,
                baseRev, startMs, endMs);
        }, ct);

        return PlanOutcome<DirectCutPrepared>.Ok(new DirectCutPrepared(suggestion, false, false, null));
    }

    /// <summary>
    /// Fase 2: sem plano, gera o primeiro (perfil do projeto, base de tempo automática); com plano, regenera como qualquer
    /// decisão. Não lança (exceto cancelamento): falhas viram PlanError, pois a decisão já está gravada.
    /// </summary>
    private async Task<PlanReaction> MaterializeDirectCutAsync(Guid projectId, CancellationToken ct)
    {
        int? revisionBefore = null;
        try
        {
            var before = await _revisions.GetLatestAsync(projectId, ct);
            revisionBefore = before?.Revision;
            if (before is not null) return await ReactToDecisionsAsync(projectId, ct);

            var generated = await GenerateAsync(new GeneratePlanRequest(projectId, null, null), ct);
            if (!generated.Success)
            {
                _logger.LogWarning("Corte direto gravado, mas a geração do primeiro plano falhou ({Code})", generated.ErrorCode);
                return IsPlanPreservedCode(generated.ErrorCode)
                    ? new PlanReaction(null, false, null, generated.ErrorCode)
                    : new PlanReaction(null, false, generated.ErrorCode);
            }

            var after = await _revisions.GetLatestAsync(projectId, ct);
            int? revision = after?.Revision ?? generated.Value?.Revision;
            return new PlanReaction(revision, revision != revisionBefore, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Corte direto gravado, mas a geração do plano lançou exceção (projeto {ProjectId})", projectId);
            return new PlanReaction(revisionBefore, false, PlanRegenerationFailedCode);
        }
    }

    /// <summary>Lê o plano vigente e confirma se a sugestão consta como corte aplicado (resultado efetivo).</summary>
    private async Task<CutApplication> ReadCutApplicationAsync(Project project, Guid suggestionId, CancellationToken ct)
    {
        try
        {
            var entry = await _revisions.GetLatestAsync(project.ProjectId, ct);
            if (entry is null) return new CutApplication(false, null, null, null);

            var read = await ReadPlanAsync(project.RootPath, entry, ct);
            if (read.Plan is not { } plan) return new CutApplication(false, entry.Revision, null, null);

            var cut = plan.AppliedCuts?.FirstOrDefault(c =>
                c.SuggestionId == suggestionId && c.Kind == DirectCutAction && c.Origin == EditPlanner.OriginApproved);
            if (cut is not null)
                return new CutApplication(true, entry.Revision, cut.SourceInUs / 1_000_000d, cut.SourceOutUs / 1_000_000d);

            bool effect = plan.AppliedEffects?.Any(e =>
                e.SuggestionId == suggestionId && e.Kind == DirectCutAction && e.Origin == "human") == true;
            return new CutApplication(effect, entry.Revision, null, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Não foi possível confirmar o corte no plano vigente (projeto {ProjectId})", project.ProjectId);
            return new CutApplication(false, null, null, null);
        }
    }

    private async Task<DirectCutResultDto> BuildDirectCutResultAsync(
        Guid projectId, EditSuggestion suggestion, CutApplication state, bool alreadyApplied, bool planChanged,
        string? planError, string? skippedReason, CancellationToken ct)
    {
        var dto = (await AffectedSuggestionsAsync([suggestion], projectId, ct)).Single();
        int? revision = state.Revision ?? await CurrentBaseRevisionAsync(projectId, ct);
        return new DirectCutResultDto(
            dto, state.Applied, alreadyApplied, revision, planChanged, planError, skippedReason,
            state.SourceStartSeconds, state.SourceEndSeconds);
    }

    private static string CutToken(Guid requestId) => $"cut:{requestId:D}";

    private static bool HasCutToken(string? evidenceRefs, string token) =>
        evidenceRefs is not null
        && evidenceRefs.StartsWith(DirectCutEvidencePrefix, StringComparison.Ordinal)
        && evidenceRefs.Split(';').Contains(token, StringComparer.Ordinal);

    /// <summary>Sugestão de corte da mesma mídia com intervalo efetivo equivalente (≤ 100 ms); aprovadas e mais próximas primeiro.</summary>
    private static EditSuggestion? FindEquivalentCut(
        IReadOnlyList<EditSuggestion> suggestions,
        IReadOnlyDictionary<Guid, EffectiveSuggestion> effective,
        Guid mediaId,
        ModifiedInterval interval) =>
        suggestions
            .Where(s => s.MediaId == mediaId && s.SuggestedAction == DirectCutAction)
            .Select(s => (Suggestion: s, State: effective[s.SuggestionId]))
            .Where(x => Math.Abs(x.State.StartMs - interval.StartMs) <= EquivalentIntervalToleranceMs
                        && Math.Abs(x.State.EndMs - interval.EndMs) <= EquivalentIntervalToleranceMs)
            .OrderByDescending(x => x.State.Status == SuggestionStatus.Approved)
            .ThenBy(x => Math.Abs(x.State.StartMs - interval.StartMs) + Math.Abs(x.State.EndMs - interval.EndMs))
            .ThenBy(x => x.Suggestion.SuggestionId)
            .Select(x => x.Suggestion)
            .FirstOrDefault();
}
