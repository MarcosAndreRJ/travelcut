using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TravelCut.Application.Ports;
using TravelCut.Contracts.Dtos;
using TravelCut.Infrastructure.Audio;
using TravelCut.Tests.Import;
using Xunit;

namespace TravelCut.UnitTests.Audio;

/// <summary>F14.8/P14 (backend): sinal de fim do ciclo (análise + lotes pendentes + pós-análise) e reimportação sem job vazio.</summary>
public class PipelineCompletionSignalTests : IDisposable
{
    private sealed class FakePostAnalysis : IPostAnalysisOrchestrator
    {
        public PostAnalysisStatusDto Status { get; set; } = new("idle", null, null, 0, 0, 0, 0, new List<string>(), null, null, null);
        public Task ScheduleAsync(Guid projectId, Guid jobId) => Task.CompletedTask;
        public PostAnalysisStatusDto GetStatus(Guid projectId) => Status;
    }

    private readonly PipelineHarness _h = new(new FakeWhisperService { Available = true, ModelCached = true, FfmpegOnPath = true });
    private readonly FakePostAnalysis _post = new();

    public void Dispose() => _h.Dispose();

    private PipelineJobManager NewManager() => new(
        NullLogger<PipelineJobManager>.Instance,
        _h.Services.GetRequiredService<IServiceScopeFactory>(),
        _h.Whisper, _h.Silence,
        new SceneClassifierService(NullLogger<SceneClassifierService>.Instance),
        new CutCandidateService(NullLogger<CutCandidateService>.Instance),
        _h.Locks, _post);

    private static PostAnalysisStatusDto Post(string state) => new(state, null, null, 0, 0, 0, 0, new List<string>(), null,
        DateTime.UtcNow.AddSeconds(-1), state is "running" or "idle" ? null : DateTime.UtcNow);

    private async Task WaitJobAsync(PipelineJobManager m, Guid projectId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (true)
        {
            var s = await m.GetStatusAsync(projectId);
            if (s.State is "completed" or "completed_with_issues" or "failed" or "canceled") return;
            if (DateTime.UtcNow > deadline) throw new TimeoutException(s.State);
            await Task.Delay(20);
        }
    }

    [Fact]
    public async Task Without_any_job_the_project_is_settled_without_token_so_opening_the_app_does_not_notify()
    {
        var project = _h.CreateProject();
        var status = await NewManager().GetStatusAsync(project.ProjectId);

        Assert.True(status.Completion!.Settled);
        Assert.Null(status.Completion.Token);
        Assert.Null(status.Completion.Outcome);
    }

    [Fact]
    public async Task Running_job_is_not_settled()
    {
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _h.Whisper.TranscribeGate = gate;
        var project = _h.CreateProject();
        _h.AddMedia(project, "1-Bruto/a.mp4");
        var manager = NewManager();

        await manager.StartAsync(new StartPipelineRequest(project.ProjectId, null, null, null, false));
        var running = await manager.GetStatusAsync(project.ProjectId);
        Assert.False(running.Completion!.Settled);

        gate.SetResult(true);
        await WaitJobAsync(manager, project.ProjectId);
    }

    [Fact]
    public async Task Completed_job_settles_only_after_post_analysis_finishes_and_token_changes_per_cycle()
    {
        var project = _h.CreateProject();
        _h.AddMedia(project, "1-Bruto/a.mp4");
        var manager = NewManager();

        await manager.StartAsync(new StartPipelineRequest(project.ProjectId, null, null, null, false));
        await WaitJobAsync(manager, project.ProjectId);

        // Pós-análise ainda idle (prestes a iniciar) ou running: não assentado.
        Assert.False((await manager.GetStatusAsync(project.ProjectId)).Completion!.Settled);
        _post.Status = Post("running");
        Assert.False((await manager.GetStatusAsync(project.ProjectId)).Completion!.Settled);

        _post.Status = Post("completed");
        var done = (await manager.GetStatusAsync(project.ProjectId)).Completion!;
        Assert.True(done.Settled);
        Assert.NotNull(done.Token);
        Assert.Equal("completed_with_issues", done.Outcome); // fake sem resultado = transcrição "failed"

        // Mesmo ciclo = mesmo token (a UI não notifica duas vezes).
        Assert.Equal(done.Token, (await manager.GetStatusAsync(project.ProjectId)).Completion!.Token);

        // Novo fim de pós-análise = token novo.
        await Task.Delay(5);
        _post.Status = Post("completed_with_issues");
        var next = (await manager.GetStatusAsync(project.ProjectId)).Completion!;
        Assert.NotEqual(done.Token, next.Token);
        Assert.Equal("completed_with_issues", next.Outcome);
    }

    [Fact]
    public async Task Failed_post_analysis_is_reported_as_failed_outcome()
    {
        var project = _h.CreateProject();
        _h.AddMedia(project, "1-Bruto/a.mp4");
        var manager = NewManager();
        await manager.StartAsync(new StartPipelineRequest(project.ProjectId, null, null, null, false));
        await WaitJobAsync(manager, project.ProjectId);

        _post.Status = Post("failed");

        Assert.Equal("failed", (await manager.GetStatusAsync(project.ProjectId)).Completion!.Outcome);
    }

    [Fact]
    public async Task Reimporting_already_analyzed_media_does_not_create_an_empty_job()
    {
        using var tmp = new TempDirectory();
        string root = Path.Combine(tmp.Path, "Projeto");
        var fake = new FakePipelineJobManager
        {
            WhisperAvailable = true,
            AnalysisByMedia = id => new MediaAnalysisDto(id, "completed", null, null, "completed", null,
                new List<VadSilenceSegmentDto>(), new List<HesitationCandidateDto>(), null, new List<EditSuggestionDto>(), DateTime.UtcNow)
        };
        using var h = new ImportHarness(pipelineJobs: fake);
        var project = h.CreateProject(root);
        string source = TestFiles.WriteSource(Path.Combine(tmp.Path, "origem", "entrevista.mp4"), TestFiles.RandomBytes(2048));

        var start = await h.Service.StartAsync(ImportHarness.Request(project.ProjectId, new[] { source }, detectSpeech: true));
        var status = await ImportHarness.WaitForFinalAsync(h.Service, start.JobId!.Value);

        Assert.Equal("completed", status.State);
        Assert.Equal("skipped", status.SpeechAnalysis);
        Assert.Empty(fake.StartedRequests);
        Assert.Empty(fake.MergedRequests);
    }

    [Fact]
    public async Task Reimporting_media_with_stale_analysis_still_enqueues_it()
    {
        using var tmp = new TempDirectory();
        string root = Path.Combine(tmp.Path, "Projeto");
        var fake = new FakePipelineJobManager
        {
            WhisperAvailable = true,
            AnalysisByMedia = id => new MediaAnalysisDto(id, "not_run", null, null, "not_run", null,
                new List<VadSilenceSegmentDto>(), new List<HesitationCandidateDto>(), null, new List<EditSuggestionDto>(), null, Stale: true)
        };
        using var h = new ImportHarness(pipelineJobs: fake);
        var project = h.CreateProject(root);
        string source = TestFiles.WriteSource(Path.Combine(tmp.Path, "origem", "entrevista.mp4"), TestFiles.RandomBytes(2048));

        var start = await h.Service.StartAsync(ImportHarness.Request(project.ProjectId, new[] { source }, detectSpeech: true));
        var status = await ImportHarness.WaitForFinalAsync(h.Service, start.JobId!.Value);

        Assert.Equal("queued", status.SpeechAnalysis);
        Assert.Single(fake.StartedRequests);
    }
}
