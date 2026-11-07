using Microsoft.Extensions.Logging.Abstractions;
using TravelCut.Application.Ports;
using TravelCut.Contracts.Dtos;
using TravelCut.Infrastructure.Settings;
using TravelCut.Infrastructure.Tooling;
using TravelCut.UnitTests.Audio;
using Xunit;

namespace TravelCut.UnitTests.Infrastructure;

// F13.2: telemetria do download do modelo. O downloader é roteirizado (nada é baixado nem gravado em disco).
public class ModelDownloadProgressTests : IDisposable
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    private readonly string _tempDir;
    private readonly ScriptedDownloader _downloader = new();
    private readonly EnvironmentAssistantService _service;

    public ModelDownloadProgressTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "tc-model-dl-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        var tempCacheDir = Path.Combine(_tempDir, "whisper-cache");
        Directory.CreateDirectory(tempCacheDir);
        _service = new EnvironmentAssistantService(
            new NullProcessRunner(),
            new FakeWhisperService(),
            new TravelCutSettingsService(NullLogger<TravelCutSettingsService>.Instance, Path.Combine(_tempDir, "settings.json")),
            _downloader,
            NullLogger<EnvironmentAssistantService>.Instance,
            TimeProvider.System,
            AutoApproveNativeInstallationAuthorizer.Instance,
            tempCacheDir);
    }

    public void Dispose()
    {
        _downloader.Release.TrySetResult(true);
        try { Directory.Delete(_tempDir, true); } catch (IOException) { }
    }

    private Task<Guid> StartAsync() => _service.StartDownloadModelAsync(new StartModelDownloadRequest("large-v3", Confirmed: true));

    private async Task<ToolInstallProgressDto> WaitForAsync(Guid jobId, Func<ToolInstallProgressDto, bool> condition)
    {
        var deadline = DateTime.UtcNow + Wait;
        while (DateTime.UtcNow < deadline)
        {
            var dto = _service.GetModelDownloadProgress(jobId);
            if (dto is not null && condition(dto)) return dto;
            await Task.Delay(10);
        }
        throw new TimeoutException("Estado esperado do download não foi alcançado.");
    }

    [Fact]
    public async Task Progress_ExposesRealBytesAndPercent()
    {
        _downloader.Script = p => p.Report(new DownloadProgressUpdate(500, 2000, 25));
        var jobId = await StartAsync();

        var dto = await WaitForAsync(jobId, d => d.BytesDownloaded == 500);

        Assert.Equal(2000, dto.TotalBytes);
        Assert.Equal(25, dto.ProgressPercent);
        Assert.Equal("Baixando Modelo", dto.Stage);
        Assert.False(dto.IsCompleted);
    }

    [Fact]
    public async Task Progress_UnknownTotal_KeepsPercentNull_AndDoesNotInventTotal()
    {
        _downloader.Script = p => p.Report(new DownloadProgressUpdate(1_048_576, null, null));
        var jobId = await StartAsync();

        var dto = await WaitForAsync(jobId, d => d.BytesDownloaded == 1_048_576);

        Assert.Null(dto.TotalBytes);
        Assert.Null(dto.ProgressPercent);
        Assert.Contains("não informado", dto.CurrentMessage);
    }

    [Fact]
    public async Task Progress_TransferFinished_ShowsShaVerificationStage()
    {
        _downloader.Script = p => p.Report(new DownloadProgressUpdate(2000, 2000, 100));
        var jobId = await StartAsync();

        // O mesmo estágio também aparece ao verificar um modelo já presente em disco (sem bytes): espera o fim da transferência.
        var dto = await WaitForAsync(jobId, d => d.Stage == "Verificando SHA-256" && d.BytesDownloaded == 2000);

        Assert.Equal(99, dto.ProgressPercent);
        Assert.False(dto.IsCompleted);
    }

    [Fact]
    public async Task Completed_IsSuccess_AndLateReportDoesNotRegress()
    {
        IProgress<DownloadProgressUpdate>? captured = null;
        _downloader.Script = p => { captured = p; p.Report(new DownloadProgressUpdate(1000, 2000, 50)); };
        var jobId = await StartAsync();
        await WaitForAsync(jobId, d => d.BytesDownloaded == 1000);

        _downloader.Release.TrySetResult(true);
        var done = await WaitForAsync(jobId, d => d.IsCompleted);

        Assert.True(done.IsSuccess);
        Assert.False(done.IsFailed);
        Assert.False(done.IsCanceled);
        Assert.Equal(100, done.ProgressPercent);
        Assert.Equal("Concluído", done.Stage);

        captured!.Report(new DownloadProgressUpdate(1500, 2000, 75));

        var after = _service.GetModelDownloadProgress(jobId)!;
        Assert.Equal(100, after.ProgressPercent);
        Assert.Equal("Concluído", after.Stage);
    }

    [Fact]
    public async Task Cancel_IsReportedAsCanceled_NotAsFailure()
    {
        _downloader.Script = p => p.Report(new DownloadProgressUpdate(10, 2000, 0.5));
        var jobId = await StartAsync();
        await WaitForAsync(jobId, d => d.BytesDownloaded == 10);

        Assert.True(_service.CancelModelDownload(jobId));
        var done = await WaitForAsync(jobId, d => d.IsCompleted);

        Assert.True(done.IsCanceled);
        Assert.False(done.IsFailed);
        Assert.False(done.IsSuccess);
        Assert.Equal("Cancelado", done.Stage);
    }

    [Fact]
    public async Task Failure_KeepsErrorMessage_AndIsNotCanceled()
    {
        _downloader.Failure = new InvalidOperationException("Hash SHA-256 do arquivo baixado é inválido.");
        var jobId = await StartAsync();

        var done = await WaitForAsync(jobId, d => d.IsCompleted);

        Assert.True(done.IsFailed);
        Assert.False(done.IsCanceled);
        Assert.Equal("Erro", done.Stage);
        Assert.Contains("SHA-256", done.ErrorMessage);
    }

    private sealed class NullProcessRunner : IProcessRunner
    {
        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken ct = default) =>
            Task.FromResult(new ProcessRunResult(ProcessRunStatus.Completed, 0, "", ""));
    }

    /// <summary>Executa o roteiro e fica aberto até Release ou cancelamento. Não toca no arquivo de destino.</summary>
    private sealed class ScriptedDownloader : IModelDownloader
    {
        public Action<IProgress<DownloadProgressUpdate>>? Script { get; set; }
        public Exception? Failure { get; set; }
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task DownloadFileAsync(string url, string destinationFilePath, string expectedSha256,
            IProgress<DownloadProgressUpdate>? progress = null, CancellationToken ct = default)
        {
            if (Failure is not null) throw Failure;
            if (progress is not null) Script?.Invoke(progress);
            await Release.Task.WaitAsync(ct).ConfigureAwait(false);
        }
    }
}
