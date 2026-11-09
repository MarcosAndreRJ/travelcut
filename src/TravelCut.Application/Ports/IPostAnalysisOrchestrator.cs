using TravelCut.Contracts.Dtos;

namespace TravelCut.Application.Ports;

/// <summary>
/// Processamento automático após uma análise concluída (F14.8/I2b): gera o plano (perfil persistido) e enfileira proxies,
/// em segundo plano, sem IPC extra. Chamado pelo gerenciador de pipeline apenas em completed/completed_with_issues.
/// </summary>
public interface IPostAnalysisOrchestrator
{
    /// <summary>
    /// Agenda a execução para o projeto. O estado "running" é visível de forma síncrona. Nunca lança.
    /// A Task devolvida representa a execução em segundo plano (o chamador de produção a descarta).
    /// </summary>
    Task ScheduleAsync(Guid projectId, Guid jobId);

    /// <summary>Estado atual do projeto (idle quando nunca executou).</summary>
    PostAnalysisStatusDto GetStatus(Guid projectId);
}
