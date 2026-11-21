using Microsoft.Extensions.Logging;
using TravelCut.Contracts.Dtos;
using TravelCut.Domain.Entities;
using TravelCut.Domain.Enums;

namespace TravelCut.Application.Planning;

/// <summary>
/// Zoom direto por intervalo (edição por transcrição; IPC "zoom.apply"): espelha <see cref="ApplyDirectCutAsync"/>. Cria (ou
/// reaproveita) uma sugestão manual "zoom", grava a decisão approve com parameters(scope "interval"), gera o plano se não existe
/// e confirma lendo o plano vigente: applied só é true quando o clipe V1 tem o zoom (clip.zoom* + AppliedEffects), nunca por presunção.
/// Regras:
/// - Idempotência: requestId gravado em EvidenceRefs ("manual;zoom:&lt;guid&gt;"); mesma requisição, ou mesma mídia com intervalo
///   equivalente (≤ 100 ms) já aprovado com os MESMOS parâmetros e aplicado, converge (alreadyApplied = true). Mesmo intervalo com
///   parâmetros diferentes grava nova decisão approve na mesma sugestão (editar o zoom); desfazer volta ao zoom anterior.
/// - Escala 1,01–1,12 (fora: INVALID_ARGUMENT, sem clamp), foco 0–1, rampas ≥ 0 com soma ≤ duração do intervalo.
/// - baseRevision divergente: PLAN_STALE_BASE (nada gravado). Plano protegido/falha: decisão gravada, applied = false, estado parcial
///   em planSkippedReason / planError (não lança).
/// - Um zoom por trecho mantido: se o trecho de maior sobreposição já tem zoom aprovado por humano de OUTRA sugestão, recusa antes de
///   gravar (ZOOM_CLIP_OCCUPIED). Quando só se descobre depois (plano ainda sem esse zoom), applied = false com planError
///   ZOOM_CLIP_OCCUPIED / ZOOM_NOT_APPLIED e a sugestão fica aprovada (desfazer/rejeitar por decision.undo / decision.submit).
/// - Remover: decision.undo (ou reject) da sugestão; o plano é regenerado sem o zoom e os cortes/outros zooms permanecem.
/// </summary>
public sealed partial class PlanningUseCases
{
    /// <summary>planError quando o plano foi gerado/lido mas não contém o zoom (ex.: intervalo fora dos clipes mantidos).</summary>
    public const string ZoomNotAppliedCode = "ZOOM_NOT_APPLIED";

    /// <summary>O trecho mantido já tem outro zoom aprovado por humano (um zoom por trecho).</summary>
    public const string ZoomClipOccupiedCode = "ZOOM_CLIP_OCCUPIED";

    private const string DirectZoomDefaultRationale = "Zoom direto criado na Biblioteca";
    private const string DirectZoomNotes = "zoom direto";
    private const string DirectZoomAction = "zoom";

    private sealed record ZoomApplication(bool Applied, int? Revision, DirectZoomAppliedDto? Zoom, bool OccupiedByOther);

    private sealed record DirectZoomPrepared(EditSuggestion Suggestion, bool Settled, bool AlreadyApplied, ZoomApplication? State);

    public async Task<PlanOutcome<DirectZoomResultDto>> ApplyDirectZoomAsync(DirectZoomRequest request, CancellationToken ct = default)
    {
        if (request.ProjectId == Guid.Empty)
            return PlanOutcome<DirectZoomResultDto>.Fail("INVALID_PROJECT", "ID de projeto inválido.");
        if (request.MediaId == Guid.Empty)
            return PlanOutcome<DirectZoomResultDto>.Fail("INVALID_MEDIA", "ID de mídia inválido.");
        if (request.BaseRevision is < 0)
            return PlanOutcome<DirectZoomResultDto>.Fail("INVALID_ARGUMENT", "Revisão base inválida.");
        if (request.Rationale is not null && request.Rationale.Length > MaxNotesLength)
            return PlanOutcome<DirectZoomResultDto>.Fail("INVALID_ARGUMENT", $"Justificativa com mais de {MaxNotesLength} caracteres.");

        var media = await _media.GetByIdAsync(request.MediaId, ct);
        if (media is null || media.ProjectId != request.ProjectId)
            return PlanOutcome<DirectZoomResultDto>.Fail("MEDIA_NOT_FOUND", "Mídia não encontrada no projeto.");

        var interval = ValidateModifiedInterval(media, request.StartSeconds, request.EndSeconds);
        if (!interval.Success)
            return PlanOutcome<DirectZoomResultDto>.Fail(interval.ErrorCode!, interval.ErrorMessage!);

        // Parâmetros tipados: escala 1,01–1,12 sem clamp (o valor exibido é o aplicado), foco 0–1, rampas dentro do intervalo.
        if (!double.IsFinite(request.Scale) || request.Scale < DecisionParameters.MinZoomScale || request.Scale > DecisionParameters.MaxZoomScale)
            return PlanOutcome<DirectZoomResultDto>.Fail("INVALID_ARGUMENT", "Escala do zoom deve estar entre 1,01 e 1,12.");
        double intervalSeconds = (interval.Value!.EndMs - interval.Value.StartMs) / 1000d;
        var zoomParams = new SmartZoomParametersDto(
            SmartZoomConstants.TypePunchIn, request.Scale, request.FocalPointX, request.FocalPointY, intervalSeconds,
            1.0, request.Scale, null, true, null, request.TransitionInSeconds, request.TransitionOutSeconds, SmartZoomConstants.ScopeInterval);
        var (parametersJson, parametersError) = DecisionParameters.Normalize(new DecisionParametersDto(zoomParams), DirectZoomAction, intervalSeconds);
        if (parametersError is not null || parametersJson is null)
            return PlanOutcome<DirectZoomResultDto>.Fail("INVALID_ARGUMENT", parametersError ?? "Parâmetros de zoom inválidos.");

        var project = await _projects.GetByIdAsync(request.ProjectId, ct);
        if (project is null || string.IsNullOrWhiteSpace(project.RootPath))
            return PlanOutcome<DirectZoomResultDto>.Fail("PROJECT_NOT_FOUND", NoProjectMessage);

        var prepared = await WithProjectLockAsync(project.ProjectId,
            () => PrepareDirectZoomAsync(project, request, interval.Value, parametersJson, ct), ct);
        if (!prepared.Success)
            return PlanOutcome<DirectZoomResultDto>.Fail(prepared.ErrorCode!, prepared.ErrorMessage!);

        var p = prepared.Value!;
        if (p.Settled)
        {
            var state = p.State ?? new ZoomApplication(false, null, null, false);
            return PlanOutcome<DirectZoomResultDto>.Ok(await BuildDirectZoomResultAsync(
                project.ProjectId, p.Suggestion, state, p.AlreadyApplied, planChanged: false, planError: null, skippedReason: null, ct));
        }

        var reaction = await MaterializeDirectCutAsync(project.ProjectId, ct);
        var applied = await ReadZoomApplicationAsync(project, p.Suggestion.SuggestionId, ct);

        var planError = reaction.PlanError;
        if (!applied.Applied && planError is null && reaction.SkippedReason is null)
            planError = applied.OccupiedByOther ? ZoomClipOccupiedCode : ZoomNotAppliedCode;

        return PlanOutcome<DirectZoomResultDto>.Ok(await BuildDirectZoomResultAsync(
            project.ProjectId, p.Suggestion, applied, alreadyApplied: false, reaction.PlanChanged, planError, reaction.SkippedReason, ct));
    }

    private async Task<PlanOutcome<DirectZoomPrepared>> PrepareDirectZoomAsync(
        Project project, DirectZoomRequest request, ModifiedInterval interval, string parametersJson, CancellationToken ct)
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
            var token = ZoomToken(requestId);
            existing = suggestions.FirstOrDefault(s => s.SuggestedAction == DirectZoomAction && HasCutToken(s.EvidenceRefs, token));
            foundByRequestId = existing is not null;
        }
        existing ??= FindEquivalentZoom(suggestions, effective, request.MediaId, interval);

        if (existing is not null)
        {
            var state = effective[existing.SuggestionId];
            bool approved = state.Status == SuggestionStatus.Approved;
            bool sameParameters = approved && SameZoomParameters(state.ParametersJson, parametersJson);
            bool hasHistory = decisions.Any(d => d.SuggestionId == existing.SuggestionId);

            if (sameParameters)
            {
                var plan = await ReadZoomApplicationAsync(project, existing.SuggestionId, ct);
                if (plan.Applied) return PlanOutcome<DirectZoomPrepared>.Ok(new DirectZoomPrepared(existing, true, true, plan));
            }
            else if (!approved && foundByRequestId && hasHistory)
            {
                // Mesma requisição cuja sugestão foi desfeita/rejeitada depois: não reaplica um zoom que o usuário removeu.
                var plan = await ReadZoomApplicationAsync(project, existing.SuggestionId, ct);
                return PlanOutcome<DirectZoomPrepared>.Ok(new DirectZoomPrepared(existing, true, false, plan));
            }
        }

        if (request.BaseRevision is int baseRevision && baseRevision != currentRevision)
        {
            return PlanOutcome<DirectZoomPrepared>.Fail(PlanStaleBaseCode,
                $"O plano mudou desde a leitura da interface (revisão {currentRevision}, esperada {baseRevision}). Recarregue e tente novamente.");
        }

        // Um zoom por trecho mantido: recusa antes de gravar quando o plano vigente já tem zoom humano de outra sugestão ali.
        var occupied = await FindOccupyingZoomAsync(project, request.MediaId, interval, existing?.SuggestionId, ct);
        if (occupied)
        {
            return PlanOutcome<DirectZoomPrepared>.Fail(ZoomClipOccupiedCode,
                "O trecho já tem outro zoom aprovado. Remova o zoom existente antes de aplicar um novo neste trecho.");
        }

        EditSuggestion suggestion;
        if (existing is not null)
        {
            suggestion = existing;
        }
        else
        {
            suggestion = new EditSuggestion
            {
                SuggestionId = Guid.NewGuid(),
                MediaId = request.MediaId,
                SourceInterval = new TravelCut.Domain.ValueObjects.TimeRange(interval.StartMs, interval.EndMs),
                Kind = SuggestionKind.SubtleZoom,
                SuggestedAction = DirectZoomAction,
                Rationale = string.IsNullOrWhiteSpace(request.Rationale) ? DirectZoomDefaultRationale : request.Rationale.Trim(),
                EvidenceRefs = request.RequestId is { } rid && rid != Guid.Empty
                    ? $"{DirectCutEvidencePrefix};{ZoomToken(rid)}"
                    : DirectCutEvidencePrefix,
                ConfidenceBand = "high",
                NeedsHumanReview = false,
                Status = SuggestionStatus.Proposed,
                Author = AuthorUser,
                CreatedAtUtc = _clock.GetUtcNow().UtcDateTime,
            };
            await _suggestionsAndDecisions.SaveSuggestionsBatchAsync([suggestion], ct);
        }

        int? baseRev = latest?.Revision;
        long startMs = interval.StartMs, endMs = interval.EndMs;
        await _suggestionsAndDecisions.SaveDecisionIfAsync(suggestion.SuggestionId, history =>
        {
            var current = EffectiveSuggestionStatus.ResolveDetailed([suggestion], history)[suggestion.SuggestionId];
            if (current.Status == SuggestionStatus.Approved
                && string.Equals(current.ParametersJson, parametersJson, StringComparison.Ordinal)
                && current.StartMs == startMs && current.EndMs == endMs) return null;
            return NewDecision(suggestion.SuggestionId, EffectiveSuggestionStatus.ActionApprove, UserReviewer, DirectZoomNotes,
                baseRev, startMs, endMs, parametersJson: parametersJson);
        }, ct);

        return PlanOutcome<DirectZoomPrepared>.Ok(new DirectZoomPrepared(suggestion, false, false, null));
    }

    /// <summary>
    /// True quando o trecho V1 de maior sobreposição com a janela, no plano vigente, tem zoom aprovado por humano de OUTRA sugestão.
    /// Melhor esforço: sem plano ou plano ilegível = false (o planejador ainda resolve o conflito com pendência).
    /// </summary>
    private async Task<bool> FindOccupyingZoomAsync(Project project, Guid mediaId, ModifiedInterval interval, Guid? exceptSuggestionId, CancellationToken ct)
    {
        try
        {
            var entry = await _revisions.GetLatestAsync(project.ProjectId, ct);
            if (entry is null) return false;
            var read = await ReadPlanAsync(project.RootPath, entry, ct);
            if (read.Plan is not { } plan) return false;

            long a = interval.StartMs * 1000, b = interval.EndMs * 1000;
            PlanClipDto? best = null;
            long bestOverlap = 0;
            foreach (var c in plan.Clips.Where(c => c.TrackId == EditPlanner.TrackV1 && c.MediaId == mediaId))
            {
                long overlap = Math.Min(c.SourceOutUs, b) - Math.Max(c.SourceInUs, a);
                if (overlap > bestOverlap) { best = c; bestOverlap = overlap; }
            }
            if (best?.Zoom is null) return false;

            var humanZoomIds = plan.AppliedEffects?
                .Where(e => e.Kind == DirectZoomAction && e.Origin == "human").Select(e => e.SuggestionId).ToHashSet() ?? [];
            return best.SuggestionIds.Any(id => humanZoomIds.Contains(id) && id != exceptSuggestionId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Não foi possível verificar zoom existente no trecho (projeto {ProjectId})", project.ProjectId);
            return false;
        }
    }

    /// <summary>Lê o plano vigente e confirma se a sugestão consta como zoom aplicado no clipe V1 (resultado efetivo).</summary>
    private async Task<ZoomApplication> ReadZoomApplicationAsync(Project project, Guid suggestionId, CancellationToken ct)
    {
        try
        {
            var entry = await _revisions.GetLatestAsync(project.ProjectId, ct);
            if (entry is null) return new ZoomApplication(false, null, null, false);

            var read = await ReadPlanAsync(project.RootPath, entry, ct);
            if (read.Plan is not { } plan) return new ZoomApplication(false, entry.Revision, null, false);

            bool effect = plan.AppliedEffects?.Any(e =>
                e.SuggestionId == suggestionId && e.Kind == DirectZoomAction && e.Origin == "human") == true;
            var clip = plan.Clips.FirstOrDefault(c =>
                c.TrackId == EditPlanner.TrackV1 && c.Zoom is not null && c.SuggestionIds.Contains(suggestionId));
            if (effect && clip is not null)
            {
                var fx = plan.AppliedEffects!.First(e => e.SuggestionId == suggestionId && e.Kind == DirectZoomAction);
                long inUs = clip.ZoomSourceInUs ?? fx.SourceInUs ?? clip.SourceInUs;
                long outUs = clip.ZoomSourceOutUs ?? fx.SourceOutUs ?? clip.SourceOutUs;
                var zoom = new DirectZoomAppliedDto(
                    clip.Zoom!.Value, clip.ZoomFocalPointX ?? 0.5, clip.ZoomFocalPointY ?? 0.5,
                    clip.ZoomTransitionInSeconds ?? 0, clip.ZoomTransitionOutSeconds ?? 0,
                    inUs / 1_000_000d, outUs / 1_000_000d);
                return new ZoomApplication(true, entry.Revision, zoom, false);
            }

            // Não aplicado: há pendência desta sugestão num clipe que já tem zoom (de outra sugestão)?
            var pending = plan.Pending.FirstOrDefault(p => p.SuggestionId == suggestionId && p.Kind == EditPlanner.ZoomSuggestionKind);
            bool occupied = pending?.ClipId is Guid clipId
                && plan.Clips.Any(c => c.ClipId == clipId && c.Zoom is not null && !c.SuggestionIds.Contains(suggestionId));
            return new ZoomApplication(false, entry.Revision, null, occupied);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Não foi possível confirmar o zoom no plano vigente (projeto {ProjectId})", project.ProjectId);
            return new ZoomApplication(false, null, null, false);
        }
    }

    private async Task<DirectZoomResultDto> BuildDirectZoomResultAsync(
        Guid projectId, EditSuggestion suggestion, ZoomApplication state, bool alreadyApplied, bool planChanged,
        string? planError, string? skippedReason, CancellationToken ct)
    {
        var dto = (await AffectedSuggestionsAsync([suggestion], projectId, ct)).Single();
        int? revision = state.Revision ?? await CurrentBaseRevisionAsync(projectId, ct);
        return new DirectZoomResultDto(dto, state.Applied, alreadyApplied, revision, planChanged, planError, skippedReason, state.Zoom);
    }

    private static string ZoomToken(Guid requestId) => $"zoom:{requestId:D}";

    /// <summary>Mesmos parâmetros efetivos (escala, foco, rampas, escopo); durationSeconds acompanha o intervalo e não conta.</summary>
    private static bool SameZoomParameters(string? a, string? b)
    {
        var x = DecisionParameters.Parse(a)?.Zoom;
        var y = DecisionParameters.Parse(b)?.Zoom;
        return x is not null && y is not null
            && x.Scale == y.Scale && x.FocalPointX == y.FocalPointX && x.FocalPointY == y.FocalPointY
            && (x.TransitionInSeconds ?? 0) == (y.TransitionInSeconds ?? 0) && (x.TransitionOutSeconds ?? 0) == (y.TransitionOutSeconds ?? 0)
            && x.Scope == y.Scope && x.ZoomType == y.ZoomType;
    }

    /// <summary>Sugestão de zoom da mesma mídia com intervalo efetivo equivalente (≤ 100 ms); aprovadas e mais próximas primeiro.</summary>
    private static EditSuggestion? FindEquivalentZoom(
        IReadOnlyList<EditSuggestion> suggestions,
        IReadOnlyDictionary<Guid, EffectiveSuggestion> effective,
        Guid mediaId,
        ModifiedInterval interval) =>
        suggestions
            .Where(s => s.MediaId == mediaId && s.SuggestedAction == DirectZoomAction && s.Author == AuthorUser)
            .Select(s => (Suggestion: s, State: effective[s.SuggestionId]))
            .Where(x => Math.Abs(x.State.StartMs - interval.StartMs) <= EquivalentIntervalToleranceMs
                        && Math.Abs(x.State.EndMs - interval.EndMs) <= EquivalentIntervalToleranceMs)
            .OrderByDescending(x => x.State.Status == SuggestionStatus.Approved)
            .ThenBy(x => Math.Abs(x.State.StartMs - interval.StartMs) + Math.Abs(x.State.EndMs - interval.EndMs))
            .ThenBy(x => x.Suggestion.SuggestionId)
            .Select(x => x.Suggestion)
            .FirstOrDefault();
}
