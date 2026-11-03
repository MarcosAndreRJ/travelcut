namespace TravelCut.Application.Ports;

/// <summary>
/// Índice de uma revisão do plano. Uma linha por revisão, nunca atualizada (revisões são imutáveis).
/// O conteúdo completo fica no artefato JSON apontado por <see cref="RelativeJsonPath"/> (relativo à raiz do projeto).
/// </summary>
public sealed record PlanRevisionIndexEntry(
    Guid ProjectId,
    Guid PlanId,
    int Revision,
    int? BaseRevision,
    DateTime CreatedAtUtc,
    string Author,
    string PlanFingerprint,
    bool IsValid,
    int ClipCount,
    long TimelineDurationFrames,
    string RelativeJsonPath,
    string? InputsFingerprint = null);  // entradas (mídias + estado efetivo das sugestões + perfil/base/fuso) no momento da geração; null = revisão antiga

public interface IPlanRevisionRepository
{
    Task<PlanRevisionIndexEntry?> GetLatestAsync(Guid projectId, CancellationToken ct = default);
    Task<PlanRevisionIndexEntry?> GetAsync(Guid projectId, int revision, CancellationToken ct = default);
    Task<List<PlanRevisionIndexEntry>> ListAsync(Guid projectId, CancellationToken ct = default);

    /// <summary>Adiciona um índice novo. Falha se (ProjectId, Revision) já existir.</summary>
    Task AddAsync(PlanRevisionIndexEntry entry, CancellationToken ct = default);

    /// <summary>
    /// Marca que a revisão continua sendo o resultado das entradas atuais (regeneração idempotente).
    /// Altera só o metadado do índice; o artefato JSON da revisão nunca é reescrito.
    /// </summary>
    Task UpdateInputsFingerprintAsync(Guid projectId, int revision, string inputsFingerprint, CancellationToken ct = default);
}

/// <summary>
/// Revisão já existente no armazenamento de artefatos (gravação recusada para não sobrescrever).
/// É um <see cref="IOException"/> para manter o contrato de <see cref="IPlanArtifactStore.WriteRevisionAsync"/>;
/// os use cases a tratam como PLAN_REVISION_CONFLICT (não como erro interno).
/// </summary>
public sealed class PlanRevisionConflictException : IOException
{
    public PlanRevisionConflictException(string message) : base(message)
    {
    }
}

/// <summary>
/// Armazenamento dos artefatos JSON das revisões em &lt;projeto&gt;/3-Trabalho/plans/&lt;planId&gt;/r&lt;NNNN&gt;.json.
/// Escrita nova e atômica; nunca sobrescreve uma revisão existente.
/// </summary>
public interface IPlanArtifactStore
{
    /// <summary>
    /// Grava a revisão. Lança <see cref="PlanRevisionConflictException"/> (um IOException) se o arquivo da revisão já existir.
    /// </summary>
    Task WriteRevisionAsync(string projectRoot, Guid planId, int revision, string json, CancellationToken ct = default);

    /// <summary>Retorna o conteúdo ou null quando o arquivo não existe.</summary>
    Task<string?> ReadRevisionAsync(string projectRoot, Guid planId, int revision, CancellationToken ct = default);

    /// <summary>Números das revisões presentes em disco para o plano (arquivos rNNNN.json), independentemente do índice.</summary>
    Task<IReadOnlyList<int>> ListRevisionNumbersAsync(string projectRoot, Guid planId, CancellationToken ct = default);

    /// <summary>Remove apenas um arquivo recém-criado cuja linha de índice não foi gravada (compensação).</summary>
    Task DeleteUncommittedRevisionAsync(string projectRoot, Guid planId, int revision, CancellationToken ct = default);
}
