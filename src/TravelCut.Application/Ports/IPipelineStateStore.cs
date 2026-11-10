using TravelCut.Contracts.Dtos;

namespace TravelCut.Application.Ports;

/// <summary>Lote pendente do pipeline persistido (F14.8/P07). MediaIds null = todas as mídias do projeto.</summary>
public sealed record PersistedPendingBatch(List<string> Stages, string Language, bool Force, List<Guid>? MediaIds);

/// <summary>
/// Persistência leve (JSON em <c>3-Trabalho/.state</c>) do que antes só existia em memória: fila de lotes pendentes do
/// pipeline e último estado do pós-análise. Melhor esforço, síncrona (arquivos pequenos) e sem banco: falha de leitura
/// devolve vazio; falha de escrita propaga para o chamador registrar em log. Nunca toca mídia original.
/// </summary>
public interface IPipelineStateStore
{
    IReadOnlyList<PersistedPendingBatch> LoadPending(string projectRoot);

    /// <summary>Lista vazia remove o arquivo.</summary>
    void SavePending(string projectRoot, IReadOnlyList<PersistedPendingBatch> batches);

    PostAnalysisStatusDto? LoadPostAnalysis(string projectRoot);

    void SavePostAnalysis(string projectRoot, PostAnalysisStatusDto status);
}
