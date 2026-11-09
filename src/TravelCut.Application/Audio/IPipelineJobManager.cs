using TravelCut.Contracts.Dtos;

namespace TravelCut.Application.Audio;

/// <summary>Resultado de <see cref="IPipelineJobManager.EnqueueAsync"/>. Outcome: "started" (job iniciado agora) | "merged" (mídias entraram no lote pendente do projeto).</summary>
public sealed record PipelineEnqueueResult(string Outcome, Guid? JobId, int PendingMediaCount)
{
    public const string Started = "started";
    public const string Merged = "merged";
}

public interface IPipelineJobManager
{
    // Falha com InvalidOperationException("PIPELINE_IN_PROGRESS") se já houver job ativo no projeto.
    Task<Guid> StartAsync(StartPipelineRequest request, CancellationToken ct = default);
    Task<PipelineStatusDto> GetStatusAsync(Guid projectId, CancellationToken ct = default);
    /// <summary>
    /// Inicia o pipeline ou, se já houver um ativo no projeto, mescla as mídias num lote pendente que inicia quando o slot for liberado
    /// (nenhuma mídia é perdida; um único job por projeto). Implementação padrão sem coalescência: apenas inicia (PIPELINE_IN_PROGRESS propaga).
    /// </summary>
    async Task<PipelineEnqueueResult> EnqueueAsync(StartPipelineRequest request, CancellationToken ct = default)
        => new(PipelineEnqueueResult.Started, await StartAsync(request, ct), 0);

    bool Cancel(Guid jobId);
    Task<AnalysisEnvironmentDto> GetEnvironmentAsync(CancellationToken ct = default, bool forceRefresh = false);
    /// <summary>Ambiente já em cache (TTL válido), ou null. Nunca dispara probe: seguro em caminhos que não podem bloquear.</summary>
    AnalysisEnvironmentDto? PeekEnvironment() => null;
    /// <summary>Execuções do worker no job mais recente do projeto (caminhos locais: só por pedido explícito).</summary>
    PipelineDiagnosticsDto GetDiagnostics(Guid projectId) => new(null, new List<WhisperExecutionDto>());
    Task<MediaAnalysisDto?> GetMediaAnalysisAsync(Guid projectId, Guid mediaId, CancellationToken ct = default);
}
