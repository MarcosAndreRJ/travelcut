using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TravelCut.Application.Audio;
using TravelCut.Application.Ports;
using TravelCut.Contracts.Dtos;
using TravelCut.Infrastructure.Audio;
using Xunit;

namespace TravelCut.UnitTests.Audio;

/// <summary>F14.8 / I2b (P01): o pipeline só aciona o orquestrador pós-análise quando termina completed/completed_with_issues.</summary>
public class PipelinePostAnalysisTriggerTests : IDisposable
{
    private sealed class SpyOrchestrator : IPostAnalysisOrchestrator
    {
        public List<(Guid ProjectId, Guid JobId)> Calls { get; } = new();

        public Task ScheduleAsync(Guid projectId, Guid jobId)
        {
            lock (Calls) Calls.Add((projectId, jobId));
            return Task.CompletedTask;
        }

        public PostAnalysisStatusDto GetStatus(Guid projectId) =>
            new("completed", null, null, 2, 3, 1, 0, new List<string>(), 1, null, null);
    }

    private readonly List<PipelineHarness> _harnesses = new();

    public void Dispose()
    {
        foreach (var h in _harnesses) h.Dispose();
    }

    private (PipelineHarness Harness, PipelineJobManager Manager, SpyOrchestrator Spy) Build(FakeWhisperService whisper, FakeSilenceService silence)
    {
        var h = new PipelineHarness(whisper, silence);
        _harnesses.Add(h);
        var spy = new SpyOrchestrator();
        var manager = new PipelineJobManager(
            NullLogger<PipelineJobManager>.Instance,
            h.Services.GetRequiredService<IServiceScopeFactory>(),
            whisper, silence,
            new SceneClassifierService(NullLogger<SceneClassifierService>.Instance),
            new CutCandidateService(NullLogger<CutCandidateService>.Instance),
            h.Locks, spy);
        return (h, manager, spy);
    }

    private static async Task<PipelineStatusDto> WaitAsync(PipelineJobManager manager, Guid projectId)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        while (true)
        {
            var s = await manager.GetStatusAsync(projectId);
            if (s.State is "completed" or "completed_with_issues" or "failed" or "canceled") return s;
            if (DateTime.UtcNow > deadline) throw new TimeoutException(s.State);
            await Task.Delay(20);
        }
    }

    private static StartPipelineRequest Start(Guid projectId, List<string>? stages = null) => new(projectId, null, stages, null, false);

    [Fact]
    public async Task CompletedWithIssues_TriggersOrchestratorOnce_AndStatusIsExposed()
    {
        var (h, manager, spy) = Build(new FakeWhisperService { Available = false }, new FakeSilenceService { FfmpegAvailable = false });
        var project = h.CreateProject();
        h.AddMedia(project, "1-Bruto/clip.mp4");

        var jobId = await manager.StartAsync(Start(project.ProjectId));
        var status = await WaitAsync(manager, project.ProjectId);

        Assert.True(status.State is "completed" or "completed_with_issues", status.State);
        var call = Assert.Single(spy.Calls);
        Assert.Equal((project.ProjectId, jobId), call);
        Assert.NotNull(status.PostAnalysis);
        Assert.Equal(2, status.PostAnalysis!.AutoCutsApplied);
        Assert.Equal(3, status.PostAnalysis.PendingReview);
        Assert.Equal(1, status.PostAnalysis.ProxiesQueued);
    }

    [Fact]
    public async Task Failed_DoesNotTriggerOrchestrator()
    {
        var (h, manager, spy) = Build(new FakeWhisperService { Available = false }, new FakeSilenceService { FfmpegAvailable = false });
        var project = h.CreateProject();
        var media = h.AddMedia(project, "1-Bruto/clip.mp4");
        File.Delete(Path.Combine(h.Root, "1-Bruto", "clip.mp4")); // arquivo ausente: todas as etapas falham

        await manager.StartAsync(Start(project.ProjectId));
        var status = await WaitAsync(manager, project.ProjectId);

        Assert.Equal("failed", status.State);
        Assert.Empty(spy.Calls);
    }

    [Fact]
    public async Task Canceled_DoesNotTriggerOrchestrator()
    {
        var whisper = new FakeWhisperService { WaitForCancellation = true };
        var (h, manager, spy) = Build(whisper, new FakeSilenceService { FfmpegAvailable = false });
        var project = h.CreateProject();
        h.AddMedia(project, "1-Bruto/a.mp4");

        var jobId = await manager.StartAsync(Start(project.ProjectId, new List<string> { "transcription" }));
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        while (whisper.TranscribeCalls < 1 && DateTime.UtcNow < deadline) await Task.Delay(20);

        Assert.True(manager.Cancel(jobId));
        var status = await WaitAsync(manager, project.ProjectId);

        Assert.Equal("canceled", status.State);
        Assert.Empty(spy.Calls);
    }

    [Fact]
    public async Task OrchestratorThatThrows_DoesNotChangeAnalysisState()
    {
        var (h, manager, spy) = Build(new FakeWhisperService { Available = false }, new FakeSilenceService { FfmpegAvailable = false });
        var throwing = new ThrowingOrchestrator();
        var manager2 = new PipelineJobManager(
            NullLogger<PipelineJobManager>.Instance,
            h.Services.GetRequiredService<IServiceScopeFactory>(),
            h.Whisper, h.Silence,
            new SceneClassifierService(NullLogger<SceneClassifierService>.Instance),
            new CutCandidateService(NullLogger<CutCandidateService>.Instance),
            h.Locks, throwing);
        var project = h.CreateProject();
        h.AddMedia(project, "1-Bruto/clip.mp4");

        await manager2.StartAsync(Start(project.ProjectId));
        var status = await WaitAsync(manager2, project.ProjectId);

        Assert.True(status.State is "completed" or "completed_with_issues", status.State);
        Assert.Equal(1, throwing.Calls);
    }

    private sealed class ThrowingOrchestrator : IPostAnalysisOrchestrator
    {
        public int Calls { get; private set; }

        public Task ScheduleAsync(Guid projectId, Guid jobId)
        {
            Calls++;
            throw new InvalidOperationException("falha simulada");
        }

        public PostAnalysisStatusDto GetStatus(Guid projectId) =>
            new("idle", null, null, 0, 0, 0, 0, new List<string>(), null, null, null);
    }
}
