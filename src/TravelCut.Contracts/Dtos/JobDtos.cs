using System.Text.Json.Serialization;

namespace TravelCut.Contracts.Dtos;

public record JobDto(
    [property: JsonPropertyName("jobId")] Guid JobId,
    [property: JsonPropertyName("projectId")] Guid ProjectId,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("completedUnits")] int CompletedUnits,
    [property: JsonPropertyName("totalUnits")] int TotalUnits,
    [property: JsonPropertyName("progressPercentage")] double ProgressPercentage,
    [property: JsonPropertyName("currentStepDescription")] string CurrentStepDescription,
    [property: JsonPropertyName("errorMessage")] string? ErrorMessage,
    [property: JsonPropertyName("createdAtUtc")] DateTime CreatedAtUtc
);

public record StartJobRequest(
    [property: JsonPropertyName("projectId")] Guid ProjectId,
    [property: JsonPropertyName("type")] string Type
);
