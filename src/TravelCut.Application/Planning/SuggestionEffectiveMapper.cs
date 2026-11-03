using TravelCut.Contracts.Dtos;
using TravelCut.Domain.Entities;
using TravelCut.Application.UseCases;

namespace TravelCut.Application.Planning;

/// <summary>Monta o DTO de sugestão com o estado EFETIVO (status, intervalo ajustado, canUndo/canRedo).</summary>
public static class SuggestionEffectiveMapper
{
    /// <summary>
    /// effectiveStart/EndSeconds só são preenchidos quando o intervalo efetivo difere do original (modify);
    /// null significa "intervalo original" (contrato de EditSuggestionDto).
    /// </summary>
    public static EditSuggestionDto ToDto(EditSuggestion suggestion, EffectiveSuggestion effective)
    {
        var dto = SuggestionStorageMapper.ToDto(suggestion);
        return dto with
        {
            Status = effective.Status.ToString(),
            EffectiveStartSeconds = effective.Adjusted ? SuggestionStorageMapper.ToSeconds(effective.StartMs) : null,
            EffectiveEndSeconds = effective.Adjusted ? SuggestionStorageMapper.ToSeconds(effective.EndMs) : null,
            CanUndo = effective.CanUndo,
            CanRedo = effective.CanRedo,
        };
    }
}
