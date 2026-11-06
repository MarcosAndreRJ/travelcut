using System.Text.Json.Serialization;

namespace TravelCut.Contracts.Dtos;

/// <summary>
/// Estados padronizados e tipados das ferramentas e integrações do ambiente.
/// </summary>
public static class ToolEnvironmentState
{
    public const string NotInstalled = "NOT_INSTALLED";
    public const string InstalledNotFound = "INSTALLED_NOT_FOUND";
    public const string NotConfigured = "NOT_CONFIGURED";
    public const string DependencyMissing = "DEPENDENCY_MISSING";
    public const string ModelMissing = "MODEL_MISSING";
    public const string ReadyCpu = "READY_CPU";
    public const string ReadyGpu = "READY_GPU";
    public const string Ready = "READY";
    public const string Error = "ERROR";
    public const string NotVerified = "NOT_VERIFIED";
}

public static class ToolActionKind
{
    public const string Diagnose = "diagnose";
    public const string Locate = "locate";
    public const string ConfigurePath = "configure_path";
    public const string Install = "install";
    public const string DownloadModel = "download_model";
    public const string Test = "test";
}

public sealed record ToolDiagnosticDto(
    [property: JsonPropertyName("toolKey")] string ToolKey,
    [property: JsonPropertyName("displayName")] string DisplayName,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("isReady")] bool IsReady,
    [property: JsonPropertyName("version")] string? Version,
    [property: JsonPropertyName("executablePath")] string? ExecutablePath,
    [property: JsonPropertyName("details")] string Details,
    [property: JsonPropertyName("supportedActions")] IReadOnlyList<string> SupportedActions,
    [property: JsonPropertyName("winGetPackageId")] string? WinGetPackageId = null,
    [property: JsonPropertyName("metadata")] IReadOnlyDictionary<string, string>? Metadata = null
);

public sealed record SystemInfoDto(
    [property: JsonPropertyName("osDescription")] string OsDescription,
    [property: JsonPropertyName("architecture")] string Architecture,
    [property: JsonPropertyName("isWindows")] bool IsWindows,
    [property: JsonPropertyName("totalMemoryMb")] long TotalMemoryMb,
    [property: JsonPropertyName("freeDiskSpaceMb")] long FreeDiskSpaceMb,
    [property: JsonPropertyName("targetCacheDirectory")] string TargetCacheDirectory,
    [property: JsonPropertyName("hasWinget")] bool HasWinget = false,
    [property: JsonPropertyName("wingetPath")] string? WingetPath = null
);

public sealed record EnvironmentAssistantDiagnosticDto(
    [property: JsonPropertyName("tools")] IReadOnlyList<ToolDiagnosticDto> Tools,
    [property: JsonPropertyName("allReady")] bool AllReady,
    [property: JsonPropertyName("evaluatedAtUtc")] DateTime EvaluatedAtUtc,
    [property: JsonPropertyName("systemInfo")] SystemInfoDto SystemInfo
);

public sealed record InstallStepDto(
    [property: JsonPropertyName("stepNumber")] int StepNumber,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("command")] string Command,
    [property: JsonPropertyName("description")] string Description
);

public sealed record EnvironmentInstallPlanDto(
    [property: JsonPropertyName("toolKey")] string ToolKey,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("canAutoInstall")] bool CanAutoInstall,
    [property: JsonPropertyName("estimatedTime")] string EstimatedTime,
    [property: JsonPropertyName("estimatedDownloadSize")] string EstimatedDownloadSize,
    [property: JsonPropertyName("requiresElevation")] bool RequiresElevation,
    [property: JsonPropertyName("requiresUserConfirmation")] bool RequiresUserConfirmation,
    [property: JsonPropertyName("steps")] IReadOnlyList<InstallStepDto> Steps,
    [property: JsonPropertyName("warning")] string? Warning = null,
    [property: JsonPropertyName("approvalToken")] string? ApprovalToken = null
);

public sealed record StartToolInstallRequest(
    [property: JsonPropertyName("toolKey")] string ToolKey,
    [property: JsonPropertyName("confirmed")] bool Confirmed = true,
    [property: JsonPropertyName("customInstallPath")] string? CustomInstallPath = null,
    [property: JsonPropertyName("preferCuda")] bool PreferCuda = false,
    [property: JsonPropertyName("approvalToken")] string? ApprovalToken = null
);

public sealed record ToolInstallProgressDto(
    [property: JsonPropertyName("jobId")] Guid JobId,
    [property: JsonPropertyName("toolKey")] string ToolKey,
    [property: JsonPropertyName("stage")] string Stage,
    [property: JsonPropertyName("progressPercent")] int? ProgressPercent,
    [property: JsonPropertyName("currentMessage")] string CurrentMessage,
    [property: JsonPropertyName("outputLogs")] IReadOnlyList<string> OutputLogs,
    [property: JsonPropertyName("canCancel")] bool CanCancel,
    [property: JsonPropertyName("isCompleted")] bool IsCompleted,
    [property: JsonPropertyName("isSuccess")] bool IsSuccess,
    [property: JsonPropertyName("isFailed")] bool IsFailed,
    [property: JsonPropertyName("errorMessage")] string? ErrorMessage
);

public sealed record StartModelDownloadRequest(
    [property: JsonPropertyName("modelName")] string ModelName = "large-v3",
    [property: JsonPropertyName("confirmed")] bool Confirmed = true
);

public sealed record ConfigureToolPathRequest(
    [property: JsonPropertyName("toolKey")] string ToolKey,
    [property: JsonPropertyName("path")] string Path
);

public sealed record TestToolRequest(
    [property: JsonPropertyName("toolKey")] string ToolKey
);

public sealed record TestToolResponse(
    [property: JsonPropertyName("toolKey")] string ToolKey,
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("details")] string? Details,
    [property: JsonPropertyName("outputLogs")] IReadOnlyList<string> OutputLogs
);

public sealed record AutoLocateResponse(
    [property: JsonPropertyName("toolKey")] string ToolKey,
    [property: JsonPropertyName("candidates")] IReadOnlyList<string> Candidates
);
