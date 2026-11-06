using Microsoft.Extensions.Logging.Abstractions;
using TravelCut.Application.Audio;
using TravelCut.Application.Ports;
using TravelCut.Contracts.Dtos;
using TravelCut.Infrastructure.Settings;
using TravelCut.Infrastructure.Tooling;
using TravelCut.UnitTests.Audio;
using Xunit;

namespace TravelCut.UnitTests.Infrastructure;

public class EnvironmentAssistantServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly TravelCutSettingsService _settingsService;
    private readonly FakeWhisperService _whisperService;
    private readonly FakeProcessRunner _processRunner;
    private readonly FakeModelDownloader _modelDownloader;
    private readonly EnvironmentAssistantService _service;

    public EnvironmentAssistantServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "tc-env-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        var settingsPath = Path.Combine(_tempDir, "settings.json");

        _settingsService = new TravelCutSettingsService(NullLogger<TravelCutSettingsService>.Instance, settingsPath);
        _whisperService = new FakeWhisperService();
        _processRunner = new FakeProcessRunner();
        _modelDownloader = new FakeModelDownloader();

        _service = new EnvironmentAssistantService(
            _processRunner,
            _whisperService,
            _settingsService,
            _modelDownloader,
            NullLogger<EnvironmentAssistantService>.Instance,
            TimeProvider.System,
            AutoApproveNativeInstallationAuthorizer.Instance
        );
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, true);
        }
        catch { }
    }

    [Fact]
    public async Task DiagnoseAsync_ReturnsAllFiveToolsAndSystemInfo()
    {
        var result = await _service.DiagnoseAsync();

        Assert.NotNull(result);
        Assert.NotNull(result.SystemInfo);
        Assert.Equal(5, result.Tools.Count);

        var toolKeys = result.Tools.Select(t => t.ToolKey).ToList();
        Assert.Contains("python", toolKeys);
        Assert.Contains("whisper", toolKeys);
        Assert.Contains("ffmpeg", toolKeys);
        Assert.Contains("node", toolKeys);
        Assert.Contains("hyperframes", toolKeys);
    }

    [Theory]
    [InlineData("python")]
    [InlineData("whisper")]
    [InlineData("ffmpeg")]
    [InlineData("node")]
    [InlineData("hyperframes")]
    public async Task GetInstallPlanAsync_ReturnsValidPlanWithSteps(string toolKey)
    {
        var plan = await _service.GetInstallPlanAsync(toolKey);

        Assert.NotNull(plan);
        Assert.Equal(toolKey, plan.ToolKey);
        Assert.False(string.IsNullOrWhiteSpace(plan.Title));
        Assert.False(string.IsNullOrWhiteSpace(plan.Description));
        Assert.NotEmpty(plan.Steps);
    }

    [Fact]
    public async Task AutoLocateToolAsync_ReturnsValidCandidateList()
    {
        var response = await _service.AutoLocateToolAsync("python");

        Assert.NotNull(response);
        Assert.Equal("python", response.ToolKey);
        Assert.NotNull(response.Candidates);
    }

    [Fact]
    public async Task ConfigureToolPathAsync_SavesInSettingsAndReflectsInDiagnostics()
    {
        var dummyPath = Path.Combine(_tempDir, "custom-ffmpeg.exe");
        File.WriteAllText(dummyPath, "dummy");

        var configured = await _service.ConfigureToolPathAsync("ffmpeg", dummyPath);
        Assert.True(configured);

        var settings = await _settingsService.GetSettingsAsync();
        Assert.Equal(dummyPath, settings.FfmpegPath);
    }

    [Fact]
    public async Task TestToolAsync_ReturnsValidResponseWithoutThrowing()
    {
        var testResult = await _service.TestToolAsync("python");

        Assert.NotNull(testResult);
        Assert.Equal("python", testResult.ToolKey);
        Assert.NotNull(testResult.Message);
        Assert.NotNull(testResult.OutputLogs);
    }

    [Fact]
    public async Task StartInstallAsync_CreatesJobAndTracksProgress()
    {
        var plan = await _service.GetInstallPlanAsync("python");
        var req = new StartToolInstallRequest(
            ToolKey: "python",
            Confirmed: true,
            ApprovalToken: plan.ApprovalToken,
            CustomInstallPath: null,
            PreferCuda: false
        );

        var jobId = await _service.StartInstallAsync(req);
        Assert.NotEqual(Guid.Empty, jobId);

        var progress = _service.GetInstallProgress(jobId);
        Assert.NotNull(progress);
        Assert.Equal("python", progress.ToolKey);
        Assert.NotNull(progress.Stage);
    }

    [Fact]
    public async Task StartDownloadModelAsync_CreatesJobAndAllowsCancellation()
    {
        var req = new StartModelDownloadRequest(
            ModelName: "large-v3",
            Confirmed: true
        );

        var jobId = await _service.StartDownloadModelAsync(req);
        Assert.NotEqual(Guid.Empty, jobId);

        var progress = _service.GetModelDownloadProgress(jobId);
        Assert.NotNull(progress);
        Assert.Equal("whisper", progress.ToolKey);

        var canceled = _service.CancelModelDownload(jobId);
        Assert.True(canceled);
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
        public async Task DownloadFileAsync(
            string url,
            string destinationFilePath,
            string expectedSha256,
            IProgress<DownloadProgressUpdate>? progress = null,
            CancellationToken ct = default)
        {
            progress?.Report(new DownloadProgressUpdate(100, 100, 100));
            await Task.Delay(500, ct).ConfigureAwait(false);
            File.WriteAllText(destinationFilePath, "fake-model-content");
        }
    }
}
