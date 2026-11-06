using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using TravelCut.Application.Audio;
using TravelCut.Application.Ipc;
using TravelCut.Application.Ports;
using TravelCut.Contracts.Dtos;
using TravelCut.Contracts.Ipc;
using TravelCut.Infrastructure.Settings;
using TravelCut.Infrastructure.Tooling;
using TravelCut.UnitTests.Audio;
using Xunit;

namespace TravelCut.UnitTests.Infrastructure;

public sealed class EnvironmentSecurityAuditTests : IDisposable
{
    private readonly string _tempDir;
    private readonly TravelCutSettingsService _settingsService;
    private readonly FakeWhisperService _whisperService;
    private readonly FakeProcessRunner _processRunner;
    private readonly FakeModelDownloader _modelDownloader;
    private readonly FakeNativeInstallationAuthorizer _nativeAuthorizer;
    private readonly EnvironmentAssistantService _assistant;

    public EnvironmentSecurityAuditTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "tc-sec-audit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        _settingsService = new TravelCutSettingsService(
            NullLogger<TravelCutSettingsService>.Instance,
            Path.Combine(_tempDir, "settings.json")
        );
        _whisperService = new FakeWhisperService();
        _processRunner = new FakeProcessRunner();
        _modelDownloader = new FakeModelDownloader();
        _nativeAuthorizer = new FakeNativeInstallationAuthorizer();

        _assistant = new EnvironmentAssistantService(
            _processRunner,
            _whisperService,
            _settingsService,
            _modelDownloader,
            NullLogger<EnvironmentAssistantService>.Instance,
            TimeProvider.System,
            _nativeAuthorizer
        );
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    [Fact]
    public async Task StartInstall_WithoutConfirmation_ThrowsInvalidOperationException()
    {
        var plan = await _assistant.GetInstallPlanAsync("python");
        var req = new StartToolInstallRequest("python", Confirmed: false, ApprovalToken: plan.ApprovalToken);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _assistant.StartInstallAsync(req));
        Assert.Contains("confirmação explícita", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StartInstall_WithoutApprovalToken_ThrowsInvalidOperationException()
    {
        var req = new StartToolInstallRequest("python", Confirmed: true, ApprovalToken: null);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _assistant.StartInstallAsync(req));
        Assert.Contains("token de aprovação", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StartInstall_WithExpiredOrInvalidApprovalToken_ThrowsInvalidOperationException()
    {
        var req = new StartToolInstallRequest("python", Confirmed: true, ApprovalToken: "forged-token-abc");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _assistant.StartInstallAsync(req));
        Assert.Contains("token de aprovação", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StartInstall_WithMismatchedApprovalToken_ThrowsInvalidOperationException()
    {
        var nodePlan = await _assistant.GetInstallPlanAsync("node");
        var req = new StartToolInstallRequest("python", Confirmed: true, ApprovalToken: nodePlan.ApprovalToken);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _assistant.StartInstallAsync(req));
        Assert.Contains("token de aprovação", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StartInstall_WithValidTokenAndConfirmation_StartsJobSuccessfully()
    {
        var plan = await _assistant.GetInstallPlanAsync("python");
        var req = new StartToolInstallRequest("python", Confirmed: true, ApprovalToken: plan.ApprovalToken);

        var jobId = await _assistant.StartInstallAsync(req);
        Assert.NotEqual(Guid.Empty, jobId);

        var progress = _assistant.GetInstallProgress(jobId);
        Assert.NotNull(progress);
        Assert.Equal("python", progress.ToolKey);
    }

    [Fact]
    public async Task StartInstall_ConcurrentInstallationForSameTool_ThrowsInvalidOperationException()
    {
        var plan1 = await _assistant.GetInstallPlanAsync("python");
        var req1 = new StartToolInstallRequest("python", Confirmed: true, ApprovalToken: plan1.ApprovalToken);
        await _assistant.StartInstallAsync(req1);

        var plan2 = await _assistant.GetInstallPlanAsync("python");
        var req2 = new StartToolInstallRequest("python", Confirmed: true, ApprovalToken: plan2.ApprovalToken);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _assistant.StartInstallAsync(req2));
        Assert.Contains("Já existe uma instalação", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StartInstall_WithUnknownToolKey_ThrowsArgumentException()
    {
        var req = new StartToolInstallRequest("unknown-malicious-tool", Confirmed: true, ApprovalToken: "token");
        await Assert.ThrowsAsync<ArgumentException>(() => _assistant.StartInstallAsync(req));
    }

    [Fact]
    public async Task StartInstall_WithMaliciousCustomPathTraversal_ThrowsArgumentException()
    {
        var plan = await _assistant.GetInstallPlanAsync("python");
        var req = new StartToolInstallRequest(
            "python",
            Confirmed: true,
            ApprovalToken: plan.ApprovalToken,
            CustomInstallPath: @"..\..\..\Windows\System32"
        );

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _assistant.StartInstallAsync(req));
        Assert.Contains("travessia", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StartModelDownload_WithoutConfirmation_ThrowsInvalidOperationException()
    {
        var req = new StartModelDownloadRequest("large-v3", Confirmed: false);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _assistant.StartDownloadModelAsync(req));
        Assert.Contains("confirmação explícita", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StartModelDownload_WithInvalidModelName_ThrowsArgumentException()
    {
        var req = new StartModelDownloadRequest("arbitrary-exec-payload", Confirmed: true);
        await Assert.ThrowsAsync<ArgumentException>(() => _assistant.StartDownloadModelAsync(req));
    }

    [Fact]
    public async Task StartModelDownload_ConcurrentDownload_ThrowsInvalidOperationException()
    {
        var req1 = new StartModelDownloadRequest("large-v3", Confirmed: true);
        await _assistant.StartDownloadModelAsync(req1);

        try
        {
            var req2 = new StartModelDownloadRequest("large-v3", Confirmed: true);
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _assistant.StartDownloadModelAsync(req2));
            Assert.Contains("Já existe um download", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            _modelDownloader.DownloadGate.TrySetResult(true);
        }
    }

    [Fact]
    public async Task SettingsService_SetToolPath_RejectsPathTraversalAndControlChars()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _settingsService.SetToolPathAsync("python", @"C:\Windows\..\Malicious\python.exe"));
        await Assert.ThrowsAsync<ArgumentException>(() => _settingsService.SetToolPathAsync("python", "relative/path/python.exe"));
        await Assert.ThrowsAsync<ArgumentException>(() => _settingsService.SetToolPathAsync("python", "C:\\test\n\\python.exe"));
    }

    [Fact]
    public async Task SettingsService_SetToolPath_RejectsInvalidBinaryNames()
    {
        var tempFile = Path.Combine(_tempDir, "malicious.cmd");
        File.WriteAllText(tempFile, "@echo evil");

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _settingsService.SetToolPathAsync("python", tempFile));
        Assert.Contains("não é um executável Python", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HttpModelDownloader_RejectsNonHttpsUrls()
    {
        var downloader = new HttpModelDownloader();
        var dest = Path.Combine(_tempDir, "model.pt");

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            downloader.DownloadFileAsync("http://insecure-site.com/model.pt", dest, new string('a', 64))
        );
        Assert.Contains("HTTPS", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HttpModelDownloader_RejectsPathTraversalInDestination()
    {
        var downloader = new HttpModelDownloader();
        var dest = Path.Combine(_tempDir, @"..\..\System32\model.pt");

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            downloader.DownloadFileAsync("https://huggingface.co/model.pt", dest, new string('a', 64))
        );
        Assert.Contains("travessia", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HttpModelDownloader_RejectsInvalidSha256()
    {
        var downloader = new HttpModelDownloader();
        var dest = Path.Combine(_tempDir, "model.pt");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            downloader.DownloadFileAsync("https://huggingface.co/model.pt", dest, "short-hash")
        );
        await Assert.ThrowsAsync<ArgumentException>(() =>
            downloader.DownloadFileAsync("https://huggingface.co/model.pt", dest, new string('Z', 64))
        );
    }

    private sealed class FakeProcessRunner : IProcessRunner
    {
        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken ct = default)
        {
            return Task.FromResult(new ProcessRunResult(
                Status: ProcessRunStatus.Completed,
                ExitCode: 0,
                StandardOutput: "v1.0.0",
                StandardError: ""
            ));
        }
    }

    private sealed class FakeModelDownloader : IModelDownloader
    {
        public TaskCompletionSource<bool> DownloadGate { get; set; } = new();

        public async Task DownloadFileAsync(
            string url,
            string destinationFilePath,
            string expectedSha256,
            IProgress<DownloadProgressUpdate>? progress = null,
            CancellationToken ct = default)
        {
            progress?.Report(new DownloadProgressUpdate(100, 100, 100));
            File.WriteAllText(destinationFilePath, "fake-model-content");
            await Task.WhenAny(DownloadGate.Task, Task.Delay(2000, ct));
        }
    }

    [Fact]
    public async Task StartInstall_WithoutNativeConfirmation_ThrowsOperationCanceledException()
    {
        _nativeAuthorizer.AllowInstall = false;
        var plan = await _assistant.GetInstallPlanAsync("python");
        var req = new StartToolInstallRequest("python", Confirmed: true, ApprovalToken: plan.ApprovalToken);

        var ex = await Assert.ThrowsAsync<OperationCanceledException>(() => _assistant.StartInstallAsync(req));
        Assert.Contains("cancelada ou rejeitada", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StartInstall_TokenObtainedByFrontend_WithoutNativeConfirmation_IsInsufficient()
    {
        // Demonstra que mesmo com um token de aprovação válido obtido pelo frontend e confirmed = true,
        // a ausência de confirmação humana no host nativo inviabiliza a instalação.
        _nativeAuthorizer.AllowInstall = false;
        var plan = await _assistant.GetInstallPlanAsync("node");
        Assert.NotNull(plan.ApprovalToken);

        var req = new StartToolInstallRequest("node", Confirmed: true, ApprovalToken: plan.ApprovalToken);
        await Assert.ThrowsAsync<OperationCanceledException>(() => _assistant.StartInstallAsync(req));
    }

    [Fact]
    public async Task StartModelDownload_WithoutNativeConfirmation_ThrowsOperationCanceledException()
    {
        _nativeAuthorizer.AllowDownload = false;
        var req = new StartModelDownloadRequest("large-v3", Confirmed: true);

        var ex = await Assert.ThrowsAsync<OperationCanceledException>(() => _assistant.StartDownloadModelAsync(req));
        Assert.Contains("cancelado ou rejeitado", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StartModelDownload_WithNativeConfirmation_StartsJobSuccessfully()
    {
        _nativeAuthorizer.AllowDownload = true;
        var req = new StartModelDownloadRequest("large-v3", Confirmed: true);

        var jobId = await _assistant.StartDownloadModelAsync(req);
        Assert.NotEqual(Guid.Empty, jobId);
        Assert.NotNull(_assistant.GetModelDownloadProgress(jobId));
        Assert.NotNull(_nativeAuthorizer.LastDownloadRequest);
        Assert.Equal("large-v3", _nativeAuthorizer.LastDownloadRequest.ModelName);
    }

    [Fact]
    public void WebView2_UntrustedOrigin_HasNoAccessToPrivilegedIpc()
    {
        // Somente https://travelcut.studio ou localhost:5173 (quando dev habilitado) são autorizados
        Assert.True(IpcOriginPolicy.IsTrustedOrigin("https://travelcut.studio", allowDevServer: false));
        Assert.True(IpcOriginPolicy.IsTrustedOrigin("https://travelcut.studio", allowDevServer: true));
        Assert.True(IpcOriginPolicy.IsTrustedOrigin("http://localhost:5173", allowDevServer: true));
        Assert.False(IpcOriginPolicy.IsTrustedOrigin("http://localhost:5173", allowDevServer: false));

        // Origens arbitrárias, portas não padrão ou esquemas inseguros são rejeitados
        Assert.False(IpcOriginPolicy.IsTrustedOrigin("https://evil-attacker.com", allowDevServer: true));
        Assert.False(IpcOriginPolicy.IsTrustedOrigin("https://travelcut.studio:8080", allowDevServer: true));
        Assert.False(IpcOriginPolicy.IsTrustedOrigin("http://localhost:3000", allowDevServer: true));
        Assert.False(IpcOriginPolicy.IsTrustedOrigin("javascript:alert(1)", allowDevServer: true));
        Assert.False(IpcOriginPolicy.IsTrustedOrigin(null, allowDevServer: true));
        Assert.False(IpcOriginPolicy.IsTrustedOrigin("   ", allowDevServer: true));
    }

    [Fact]
    public async Task SettingsService_RejectsBatAndCmdScripts()
    {
        // Scripts .bat e .cmd são rejeitados por risco de injeção e incompatibilidade com UseShellExecute = false
        await Assert.ThrowsAsync<ArgumentException>(() => _settingsService.SetToolPathAsync("python", @"C:	oolspython.cmd"));
        await Assert.ThrowsAsync<ArgumentException>(() => _settingsService.SetToolPathAsync("python", @"C:	oolspython.bat"));
        await Assert.ThrowsAsync<ArgumentException>(() => _settingsService.SetToolPathAsync("hyperframes", @"C:	oolshyperframes.cmd"));
        await Assert.ThrowsAsync<ArgumentException>(() => _settingsService.SetToolPathAsync("ffmpeg", @"C:	oolsfmpeg.bat"));
    }

    public sealed class FakeNativeInstallationAuthorizer : INativeInstallationAuthorizer
    {
        public bool AllowInstall { get; set; } = true;
        public bool AllowDownload { get; set; } = true;
        public NativeInstallationRequest? LastInstallRequest { get; private set; }
        public NativeDownloadRequest? LastDownloadRequest { get; private set; }

        public Task<bool> RequestInstallationAuthorizationAsync(NativeInstallationRequest request, CancellationToken ct = default)
        {
            LastInstallRequest = request;
            return Task.FromResult(AllowInstall);
        }

        public Task<bool> RequestDownloadAuthorizationAsync(NativeDownloadRequest request, CancellationToken ct = default)
        {
            LastDownloadRequest = request;
            return Task.FromResult(AllowDownload);
        }
    }

}
