using TravelCut.Application.Ports;
using TravelCut.Application.UseCases;
using TravelCut.Contracts.Dtos;
using TravelCut.Domain.Entities;
using TravelCut.Domain.Enums;
using TravelCut.Infrastructure.Media.Preview;
using Xunit;

namespace TravelCut.UnitTests.Preview;

public class ProxyJobTests
{
    [Fact]
    public void Arguments_AreExactAndNeverOverwrite()
    {
        var args = ProxyFfmpegArguments.Build(@"C:\proj\1-Bruto\a.mp4", @"C:\proj\3-Trabalho\cache\proxies\x.tcpart.mp4");

        Assert.Equal(
            new[]
            {
                "-hide_banner", "-nostats", "-loglevel", "error",
                "-n",
                "-i", @"C:\proj\1-Bruto\a.mp4",
                "-c:v", "libx264", "-preset", "veryfast", "-crf", "23",
                "-vf", "scale=-2:720",
                "-profile:v", "main", "-level", "3.1",
                "-pix_fmt", "yuv420p",
                "-fps_mode", "passthrough",
                "-c:a", "aac", "-ac", "2", "-b:a", "128k",
                "-movflags", "+faststart",
                @"C:\proj\3-Trabalho\cache\proxies\x.tcpart.mp4"
            },
            args);

        Assert.DoesNotContain("-y", args);
        Assert.DoesNotContain("-r", args);
        Assert.EndsWith(".tcpart.mp4", args[^1]);
    }

    [Fact]
    public async Task Job_WritesPartialThenRenamesWithoutTouchingSource()
    {
        using var h = new Harness();
        h.Ffmpeg.Path = @"C:\ffmpeg\bin\ffmpeg.exe";
        var content = new byte[] { 9, 8, 7, 6, 5 };
        var media = h.AddMedia("1-Bruto/Viagem Açores/cena 1.mp4", fingerprint: "fp-job", content: content);
        string source = h.FullPath(media.RelativePath);
        var before = File.GetLastWriteTimeUtc(source);

        var outcome = h.Jobs.Start(h.Project.ProjectId, h.Root, new[] { Plan(h, media) });

        Assert.False(outcome.Conflict);
        var finished = await WaitTerminalAsync(h.Jobs, outcome.Job!.JobId);

        Assert.Equal("completed", finished.State);
        Assert.Equal(1, finished.Processed);
        Assert.Equal(1, finished.Total);
        Assert.Equal("ready", Assert.Single(finished.Items).State);

        // Argumentos: saída parcial com -n, sem -y.
        var call = h.Process.Calls[0]; Assert.True(h.Process.Calls.Count >= 2);
        Assert.Contains("-n", call.Arguments);
        Assert.DoesNotContain("-y", call.Arguments);
        Assert.EndsWith(".tcpart.mp4", call.Arguments[^1]);
        Assert.Equal(TimeSpan.FromMinutes(60), call.Timeout);

        // Rename concluído: final existe, parcial não.
        string finalPath = Path.Combine(h.ProxiesFolder, PreviewPaths.ProxyFileName(media.MediaId, "fp-job"));
        Assert.True(File.Exists(finalPath));
        Assert.False(File.Exists(call.Arguments[^1]));
        Assert.Empty(Directory.GetFiles(h.ProxiesFolder, "*.tcpart.mp4"));

        // Origem intacta.
        Assert.Equal(content, File.ReadAllBytes(source));
        Assert.Equal(before, File.GetLastWriteTimeUtc(source));
        Assert.Equal("ready", h.Jobs.GetLatestItem(h.Project.ProjectId, media.MediaId)!.State);
    }

    [Fact]
    public async Task FfmpegFailure_LeavesNoProxyAndMarksItemFailed()
    {
        using var h = new Harness();
        h.Ffmpeg.Path = @"C:\ffmpeg\bin\ffmpeg.exe";
        var media = h.AddMedia("1-Bruto/a.mp4", fingerprint: "fp-fail");
        h.Process.Handler = async (req, ct) =>
        {
            await File.WriteAllBytesAsync(req.Arguments[^1], new byte[] { 1 }, ct);
            return new ProcessRunResult(ProcessRunStatus.Completed, 1, string.Empty, "erro simulado");
        };

        var outcome = h.Jobs.Start(h.Project.ProjectId, h.Root, new[] { Plan(h, media) });
        var finished = await WaitTerminalAsync(h.Jobs, outcome.Job!.JobId);

        Assert.Equal("failed", finished.State);
        Assert.Equal("failed", Assert.Single(finished.Items).State);
        Assert.False(File.Exists(Path.Combine(h.ProxiesFolder, PreviewPaths.ProxyFileName(media.MediaId, "fp-fail"))));
        Assert.Empty(Directory.GetFiles(h.ProxiesFolder, "*.tcpart.mp4"));
    }

    [Fact]
    public async Task SecondStartWhileActive_IsProxyInProgress()
    {
        using var h = new Harness();
        h.Ffmpeg.Path = @"C:\ffmpeg\bin\ffmpeg.exe";
        var first = h.AddMedia("1-Bruto/a.mp4", fingerprint: "fp-1");
        var second = h.AddMedia("1-Bruto/b.mp4", fingerprint: "fp-2");

        var release = new TaskCompletionSource();
        var started = new TaskCompletionSource();
        h.Process.Handler = async (req, ct) =>
        {
            started.TrySetResult();
            await release.Task.WaitAsync(ct);
            if (req.Arguments[^1] == "-")
                return new ProcessRunResult(ProcessRunStatus.Completed, 0, string.Empty, string.Empty);
            var mock = new byte[1024]; mock[3] = 32; mock[4] = 0x66; mock[5] = 0x74; mock[6] = 0x79; mock[7] = 0x70;
            await File.WriteAllBytesAsync(req.Arguments[^1], mock, ct);
            return new ProcessRunResult(ProcessRunStatus.Completed, 0, string.Empty, string.Empty);
        };

        var job1 = h.Jobs.Start(h.Project.ProjectId, h.Root, new[] { Plan(h, first) });
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var conflict = h.Jobs.Start(h.Project.ProjectId, h.Root, new[] { Plan(h, second) });

        Assert.True(conflict.Conflict);
        Assert.NotNull(conflict.Job);

        release.SetResult();
        var finished = await WaitTerminalAsync(h.Jobs, job1.Job!.JobId);
        Assert.Equal("completed", finished.State);

        // Após concluir, um novo job do mesmo projeto já é aceito.
        var next = h.Jobs.Start(h.Project.ProjectId, h.Root, new[] { Plan(h, second) });
        Assert.False(next.Conflict);
        await WaitTerminalAsync(h.Jobs, next.Job!.JobId);
    }

    [Fact]
    public void WithoutFfmpeg_JobIsFfmpegUnavailableImmediately()
    {
        using var h = new Harness();
        var media = h.AddMedia("1-Bruto/a.mp4", fingerprint: "fp-noff");

        var outcome = h.Jobs.Start(h.Project.ProjectId, h.Root, new[] { Plan(h, media) });

        Assert.False(outcome.Conflict);
        Assert.Equal("ffmpeg_unavailable", outcome.Job!.State);
        Assert.Equal("skipped", Assert.Single(outcome.Job.Items).State);
        Assert.Empty(h.Process.Calls);
        Assert.False(Directory.Exists(h.ProxiesFolder) && Directory.GetFiles(h.ProxiesFolder).Length > 0);
    }

    [Fact]
    public void ExistingProxy_IsReadyWithoutProcess()
    {
        using var h = new Harness();
        h.Ffmpeg.Path = @"C:\ffmpeg\bin\ffmpeg.exe";
        var media = h.AddMedia("1-Bruto/a.mp4", fingerprint: "fp-existing");
        Directory.CreateDirectory(h.ProxiesFolder);
        var validMock = new byte[1024]; validMock[3] = 32; validMock[4] = 0x66; validMock[5] = 0x74; validMock[6] = 0x79; validMock[7] = 0x70; File.WriteAllBytes(Path.Combine(h.ProxiesFolder, PreviewPaths.ProxyFileName(media.MediaId, "fp-existing")), validMock);

        var outcome = h.Jobs.Start(h.Project.ProjectId, h.Root, new[] { Plan(h, media) });

        Assert.Equal("completed", outcome.Job!.State);
        Assert.Equal("ready", Assert.Single(outcome.Job.Items).State);
        Assert.Empty(h.Process.Calls);
    }

    [Fact]
    public void SkipReason_MarksItemSkipped()
    {
        using var h = new Harness();
        h.Ffmpeg.Path = @"C:\ffmpeg\bin\ffmpeg.exe";
        var id = Guid.NewGuid();
        var plan = new ProxyJobItemPlan(id, "x.wav", null, string.Empty, "Somente mídia de vídeo recebe proxy.");

        var outcome = h.Jobs.Start(h.Project.ProjectId, h.Root, new[] { plan });

        var item = Assert.Single(outcome.Job!.Items);
        Assert.Equal("skipped", item.State);
        Assert.Equal("Somente mídia de vídeo recebe proxy.", item.Message);
        Assert.Equal("completed", outcome.Job.State);
        Assert.Empty(h.Process.Calls);
    }

    [Fact]
    public async Task UseCase_RejectsEmptyAndOversizedRequests()
    {
        using var h = new Harness();

        var empty = await h.UseCases.StartProxyAsync(new StartProxyRequest(h.Project.ProjectId, new List<Guid>()));
        var tooMany = await h.UseCases.StartProxyAsync(new StartProxyRequest(
            h.Project.ProjectId,
            Enumerable.Range(0, PreviewPaths.MaxProxyMediaPerRequest + 1).Select(_ => Guid.NewGuid()).ToList()));

        Assert.Equal("INVALID_ARGUMENT", empty.ErrorCode);
        Assert.Equal("INVALID_ARGUMENT", tooMany.ErrorCode);
        Assert.Empty(h.Process.Calls);
    }

    [Fact]
    public async Task UseCase_ForeignOrMissingMediaIsSkippedNotProcessed()
    {
        using var h = new Harness();
        h.Ffmpeg.Path = @"C:\ffmpeg\bin\ffmpeg.exe";
        var missingId = Guid.NewGuid();

        var result = await h.UseCases.StartProxyAsync(new StartProxyRequest(h.Project.ProjectId, new List<Guid> { missingId }));

        Assert.True(result.Ok);
        var item = Assert.Single(result.Value!.Items);
        Assert.Equal("skipped", item.State);
        Assert.Empty(h.Process.Calls);
    }

    [Fact]
    public void UnknownJob_IsNotFound()
    {
        using var h = new Harness();

        var result = h.UseCases.GetProxyStatus(new ProxyStatusRequest(h.Project.ProjectId, Guid.NewGuid()));

        Assert.False(result.Ok);
        Assert.Equal("PROXY_JOB_NOT_FOUND", result.ErrorCode);
    }

    [Fact]
    public async Task Dispose_CancelsRunningEncodeAndDoesNotLeaveParts()
    {
        using var h = new Harness();
        h.Ffmpeg.Path = @"C:\ffmpeg\bin\ffmpeg.exe";
        var media = h.AddMedia("1-Bruto/a.mp4", fingerprint: "fp-dispose");
        var started = new TaskCompletionSource();
        h.Process.Handler = async (req, ct) =>
        {
            await File.WriteAllBytesAsync(req.Arguments[^1], new byte[] { 1 }, CancellationToken.None);
            started.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return new ProcessRunResult(ProcessRunStatus.Canceled, null, string.Empty, string.Empty);
        };

        var outcome = h.Jobs.Start(h.Project.ProjectId, h.Root, new[] { Plan(h, media) });
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        h.Jobs.Dispose();

        var job = h.Jobs.GetJob(outcome.Job!.JobId);
        Assert.Equal("canceled", job!.State);
        Assert.Empty(Directory.GetFiles(h.ProxiesFolder, "*.tcpart.mp4"));
        Assert.False(File.Exists(Path.Combine(h.ProxiesFolder, PreviewPaths.ProxyFileName(media.MediaId, "fp-dispose"))));
    }

    private static ProxyJobItemPlan Plan(Harness h, MediaAsset media) =>
        new(media.MediaId, Path.GetFileName(media.RelativePath), h.FullPath(media.RelativePath), media.Fingerprint, null);

    private static async Task<ProxyJobSnapshot> WaitTerminalAsync(ProxyJobService jobs, Guid jobId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            var snap = jobs.GetJob(jobId);
            if (snap != null && snap.State is "completed" or "completed_with_errors" or "failed" or "canceled" or "ffmpeg_unavailable")
                return snap;

            await Task.Delay(20);
        }

        throw new TimeoutException("Job de proxy não terminou no tempo esperado.");
    }
}
