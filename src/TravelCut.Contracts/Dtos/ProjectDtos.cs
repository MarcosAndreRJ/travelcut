using System.Text.Json.Serialization;

namespace TravelCut.Contracts.Dtos;

public record ProjectSummaryDto(
    [property: JsonPropertyName("projectId")] Guid ProjectId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("rootPath")] string RootPath,
    [property: JsonPropertyName("fourFoldersValid")] bool FourFoldersValid,
    [property: JsonPropertyName("missingFolder")] string? MissingFolder,
    [property: JsonPropertyName("mediaCount")] int MediaCount,
    [property: JsonPropertyName("suggestionsCount")] int SuggestionsCount,
    [property: JsonPropertyName("approvedCutsCount")] int ApprovedCutsCount,
    // Soma apenas das mídias com metadataStatus "ok". Mídias sem metadados não entram na soma (ver mediaWithoutMetadataCount).
    [property: JsonPropertyName("totalOriginalDurationSeconds")] double TotalOriginalDurationSeconds,
    // null até existir estimativa real (sem dado de corte ainda). Nunca derivado de constante.
    [property: JsonPropertyName("estimatedCutDurationSeconds")] double? EstimatedCutDurationSeconds,
    // Quantidade de mídias cujo metadataStatus não é "ok" (a UI deve indicar que a soma é parcial).
    [property: JsonPropertyName("mediaWithoutMetadataCount")] int MediaWithoutMetadataCount,
    [property: JsonPropertyName("lastModifiedUtc")] DateTime LastModifiedUtc
);

public record OpenProjectRequest(
    [property: JsonPropertyName("rootPath")] string RootPath,
    [property: JsonPropertyName("autoCreateFolders")] bool AutoCreateFolders = true
);

public record OpenProjectResponse(
    [property: JsonPropertyName("project")] ProjectSummaryDto Project,
    [property: JsonPropertyName("summary")] ProjectSummaryDto Summary,
    [property: JsonPropertyName("status")] string Status
)
{
    public OpenProjectResponse(ProjectSummaryDto project, string status)
        : this(project, project, status) { }
}

public record CreateProjectRequest(
    [property: JsonPropertyName("rootPath")] string RootPath,
    [property: JsonPropertyName("name")] string? Name = null,
    [property: JsonPropertyName("createFoldersIfNotExists")] bool CreateFoldersIfNotExists = true,
    [property: JsonPropertyName("resolution")] string? Resolution = null,
    [property: JsonPropertyName("fps")] double? Fps = null
);

public record CreateProjectResponse(
    [property: JsonPropertyName("project")] ProjectSummaryDto Project,
    [property: JsonPropertyName("summary")] ProjectSummaryDto Summary,
    [property: JsonPropertyName("createdFolders")] List<string> CreatedFolders,
    [property: JsonPropertyName("status")] string Status
)
{
    public CreateProjectResponse(ProjectSummaryDto project, List<string> createdFolders, string status)
        : this(project, project, createdFolders, status) { }
}

public record SelectFolderResponse(
    [property: JsonPropertyName("selectedPath")] string SelectedPath,
    [property: JsonPropertyName("canceled")] bool Canceled
);

public record RecentProjectsResponse(
    [property: JsonPropertyName("projects")] List<ProjectSummaryDto> Projects
);
