using TravelCut.Application.Ports;
using TravelCut.Contracts.Dtos;

namespace TravelCut.Infrastructure.Planning;

/// <summary>Implementação padrão de <see cref="INarrativeAssistant"/>: nenhum assistente configurado nesta versão.</summary>
public sealed class NullNarrativeAssistant : INarrativeAssistant
{
    public Task<NarrativeAssistantResult> ProposeAsync(EditPlanDto plan, CancellationToken ct = default) =>
        Task.FromResult(new NarrativeAssistantResult(
            "not_configured",
            "Assistente narrativo não configurado nesta versão; o plano determinístico segue sem propostas criativas."));
}
