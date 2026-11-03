namespace TravelCut.Domain.Entities;

public class EditDecision
{
    public Guid DecisionId { get; set; } = Guid.NewGuid();
    public Guid SuggestionId { get; set; }
    public string Action { get; set; } = "approve"; // approve, reject, defer, modify, undo
    public string Reviewer { get; set; } = "user";
    public string Notes { get; set; } = string.Empty;
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public int Version { get; set; } = 1;

    /// <summary>Revisão do plano vigente no momento da decisão (null = projeto sem plano).</summary>
    public int? BaseRevision { get; set; }

    /// <summary>
    /// Intervalo de origem decidido, em milissegundos da mídia fonte (mesma unidade de <see cref="EditSuggestion.SourceInterval"/>).
    /// Approve/reject/defer gravam o intervalo efetivo atual; modify grava o ajustado; undo não grava intervalo (null).
    /// Null em decisões antigas: vale o intervalo original da sugestão.
    /// </summary>
    public long? SourceStartMs { get; set; }
    public long? SourceEndMs { get; set; }

    /// <summary>
    /// Verdadeiro para a linha de refazer (redo, ADR-014): Action = ação desfeita, mesmo intervalo, Notes "refeito".
    /// Marca explícita (não inferida de Notes) para o estado em pilha. Decisões antigas: false (ver migração do DbContext).
    /// </summary>
    public bool IsRedo { get; set; }
}
