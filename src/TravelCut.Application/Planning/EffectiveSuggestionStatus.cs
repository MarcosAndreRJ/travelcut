using TravelCut.Domain.Entities;
using TravelCut.Domain.Enums;

namespace TravelCut.Application.Planning;

/// <summary>
/// Estado efetivo de uma sugestão calculado a partir do histórico de decisões.
/// StartMs/EndMs são o intervalo de origem efetivo (ms da mídia). Adjusted = diferente do intervalo original (modify).
/// CanUndo: há decisão de usuário ativa (pilha ativa não vazia). CanRedo: há decisão desfeita (pilha de desfeitas não vazia).
/// </summary>
public sealed record EffectiveSuggestion(
    Guid SuggestionId,
    SuggestionStatus Status,
    long StartMs,
    long EndMs,
    bool Adjusted,
    bool CanUndo,
    bool CanRedo,
    string? ParametersJson = null); // F14.8: parâmetros da decisão ativa (topo da pilha); null = nenhum

/// <summary>
/// Pilhas de decisões de uma sugestão (ADR-014). <see cref="Active"/>: decisões de usuário ativas, a última é o estado
/// efetivo. <see cref="Undone"/>: decisões desfeitas, a última é a próxima a ser refeita. Ambas em ordem de aplicação.
/// </summary>
public sealed record DecisionStacks(IReadOnlyList<EditDecision> Active, IReadOnlyList<EditDecision> Undone);

/// <summary>
/// Estado efetivo das sugestões a partir das decisões. Fonte ÚNICA usada por suggestions.list, pelo planejador
/// (EditPlanner), pelo resumo do projeto e pelas validações de undo/redo.
/// Regras (ADR-014, undo como pilha):
/// - Decisões são append-only e processadas em ordem de Version (não timestamp nem GUID).
/// - approve/reject/defer/modify (decisão de usuário) empilham em Active e LIMPAM a pilha Undone.
/// - undo desempilha o topo de Active e empilha em Undone (não grava nada além da própria linha de undo).
/// - redo (linha com <see cref="EditDecision.IsRedo"/> = true) desempilha o topo de Undone e reempilha em Active,
///   com a mesma ação e intervalo da decisão desfeita; não limpa Undone.
/// - Estado efetivo = topo de Active. Sem pilha ativa mas com histórico de undo → Proposed, intervalo original.
///   Sem nenhuma decisão, vale o status da própria sugestão (Proposed por padrão) e o intervalo original.
/// </summary>
public static class EffectiveSuggestionStatus
{
    public const string ActionApprove = "approve";
    public const string ActionReject = "reject";
    public const string ActionDefer = "defer";
    public const string ActionModify = "modify";
    public const string ActionReopen = "reopen";
    public const string ActionUndo = "undo";

    /// <summary>Ações de decisão do usuário aceitas em decision.submit (inclui modify e reopen).</summary>
    public static readonly IReadOnlyList<string> SubmitActions = [ActionApprove, ActionReject, ActionDefer, ActionModify, ActionReopen];

    /// <summary>Ações aceitas em suggestions.batchDecide: modify NÃO é permitido em lote.</summary>
    public static readonly IReadOnlyList<string> BatchActions = [ActionApprove, ActionReject, ActionDefer, ActionReopen];

    /// <summary>Verdadeiro para approve/reject/defer (valores exatos, sem variações).</summary>
    public static bool IsUserAction(string? action) =>
        action is not null && BatchActions.Contains(action, StringComparer.Ordinal);

    /// <summary>
    /// Mapeia uma ação (aceita também as formas no passado usadas em dados antigos) ao status.
    /// "modify" resulta em Approved: o usuário aprovou um intervalo ajustado.
    /// </summary>
    public static SuggestionStatus? ActionToStatus(string? action) => action?.ToLowerInvariant() switch
    {
        "approve" or "approved" or "modify" or "modified" => SuggestionStatus.Approved,
        "reject" or "rejected" => SuggestionStatus.Rejected,
        "defer" or "deferred" => SuggestionStatus.Deferred,
        "undo" or "reopen" or "reopened" => SuggestionStatus.Proposed,
        _ => null,
    };

    /// <summary>Verdadeiro para linha de undo (ação "undo", sem diferenciar maiúsculas).</summary>
    public static bool IsUndo(EditDecision decision) =>
        string.Equals(decision.Action, ActionUndo, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Pilhas de uma sugestão a partir do histórico completo (qualquer ordem; processado por Version).
    /// Undo/redo sem par correspondente (pilha vazia) são ignorados: a validação do use case recusa antes de gravar.
    /// </summary>
    public static DecisionStacks ComputeStacks(IEnumerable<EditDecision> history)
    {
        var active = new List<EditDecision>();
        var undone = new List<EditDecision>();
        foreach (var d in Ordered(history))
        {
            if (IsUndo(d))
            {
                if (active.Count == 0) continue;
                undone.Add(active[^1]);
                active.RemoveAt(active.Count - 1);
            }
            else if (d.IsRedo)
            {
                if (undone.Count == 0 || ActionToStatus(d.Action) is null) continue;
                undone.RemoveAt(undone.Count - 1);
                active.Add(d);
            }
            else if (ActionToStatus(d.Action) is not null)
            {
                active.Add(d);
                undone.Clear();
            }
            // Ação desconhecida: não altera as pilhas (o estado anterior continua valendo).
        }
        return new DecisionStacks(active, undone);
    }

    /// <summary>Estado efetivo (status, intervalo, canUndo, canRedo) de cada sugestão informada (chave = SuggestionId).</summary>
    public static Dictionary<Guid, EffectiveSuggestion> ResolveDetailed(
        IEnumerable<EditSuggestion> suggestions,
        IEnumerable<EditDecision> decisions)
    {
        var historyById = decisions
            .GroupBy(d => d.SuggestionId)
            .ToDictionary(g => g.Key, g => Ordered(g).ToList());

        var result = new Dictionary<Guid, EffectiveSuggestion>();
        foreach (var s in suggestions)
        {
            var history = historyById.TryGetValue(s.SuggestionId, out var h) ? h : [];
            var stacks = ComputeStacks(history);
            var top = stacks.Active.Count > 0 ? stacks.Active[^1] : null;

            SuggestionStatus status;
            if (top is not null) status = ActionToStatus(top.Action)!.Value;
            else if (history.Any(IsUndo)) status = SuggestionStatus.Proposed;
            else status = s.Status;

            long startMs = s.SourceInterval.StartTicks;
            long endMs = s.SourceInterval.EndTicks;
            if (top is not null && top.SourceStartMs is long ss && top.SourceEndMs is long se)
            {
                startMs = ss;
                endMs = se;
            }

            bool adjusted = startMs != s.SourceInterval.StartTicks || endMs != s.SourceInterval.EndTicks;
            result[s.SuggestionId] = new EffectiveSuggestion(
                s.SuggestionId, status, startMs, endMs, adjusted,
                CanUndo: stacks.Active.Count > 0,
                CanRedo: stacks.Undone.Count > 0,
                ParametersJson: top?.ParametersJson);
        }
        return result;
    }

    /// <summary>Status efetivo de cada sugestão informada (chave = SuggestionId), derivado de <see cref="ResolveDetailed"/>.</summary>
    public static Dictionary<Guid, SuggestionStatus> ResolveAll(
        IEnumerable<EditSuggestion> suggestions,
        IEnumerable<EditDecision> decisions) =>
        ResolveDetailed(suggestions, decisions).ToDictionary(kv => kv.Key, kv => kv.Value.Status);

    /// <summary>Quantidade de sugestões cujo estado efetivo é Approved (aprovar duas vezes conta uma).</summary>
    public static int CountApproved(IEnumerable<EditSuggestion> suggestions, IEnumerable<EditDecision> decisions) =>
        ResolveAll(suggestions, decisions).Values.Count(v => v == SuggestionStatus.Approved);

    /// <summary>Ordem de processamento: Version; desempates por timestamp e id só valem para dados legados em memória.</summary>
    private static IEnumerable<EditDecision> Ordered(IEnumerable<EditDecision> decisions) =>
        decisions.OrderBy(d => d.Version).ThenBy(d => d.TimestampUtc).ThenBy(d => d.DecisionId);
}
