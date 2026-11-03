using Microsoft.Extensions.Logging.Abstractions;
using TravelCut.Application.Ports;
using TravelCut.Application.UseCases;
using TravelCut.Contracts.Dtos;
using TravelCut.Contracts.Ipc;
using TravelCut.Domain.Enums;
using TravelCut.Infrastructure.Media.Preview;
using Xunit;

namespace TravelCut.UnitTests.Preview;

/// <summary>
/// Regressões dos achados BAIXOS da auditoria QA da F07 na prévia: cache de forma de onda que não pode gravar,
/// decodificação incompleta, media.proxy.status amarrado ao projeto e encerramento do serviço de proxy.
/// </summary>
public sealed class F07AuditPreviewRegressionTests
{
    private static short[] Samples(int count)
    {
        var samples = new short[count];
        for (int i = 0; i < count; i++) samples[i] = 16384;
        return samples;
    }

    /// <summary>Mídia de 1 s com áudio, fingerprint e arquivo presentes (janela 0–1 s = 8000 amostras a 8 kHz).</summary>
    private static Guid AddOneSecondAudioMedia(Harness h, string fingerprint)
    {
        var id = Guid.NewGuid();
        h.AddMedia("1-Bruto/voz.mp4", fingerprint: fingerprint, metadata: Harness.OkMetadata(id, "h264", 1), mediaId: id);
        h.Ffmpeg.Path = @"C:\ffmpeg\bin\ffmpeg.exe";
        return id;
    }

    private static WaveformRequest Request(Harness h, Guid mediaId) =>
        new(h.Project.ProjectId, mediaId, 0, 1, 16);

    /// <summary>Cache cuja gravação sempre falha com a exceção dada (leitura: miss).</summary>
    private sealed class FailingWaveformCache(Exception failure) : IWaveformCache
    {
        public Task<WaveformDto?> ReadAsync(string projectRoot, string cacheKey, CancellationToken ct = default) =>
            Task.FromResult<WaveformDto?>(null);

        public Task WriteAsync(string projectRoot, string cacheKey, WaveformDto result, CancellationToken ct = default) =>
            Task.FromException(failure);
    }

    // ------------------------------------------------------------------ cache de forma de onda

    [Theory]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(UnauthorizedAccessException))]
    public async Task Waveform_CacheWriteFails_ResponseIsStillOk(Type failureType)
    {
        using var h = new Harness();
        var id = AddOneSecondAudioMedia(h, "fp-cache-fail");
        h.Binary.Stdout = PcmBytes.S16(Samples(8000));
        var failure = (Exception)Activator.CreateInstance(failureType, "falha injetada")!;
        var useCases = new PreviewUseCases(
            h.Projects, h.Media, h.Ffmpeg, h.Binary, new FailingWaveformCache(failure), h.Jobs,
            NullLogger<PreviewUseCases>.Instance);

        var result = await useCases.GetWaveformAsync(Request(h, id));

        Assert.True(result.Ok, result.ErrorMessage);
        Assert.Equal("ok", result.Value!.Status);
        Assert.Equal(16, result.Value.Peaks.Count);
    }

    [Fact]
    public async Task Waveform_RealFileCacheUnwritable_ResponseIsStillOk()
    {
        using var h = new Harness();
        var id = AddOneSecondAudioMedia(h, "fp-cache-real");
        h.Binary.Stdout = PcmBytes.S16(Samples(8000));
        // Um ARQUIVO no lugar da pasta 3-Trabalho/cache faz CreateDirectory do cache falhar com IOException.
        File.WriteAllText(Path.Combine(h.Root, "3-Trabalho", "cache"), "bloqueio");

        var result = await h.UseCases.GetWaveformAsync(Request(h, id));

        Assert.True(result.Ok, result.ErrorMessage);
        Assert.Equal("ok", result.Value!.Status);
        Assert.Equal(16, result.Value.Peaks.Count);
    }

    [Fact]
    public async Task Waveform_IncompleteDecode_IsFailedWithReason_NotZeroBuckets()
    {
        using var h = new Harness();
        var id = AddOneSecondAudioMedia(h, "fp-incomplete");
        h.Binary.Stdout = PcmBytes.S16(Samples(4000)); // 50% das 8000 amostras esperadas

        var result = await h.UseCases.GetWaveformAsync(Request(h, id));

        Assert.True(result.Ok);
        Assert.Equal("failed", result.Value!.Status);
        Assert.Equal("áudio decodificado incompleto", result.Value.Reason);
        Assert.Empty(result.Value.Peaks);
    }

    [Theory]
    [InlineData(7200, "ok")]      // 90% exatos: aceito
    [InlineData(7199, "failed")]  // abaixo de 90%: falha honesta
    public async Task Waveform_DecodeBoundaryAtNinetyPercent(int decodedSamples, string expectedStatus)
    {
        using var h = new Harness();
        var id = AddOneSecondAudioMedia(h, $"fp-boundary-{decodedSamples}");
        h.Binary.Stdout = PcmBytes.S16(Samples(decodedSamples));

        var result = await h.UseCases.GetWaveformAsync(Request(h, id));

        Assert.Equal(expectedStatus, result.Value!.Status);
    }

    [Fact]
    public async Task Waveform_IncompleteDecode_IsNotCached()
    {
        using var h = new Harness();
        var id = AddOneSecondAudioMedia(h, "fp-incomplete-nocache");
        h.Binary.Stdout = PcmBytes.S16(Samples(4000));

        await h.UseCases.GetWaveformAsync(Request(h, id));

        var cacheFolder = Path.Combine(h.Root, "3-Trabalho", "cache", "waveforms");
        Assert.False(Directory.Exists(cacheFolder) && Directory.GetFiles(cacheFolder).Length > 0);
    }

    // ------------------------------------------------------------------ media.proxy.status { projectId, jobId }

    private static PreviewIpcHandler Handler(Harness h) =>
        new(h.UseCases, NullLogger<PreviewIpcHandler>.Instance);

    [Fact]
    public async Task ProxyStatus_WithoutProjectId_IsInvalidArgument()
    {
        using var h = new Harness();

        var response = await Handler(h).TryHandleAsync(new IpcRequest
        {
            RequestId = "p1",
            Type = PreviewIpcHandler.ProxyStatusType,
            Payload = new { jobId = Guid.NewGuid() },
        });

        Assert.NotNull(response);
        Assert.False(response!.Ok);
        Assert.Equal("INVALID_ARGUMENT", response.ErrorCode);
    }

    [Fact]
    public async Task ProxyStatus_WithProjectAndJob_ReturnsStatus()
    {
        using var h = new Harness();
        var outcome = h.Jobs.Start(h.Project.ProjectId, h.Root, Array.Empty<ProxyJobItemPlan>());

        var response = await Handler(h).TryHandleAsync(new IpcRequest
        {
            RequestId = "p2",
            Type = PreviewIpcHandler.ProxyStatusType,
            Payload = new { projectId = h.Project.ProjectId, jobId = outcome.Job!.JobId },
        });

        Assert.True(response!.Ok, response.ErrorMessage);
        var status = Assert.IsType<ProxyJobStatusDto>(response.Payload);
        Assert.Equal(outcome.Job.JobId, status.JobId);
    }

    [Fact]
    public async Task ProxyStatus_JobOfAnotherProject_IsNotFound_EvenWithValidJobId()
    {
        using var h = new Harness();
        var otherProject = Guid.NewGuid();
        var outcome = h.Jobs.Start(otherProject, h.Root, Array.Empty<ProxyJobItemPlan>());

        var wrongProject = h.UseCases.GetProxyStatus(new ProxyStatusRequest(h.Project.ProjectId, outcome.Job!.JobId));
        var rightProject = h.UseCases.GetProxyStatus(new ProxyStatusRequest(otherProject, outcome.Job.JobId));

        Assert.False(wrongProject.Ok);
        Assert.Equal("PROXY_JOB_NOT_FOUND", wrongProject.ErrorCode);
        Assert.True(rightProject.Ok, rightProject.ErrorMessage);
    }

    // ------------------------------------------------------------------ encerramento do serviço de proxy

    [Fact]
    public async Task ProxyJobService_Dispose_WithFailingEncode_DoesNotThrow()
    {
        using var h = new Harness();
        h.Ffmpeg.Path = @"C:\ffmpeg\bin\ffmpeg.exe";
        var media = h.AddMedia("1-Bruto/falha.mp4", fingerprint: "fp-dispose-fault");
        var encodeStarted = new TaskCompletionSource();
        h.Process.Handler = (req, ct) =>
        {
            encodeStarted.TrySetResult();
            return Task.FromException<ProcessRunResult>(new InvalidOperationException("falha injetada no encode"));
        };
        var outcome = h.Jobs.Start(h.Project.ProjectId, h.Root, new[]
        {
            new ProxyJobItemPlan(media.MediaId, "falha.mp4", h.FullPath(media.RelativePath), media.Fingerprint, null),
        });
        await encodeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var error = Record.Exception(() => h.Jobs.Dispose());

        Assert.Null(error);
        Assert.NotEqual("running", h.Jobs.GetJob(outcome.Job!.JobId)!.State);
    }
}
