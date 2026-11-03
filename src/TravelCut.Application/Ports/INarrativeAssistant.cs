using TravelCut.Contracts.Dtos;

namespace TravelCut.Application.Ports;

/// <summary>
/// Porta para decisões criativas futuras (ex.: assistente via Claude). O planejador determinístico NÃO depende dela.
/// Nenhuma implementação real nesta fase: <c>NullNarrativeAssistant</c> responde "not_configured".
/// </summary>
public interface INarrativeAssistant
{
    Task<NarrativeAssistantResult> ProposeAsync(EditPlanDto plan, CancellationToken ct = default);
}

/// <summary>Status: not_configured | ok | unavailable | failed. Mensagem em PT-BR, sem dados sensíveis.</summary>
public sealed record NarrativeAssistantResult(string Status, string Message);
