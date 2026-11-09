using System.Text.Json.Serialization;

namespace TravelCut.Contracts.Dtos;

// Contrato do perfil de automação editorial por projeto (F14.8 / I1).
// Métodos IPC: automation.profile.get, automation.profile.set.
// Vocabulário: "conservative" | "balanced" | "aggressive". Projeto sem escolha (ou anterior ao campo) = "balanced" (isDefault=true).
// plan.generate sem 'profile' usa o perfil persistido; com 'profile' explícito, o explícito vence (sem persistir).

public record GetAutomationProfileRequest(
    [property: JsonPropertyName("projectId")] Guid ProjectId
);

public record SetAutomationProfileRequest(
    [property: JsonPropertyName("projectId")] Guid ProjectId,
    [property: JsonPropertyName("profile")] string Profile
);

public record AutomationProfileDto(
    [property: JsonPropertyName("projectId")] Guid ProjectId,
    [property: JsonPropertyName("profile")] string Profile,
    [property: JsonPropertyName("isDefault")] bool IsDefault   // true = nunca escolhido pelo usuário (valor padrão aplicado)
);
