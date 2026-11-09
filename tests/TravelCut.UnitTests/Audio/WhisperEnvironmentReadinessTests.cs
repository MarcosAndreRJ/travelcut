using Microsoft.Extensions.Logging.Abstractions;
using TravelCut.Application.Audio;
using TravelCut.Contracts.Dtos;
using TravelCut.Infrastructure.Audio;
using Xunit;

namespace TravelCut.UnitTests.Audio;

/// <summary>F14.8 P04/P05: disponibilidade real do Whisper (modelo + ffmpeg), device/cpuFallback e TTL do cache de ambiente.</summary>
public sealed class WhisperEnvironmentReadinessTests
{
    private const string PythonPath = @"C:\Tools\Python 3.11\python.exe";

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }

    private static string Lower(bool b) => b ? "true" : "false";

    private static string ProbeJson(bool installed, bool modelCached, bool ffmpeg, bool cuda)
    {
        string result =
            "\"python\":\"3.11.9\",\"whisperInstalled\":" + Lower(installed) + ",\"whisperVersion\":\"20240930\"," +
            "\"cuda\":" + Lower(cuda) + ",\"device\":\"" + (cuda ? "cuda" : "cpu") + "\",\"ffmpegOnPath\":" + Lower(ffmpeg) + "," +
            "\"defaultModel\":\"large-v3\",\"modelCached\":" + Lower(modelCached);
        return "{\"protocol\":\"travelcut.whisper\",\"version\":1,\"requestId\":\"__REQ__\",\"ok\":true,\"result\":{" + result + "}}";
    }

    private static string CreateScript()
    {
        var root = Path.Combine(Path.GetTempPath(), "TravelCut F148 " + Guid.NewGuid().ToString("N"), "workers");
        Directory.CreateDirectory(root);
        var script = Path.Combine(root, "whisper_worker.py");
        File.WriteAllText(script, "# stub\n");
        return script;
    }

    private static (WhisperTranscriptionService Service, FakeProcessRunner Runner) Create(Func<string> probeJson, TimeProvider clock)
    {
        var script = CreateScript();
        var runner = new FakeProcessRunner(r =>
        {
            if (FakeProcessRunner.IsPythonCheck(r)) return FakeProcessRunner.Completed(0, "TRAVELCUT_PY=3.11.9\n");
            if (FakeProcessRunner.IsCommand(r, "probe"))
                return FakeProcessRunner.Completed(0, probeJson().Replace("__REQ__", FakeProcessRunner.RequestIdFrom(r)));
            return FakeProcessRunner.NotStarted();
        });
        var service = new WhisperTranscriptionService(NullLogger<WhisperTranscriptionService>.Instance, runner,
            new WhisperServiceOptions(PythonPath: PythonPath, WorkerScriptPath: script, Clock: clock));
        return (service, runner);
    }

    private static int ProbeCount(FakeProcessRunner runner) => runner.Requests.Count(r => FakeProcessRunner.IsCommand(r, "probe"));

    [Fact]
    public async Task Probe_ExposesModelCachedAndFfmpegOnPath()
    {
        var (service, _) = Create(() => ProbeJson(true, modelCached: false, ffmpeg: true, cuda: false), new ManualClock());

        var probe = await service.ProbeEnvironmentAsync();

        Assert.True(probe.HasWhisper);
        Assert.False(probe.ModelCached);
        Assert.True(probe.FfmpegOnPath);
    }

    [Fact]
    public async Task IncompleteEnvironment_ModelMissing_IsReprobedAfterTtl_AndPicksUpInstall()
    {
        var clock = new ManualClock();
        bool modelCached = false;
        var (service, runner) = Create(() => ProbeJson(true, modelCached, ffmpeg: true, cuda: true), clock);

        Assert.False((await service.ProbeEnvironmentAsync()).ModelCached);
        await service.ProbeEnvironmentAsync();
        Assert.Equal(1, ProbeCount(runner)); // dentro do TTL: cache

        modelCached = true; // usuário baixou o modelo
        clock.Advance(TimeSpan.FromSeconds(59));
        Assert.False((await service.ProbeEnvironmentAsync()).ModelCached);
        Assert.Equal(1, ProbeCount(runner));

        clock.Advance(TimeSpan.FromSeconds(2)); // > 60 s
        Assert.True((await service.ProbeEnvironmentAsync()).ModelCached);
        Assert.Equal(2, ProbeCount(runner));
    }

    [Fact]
    public async Task WhisperNotInstalled_IsReprobedAfterTtl()
    {
        var clock = new ManualClock();
        bool installed = false;
        var (service, runner) = Create(() => ProbeJson(installed, true, true, false), clock);

        Assert.False((await service.ProbeEnvironmentAsync()).HasWhisper);
        installed = true;
        clock.Advance(TimeSpan.FromSeconds(61));

        Assert.True((await service.ProbeEnvironmentAsync()).HasWhisper);
        Assert.Equal(2, ProbeCount(runner));
    }

    [Fact]
    public async Task ReadyEnvironment_StaysCached()
    {
        var clock = new ManualClock();
        var (service, runner) = Create(() => ProbeJson(true, true, true, true), clock);

        await service.ProbeEnvironmentAsync();
        clock.Advance(TimeSpan.FromMinutes(30));
        await service.ProbeEnvironmentAsync();

        Assert.Equal(1, ProbeCount(runner));
    }

    [Fact]
    public async Task InvalidateEnvironment_ForcesNewProbeAndBumpsGeneration()
    {
        var (service, runner) = Create(() => ProbeJson(true, true, true, true), new ManualClock());
        await service.ProbeEnvironmentAsync();
        long before = service.EnvironmentGeneration;

        service.InvalidateEnvironment();
        await service.ProbeEnvironmentAsync();

        Assert.Equal(before + 1, service.EnvironmentGeneration);
        Assert.Equal(2, ProbeCount(runner));
    }

    // ---- Manager: WhisperAvailable = python && whisper && modelCached && ffmpegOnPath ----

    private static async Task<AnalysisEnvironmentDto> EnvOf(FakeWhisperService whisper)
    {
        using var h = new PipelineHarness(whisper, new FakeSilenceService { FfmpegAvailable = true });
        return await h.Manager.GetEnvironmentAsync();
    }

    [Fact]
    public async Task Manager_ModelNotCached_IsUnavailableWithReason()
    {
        var env = await EnvOf(new FakeWhisperService { Available = true, ModelCached = false, FfmpegOnPath = true, Cuda = true, Device = "cuda" });

        Assert.False(env.WhisperAvailable);
        Assert.False(env.ModelCached);
        Assert.Contains("cache local", env.WhisperUnavailableReason);
        Assert.False(env.CpuFallback);
    }

    [Fact]
    public async Task Manager_FfmpegNotOnWorkerPath_IsUnavailable()
    {
        var env = await EnvOf(new FakeWhisperService { Available = true, ModelCached = true, FfmpegOnPath = false, Cuda = true, Device = "cuda" });

        Assert.False(env.WhisperAvailable);
        Assert.Contains("ffmpeg", env.WhisperUnavailableReason);
    }

    [Fact]
    public async Task Manager_EverythingPresentWithCuda_IsAvailableOnCudaWithoutCpuFallback()
    {
        var env = await EnvOf(new FakeWhisperService { Available = true, ModelCached = true, FfmpegOnPath = true, Cuda = true, Device = "cuda" });

        Assert.True(env.WhisperAvailable);
        Assert.Equal("cuda", env.Device);
        Assert.False(env.CpuFallback);
        Assert.Null(env.WhisperUnavailableReason);
    }

    [Fact]
    public async Task Manager_NoCuda_DoesNotBlockButFlagsCpuFallback()
    {
        var env = await EnvOf(new FakeWhisperService { Available = true, ModelCached = true, FfmpegOnPath = true, Cuda = false, Device = "cpu" });

        Assert.True(env.WhisperAvailable);
        Assert.True(env.CpuFallback);
        Assert.Equal("cpu", env.Device);
        Assert.Contains(env.Reasons, r => r.Contains("CPU"));
    }

    [Fact]
    public async Task Manager_UnknownModelAndFfmpeg_DoNotBlock()
    {
        var env = await EnvOf(new FakeWhisperService { Available = true, ModelCached = null, FfmpegOnPath = null });

        Assert.True(env.WhisperAvailable);
    }

    [Fact]
    public async Task Manager_ReprobesAfterWhisperEnvironmentIsInvalidated()
    {
        var whisper = new FakeWhisperService { Available = true, ModelCached = false, FfmpegOnPath = true };
        using var h = new PipelineHarness(whisper, new FakeSilenceService { FfmpegAvailable = true });

        Assert.False((await h.Manager.GetEnvironmentAsync()).WhisperAvailable);
        Assert.NotNull(h.Manager.PeekEnvironment());

        whisper.ModelCached = true;
        whisper.InvalidateEnvironment(); // como o assistente após instalar/baixar o modelo

        Assert.Null(h.Manager.PeekEnvironment());
        Assert.True((await h.Manager.GetEnvironmentAsync()).WhisperAvailable);
        Assert.Equal(2, whisper.ProbeCalls);
    }
}
