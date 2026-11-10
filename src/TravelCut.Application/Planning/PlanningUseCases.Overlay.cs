using Microsoft.Extensions.Logging;
using TravelCut.Application.Ports;
using TravelCut.Contracts.Dtos;
using TravelCut.Domain.Entities;
using TravelCut.Domain.Planning;

namespace TravelCut.Application.Planning;

/// <summary>Liga o motor de overlay (PlanManualEdit / ManualEditDeriver / PlanReconciler.Apply) ao fluxo real de geração e save (F14.7 incremento 3).</summary>
public sealed partial class PlanningUseCases
{
    /// <summary>Sufixo do autor de uma revisão regenerada que carrega overlay manual (ex.: planner-rules/1+manual).</summary>
    public const string ManualAuthorSuffix = "+manual";

    /// <summary>Ações sugeridas ao usuário quando o overlay não pode ser reaplicado (vão em IpcError.Details).</summary>
    public const string ManualProtectedSuggestedActions = "reconcile,discard_manual_edit";

    private const string ManualProtectedMessage =
        "As edições manuais não puderam ser reaplicadas com segurança; o plano foi preservado. Reconcilie o plano ou descarte a edição manual.";

    private const int MaxLegacyDepth = 16;

    /// <summary>Revisão cujo artefato precisa ser lido antes de regenerar: edição humana, revert ou regeneração com overlay.</summary>
    private static bool NeedsOverlayRead(string? author) => !IsAutomaticAuthor(author);

    private sealed record Overlay(IReadOnlyList<PlanManualEdit> Edits, string? ErrorCode = null, string? ErrorMessage = null)
    {
        public static readonly Overlay Empty = new(Array.Empty<PlanManualEdit>());

        public static Overlay Protected(string why) =>
            new(Array.Empty<PlanManualEdit>(), PlanManualProtectedCode, ManualProtectedMessage + " (" + why + ")");
    }

    /// <summary>
    /// Overlay herdado da revisão vigente. Automática pura: vazio. Com manualEdits: eles (fingerprint conferido). Manual legada (sem
    /// manualEdits): migração E = Derive(revisão BaseRevision lida do disco, revisão manual); base ilegível, adulterada ou ausente
    /// recusa com PLAN_MANUAL_PROTECTED.
    /// </summary>
    private async Task<Overlay> ResolveOverlayAsync(Project project, PlanRevisionIndexEntry? latest, CancellationToken ct)
    {
        if (latest is null || !NeedsOverlayRead(latest.Author)) return Overlay.Empty;
        var read = await ReadPlanAsync(project.RootPath, latest, ct);
        if (read.ErrorCode is not null)
            return new Overlay(Array.Empty<PlanManualEdit>(), PlanManualEditsUnreadableCode, ManualUnreadableMessage);
        return await OverlayOfAsync(project, read.Plan!, 0, ct);
    }

    private async Task<Overlay> OverlayOfAsync(Project project, EditPlanDto plan, int depth, CancellationToken ct)
    {
        if (!IsFingerprintIntact(plan)) return Overlay.Protected("a revisão não confere com o fingerprint gravado");
        if (plan.ManualEdits is { Count: > 0 } me) return new Overlay(ManualEdits.Compact(me.Select(PlanMapping.ToDomain)));
        if (plan.ManualEdits is not null || IsAutomaticAuthor(plan.Author)) return Overlay.Empty;

        // Revisão manual legada (autor humano ou revert, sem manualEdits): precisa da revisão base para derivar a intenção.
        if (depth >= MaxLegacyDepth || plan.BaseRevision is not int baseRevision)
            return Overlay.Protected("a revisão base da edição manual legada não é conhecida");
        var baseEntry = await _revisions.GetAsync(project.ProjectId, baseRevision, ct);
        if (baseEntry is null || baseEntry.PlanId != plan.PlanId)
            return Overlay.Protected("a revisão base da edição manual legada não existe");
        var baseRead = await ReadPlanAsync(project.RootPath, baseEntry, ct);
        if (baseRead.ErrorCode is not null)
            return Overlay.Protected("a revisão base da edição manual legada está ilegível");
        var baseOverlay = await OverlayOfAsync(project, baseRead.Plan!, depth + 1, ct);
        if (baseOverlay.ErrorCode is not null) return baseOverlay;

        var baseDomain = PlanMapping.ToDomain(baseRead.Plan!) with
        {
            ManualEdits = baseOverlay.Edits.Count > 0 ? baseOverlay.Edits.ToList() : null,
        };
        // Revert de uma base idêntica: Derive(r, r) é vazio (nada além do que a base já carrega).
        return new Overlay(ManualEditDeriver.Derive(baseDomain, PlanMapping.ToDomain(plan).Clips));
    }

    /// <summary>Resultado de reaplicar o overlay: plano (com overlay COMPLETO gravado) ou recusa estrutural.</summary>
    private sealed record OverlayApplied(EditPlanDto? Plan, string? ErrorCode = null, string? ErrorMessage = null);

    /// <summary>
    /// Apply(auto, E). Edições vencidas por decisão humana posterior são ignoradas na leitura, mas continuam no overlay do plano
    /// (desfazer a decisão as reativa). Com overlay o autor vira autor+manual; sem overlay devolve o plano automático intacto.
    /// </summary>
    private OverlayApplied ApplyOverlay(
        EditPlanDto auto, IReadOnlyList<PlanManualEdit> full, PlanningInput input, string? authorOverride = null)
    {
        if (full.Count == 0) return new OverlayApplied(auto);
        var active = ManualPrecedence.Active(full, input.Suggestions, input.Decisions);
        var eligible = PlanEligibility.Eligible(input.Media);
        var ctx = new PlanApplyContext(
            eligible.ToDictionary(m => m.MediaId, PlanEligibility.DurationUs),
            PlanEligibility.SpeechByMedia(eligible, input.Analyses));
        var applied = PlanReconciler.Apply(auto, active, PlanMapping.ToDomain(auto).TimeBase, ctx);
        if (applied.Refused)
        {
            _logger.LogWarning("Overlay manual recusado na reaplicação ({Count} erro(s) estrutural(is): {Codes})",
                applied.Refusals.Count, string.Join(",", applied.Refusals.Select(r => r.Code)));
            return new OverlayApplied(null, PlanManualProtectedCode, ManualProtectedMessage);
        }
        foreach (var c in applied.Conflicts)
            _logger.LogInformation("Overlay manual: conflito {Type} ({Severity}): {Message}", c.Type, c.Severity, c.Message);

        var plan = applied.Plan! with
        {
            ManualEdits = full.Select(PlanMapping.ToDto).ToList(),
            Author = authorOverride ?? (auto.Author + ManualAuthorSuffix),
        };
        return new OverlayApplied(plan with { PlanFingerprint = PlanFingerprint.ComputePlan(PlanMapping.ToDomain(plan)) });
    }
}
