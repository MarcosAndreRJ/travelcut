namespace TravelCut.Infrastructure.Planning;

/// <summary>
/// Linha de índice da tabela PlanRevisions (EF Core). Imutável por convenção: só INSERT; nunca UPDATE.
/// O conteúdo do plano fica no JSON apontado por <see cref="RelativeJsonPath"/>.
/// </summary>
public class PlanRevisionRecord
{
    public Guid PlanId { get; set; }
    public int Revision { get; set; }
    public Guid ProjectId { get; set; }
    public int? BaseRevision { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public string Author { get; set; } = string.Empty;
    public string PlanFingerprint { get; set; } = string.Empty;
    public bool IsValid { get; set; }
    public int ClipCount { get; set; }
    public long TimelineDurationFrames { get; set; }
    public string RelativeJsonPath { get; set; } = string.Empty;

    /// <summary>Fingerprint das entradas na geração (PLAN_OUTDATED). Nulo em revisões antigas e em reverts.</summary>
    public string? InputsFingerprint { get; set; }
}
