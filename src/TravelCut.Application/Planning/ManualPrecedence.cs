using TravelCut.Domain.Entities;
using TravelCut.Domain.Enums;
using TravelCut.Domain.Planning;

namespace TravelCut.Application.Planning;

/// <summary>
/// Precedência (F14.7/04): edição manual &gt; config do projeto &gt; política automática. Única exceção: uma decisão humana de sugestão
/// (approve/reject efetivo, no topo da pilha ativa) tomada DEPOIS da edição (BaseRevision ≥ IntroducedInRevision) que sobreponha o
/// trecho da edição vence. A edição é ignorada NA LEITURA (continua guardada no overlay) e desfazer a decisão a reativa.
/// </summary>
public static class ManualPrecedence
{
    private sealed record Override(Guid MediaId, long StartUs, long EndUs, int BaseRevision);

    /// <summary>Edições que valem agora (o overlay completo permanece intacto no plano).</summary>
    public static IReadOnlyList<PlanManualEdit> Active(
        IReadOnlyList<PlanManualEdit> edits, IReadOnlyList<EditSuggestion> suggestions, IReadOnlyList<EditDecision> decisions)
    {
        if (edits.Count == 0) return edits;
        var overrides = Overrides(suggestions, decisions);
        if (overrides.Count == 0) return edits;
        return edits.Where(e => !overrides.Any(o => o.MediaId == e.MediaId && o.BaseRevision >= e.IntroducedInRevision && Overlaps(o, e))).ToList();
    }

    /// <summary>Edições ignoradas por uma decisão humana posterior (para diagnóstico e testes).</summary>
    public static IReadOnlyList<PlanManualEdit> Ignored(
        IReadOnlyList<PlanManualEdit> edits, IReadOnlyList<EditSuggestion> suggestions, IReadOnlyList<EditDecision> decisions)
    {
        var active = Active(edits, suggestions, decisions).Select(e => e.EditId).ToHashSet();
        return edits.Where(e => !active.Contains(e.EditId)).ToList();
    }

    private static List<Override> Overrides(IReadOnlyList<EditSuggestion> suggestions, IReadOnlyList<EditDecision> decisions)
    {
        var byId = decisions.GroupBy(d => d.SuggestionId).ToDictionary(g => g.Key, g => g.ToList());
        var result = new List<Override>();
        foreach (var s in suggestions)
        {
            if (!byId.TryGetValue(s.SuggestionId, out var history)) continue;
            var top = EffectiveSuggestionStatus.ComputeStacks(history).Active.LastOrDefault();
            if (top?.BaseRevision is not int baseRevision) continue;
            if (EffectiveSuggestionStatus.ActionToStatus(top.Action) is not (SuggestionStatus.Approved or SuggestionStatus.Rejected)) continue;
            long startMs = top.SourceStartMs ?? s.SourceInterval.StartTicks;
            long endMs = top.SourceEndMs ?? s.SourceInterval.EndTicks;
            result.Add(new Override(s.MediaId, startMs * 1000, endMs * 1000, baseRevision));
        }
        return result;
    }

    private static bool Overlaps(Override o, PlanManualEdit e) => e.Kind == ManualEditKinds.Split
        ? o.StartUs < e.SourceInUs && e.SourceInUs < o.EndUs
        : e.SourceInUs < o.EndUs && e.SourceOutUs > o.StartUs;
}
