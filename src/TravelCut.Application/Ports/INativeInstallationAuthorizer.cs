namespace TravelCut.Application.Ports;

public sealed record NativeInstallationRequest(
    string ToolKey,
    string Title,
    string Description,
    IReadOnlyList<string> Steps,
    string? TargetDirectory,
    bool RequiresElevation
);

public sealed record NativeDownloadRequest(
    string ModelName,
    string DestinationDirectory,
    string DownloadSize
);

public interface INativeInstallationAuthorizer
{
    Task<bool> RequestInstallationAuthorizationAsync(
        NativeInstallationRequest request,
        CancellationToken ct = default);

    Task<bool> RequestDownloadAuthorizationAsync(
        NativeDownloadRequest request,
        CancellationToken ct = default);
}

public sealed class DenyAllNativeInstallationAuthorizer : INativeInstallationAuthorizer
{
    public static DenyAllNativeInstallationAuthorizer Instance { get; } = new();

    public Task<bool> RequestInstallationAuthorizationAsync(NativeInstallationRequest request, CancellationToken ct = default)
        => Task.FromResult(false);

    public Task<bool> RequestDownloadAuthorizationAsync(NativeDownloadRequest request, CancellationToken ct = default)
        => Task.FromResult(false);
}

public sealed class AutoApproveNativeInstallationAuthorizer : INativeInstallationAuthorizer
{
    public static AutoApproveNativeInstallationAuthorizer Instance { get; } = new();

    public Task<bool> RequestInstallationAuthorizationAsync(NativeInstallationRequest request, CancellationToken ct = default)
        => Task.FromResult(true);

    public Task<bool> RequestDownloadAuthorizationAsync(NativeDownloadRequest request, CancellationToken ct = default)
        => Task.FromResult(true);
}
