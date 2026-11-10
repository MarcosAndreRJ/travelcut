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
    public static EditSuggestionDto ToDto(EditSuggestion suggestion, EffectiveSuggestion effective,
        PlanAppliedEffectDto? appliedEffect = null, bool? batchApprovable = null)
    {
        var dto = SuggestionStorageMapper.ToDto(suggestion);

        // F14.8: origem. Decisão humana ativa vence; "auto" só quando o plano vigente autoaplicou pela política.
        string? origin = null;
        string? policyVersion = null;
        string? policyReason = null;
        if (effective.CanUndo)
        {
            origin = "human";
        }
        else if (appliedEffect is { Origin: "auto" })
        {
            origin = "auto";
            policyVersion = appliedEffect.PolicyVersion;
            policyReason = appliedEffect.Reason;
        }

        // Parâmetros efetivos: da decisão humana ativa; senão os da própria sugestão de zoom (recomendador).
        DecisionParametersDto? parameters = DecisionParameters.Parse(effective.ParametersJson);
        if (parameters?.Zoom is null && suggestion.SuggestedAction == "zoom" && !string.IsNullOrWhiteSpace(suggestion.EvidenceRefs))
        {
            try
            {
                var own = System.Text.Json.JsonSerializer.Deserialize<SmartZoomParametersDto>(suggestion.EvidenceRefs);
                if (own is not null) parameters = new DecisionParametersDto(own);
            }
            catch (System.Text.Json.JsonException) { /* EvidenceRefs não é JSON de zoom (ex.: "manual"): sem parâmetros. */ }
        }

        return dto with
        {
            Status = effective.Status.ToString(),
            EffectiveStartSeconds = effective.Adjusted ? SuggestionStorageMapper.ToSeconds(effective.StartMs) : null,
            EffectiveEndSeconds = effective.Adjusted ? SuggestionStorageMapper.ToSeconds(effective.EndMs) : null,
            CanUndo = effective.CanUndo,
            CanRedo = effective.CanRedo,
            Origin = origin,
            PolicyVersion = policyVersion,
            PolicyReason = policyReason,
            Parameters = parameters,
            BatchApprovable = batchApprovable,
        };
    }
}
