using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TravelCut.Application.Audio;
using TravelCut.Application.Ports;
using TravelCut.Application.PostAnalysis;
using TravelCut.Contracts.Dtos;
using TravelCut.Infrastructure.Audio;
using Xunit;

namespace TravelCut.UnitTests.Audio;

/// <summary>F14.8/P07: retomada ao abrir o projeto (política explícita), fila pendente e pós-análise persistidos, parciais órfãos.</summary>
public class PipelineResumeTests : IDisposable
{
    private readonly PipelineHarness _h;
    private readonly FilePipelineStateStore _store = new();

    public PipelineResumeTests()
    {
        _h = new PipelineHarness(new FakeWhisperService { Available = true, ModelCached = true, FfmpegOnPath = true });
    }

    public void Dispose() => _h.Dispose();

    private PipelineJobManager NewManager() => new(
        NullLogger<PipelineJobManager>.Instance,
        _h.Services.GetRequiredService<IServiceScopeFactory>(),
        _h.Whisper,
        _h.Silence,
        new SceneClassifierService(NullLogger<SceneClassifierService>.Instance),
        new CutCandidateService(NullLogger<CutCandidateService>.Instance),
        _h.Locks,
        null,
        _store);

    private PipelineResumeService NewResume(PipelineJobManager manager, IPostAnalysisOrchestrator? post = null) => new(
        _h.Services.GetRequiredService<IServiceScopeFactory>(), manager, TimeProvider.System,
        NullLogger<PipelineResumeService>.Instance, post);

    private async Task<PipelineStatusDto> WaitAsync(PipelineJobManager manager, Guid projectId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (true)
        {
            var s = await manager.GetStatusAsync(projectId);
            if (s.State is "completed" or "completed_with_issues" or "failed" or "canceled") return s;
            if (DateTime.UtcNow > deadline) throw new TimeoutException(s.State);
            await Task.Delay(20);
        }
    }

    [Fact]
    public async Task Resume_enqueues_media_without_analysis_and_does_not_retry_failures()
    {
        var project = _h.CreateProject();
        _h.AddMedia(project, "1-Bruto/a.mp4", fingerprint: "fa");
        _h.AddMedia(project, "1-Bruto/b.mp4", fingerprint: "fb");
        var manager = NewManager();
        var resume = NewResume(manager);

        var first = await resume.ResumeProjectAsync(project.ProjectId);
        Assert.Equal("queued", first.Status);
        Assert.Equal(2, first.QueuedMedia);
        await WaitAsync(manager, project.ProjectId);
        Assert.Equal(2, _h.Whisper.TranscribeCalls);

        // Fake sem resultado = transcrição "failed": falha NÃO é reenfileirada automaticamente (retryFailed é explícito).
        var second = await resume.ResumeProjectAsync(project.ProjectId);
        Assert.Equal("skipped", second.Status);
        Assert.Equal(PipelineResumeService.ReasonNothingToResume, second.Reason);
        Assert.Equal(2, _h.Whisper.TranscribeCalls);
    }

    [Fact]
    public async Task Resume_respects_auto_process_disabled_and_missing_whisper()
    {
        var project = _h.CreateProject();
        _h.AddMedia(project, "1-Bruto/a.mp4", fingerprint: "fa");
        var manager = NewManager();
        var resume = NewResume(manager);

        project.AutoProcess = false;
        _h.Db.SaveChanges();
        var off = await resume.ResumeProjectAsync(project.ProjectId);
        Assert.Equal(PipelineResumeService.ReasonAutoProcessDisabled, off.Reason);

        project.AutoProcess = true;
        _h.Db.SaveChanges();
        _h.Whisper.Available = false;
        var noWhisper = await resume.ResumeProjectAsync(project.ProjectId);
        Assert.Equal(PipelineResumeService.ReasonWhisperUnavailable, noWhisper.Reason);
        Assert.Equal(0, _h.Whisper.TranscribeCalls);
    }

    [Fact]
    public async Task Resume_removes_old_tcpart_only_and_never_touches_originals()
    {
        var project = _h.CreateProject();
        var media = _h.AddMedia(project, "1-Bruto/a.mp4", fingerprint: "fa");
        string brutoDir = Path.Combine(_h.Root, "1-Bruto");
        string oldPart = Path.Combine(brutoDir, "x.mp4.tcpart");
        string freshPart = Path.Combine(brutoDir, "y.mp4.tcpart");
        File.WriteAllBytes(oldPart, new byte[8]);
        File.WriteAllBytes(freshPart, new byte[8]);
        File.SetLastWriteTimeUtc(oldPart, DateTime.UtcNow.AddHours(-2));
        _h.Whisper.Available = false; // sem análise: só a limpeza é exercitada

        var outcome = await NewResume(NewManager()).ResumeProjectAsync(project.ProjectId);

        Assert.Equal(1, outcome.PartialFilesRemoved);
        Assert.False(File.Exists(oldPart));
        Assert.True(File.Exists(freshPart));
        Assert.True(File.Exists(Path.Combine(_h.Root, media.RelativePath)));
    }

    [Fact]
    public async Task Pending_queue_is_persisted_and_restored_by_a_new_manager()
    {
        var project = _h.CreateProject();
        var a = _h.AddMedia(project, "1-Bruto/a.mp4", fingerprint: "fa");
        var b = _h.AddMedia(project, "1-Bruto/b.mp4", fingerprint: "fb");
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _h.Whisper.TranscribeGate = gate;

        var first = NewManager();
        await first.EnqueueAsync(new StartPipelineRequest(project.ProjectId, new List<Guid> { a.MediaId }, null, null, false));
        var merged = await first.EnqueueAsync(new StartPipelineRequest(project.ProjectId, new List<Guid> { b.MediaId }, null, null, false));
        Assert.Equal(PipelineEnqueueResult.Merged, merged.Outcome);

        var saved = _store.LoadPending(_h.Root);
        Assert.Single(saved);
        Assert.Equal(new[] { b.MediaId }, saved[0].MediaIds);

        // "Reinício do app": gerenciador novo, mesmo disco. O lote persistido volta e inicia quando o slot está livre.
        var second = NewManager();
        Assert.Equal(1, await second.RestorePendingAsync(project.ProjectId));
        gate.SetResult(true);
        await WaitForAsync(() => _store.LoadPending(_h.Root).Count == 0);
        await WaitForAsync(async () => (await second.GetStatusAsync(project.ProjectId)).JobId != null);
    }

    [Fact]
    public async Task PostAnalysis_state_is_persisted_and_restored()
    {
        var project = _h.CreateProject();
        var done = new PostAnalysisStatusDto("completed_with_issues", null, null, 3, 2, 0, 1,
            new List<string> { "PROXY_FAILED:1" }, 4, DateTime.UtcNow.AddMinutes(-5), DateTime.UtcNow);
        _store.SavePostAnalysis(_h.Root, done);

        var orchestrator = new PostAnalysisOrchestrator(
            _h.Services.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System,
            NullLogger<PostAnalysisOrchestrator>.Instance, _store);

        Assert.Equal("idle", orchestrator.GetStatus(project.ProjectId).State);
        Assert.False(await orchestrator.RestoreAsync(project.ProjectId));

        var restored = orchestrator.GetStatus(project.ProjectId);
        Assert.Equal("completed_with_issues", restored.State);
        Assert.Equal(3, restored.AutoCutsApplied);
        Assert.Equal(4, restored.PlanRevision);
    }

    [Fact]
    public async Task Interrupted_post_analysis_is_rescheduled_and_persists_its_final_state()
    {
        var project = _h.CreateProject();
        _store.SavePostAnalysis(_h.Root, new PostAnalysisStatusDto("running", "plan", null, 0, 0, 0, 0,
            new List<string>(), null, DateTime.UtcNow, null));

        var orchestrator = new PostAnalysisOrchestrator(
            _h.Services.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System,
            NullLogger<PostAnalysisOrchestrator>.Instance, _store);

        Assert.True(await orchestrator.RestoreAsync(project.ProjectId));
        await WaitForAsync(() => _store.LoadPostAnalysis(_h.Root)?.State is not (null or "running"));
        Assert.NotEqual("running", orchestrator.GetStatus(project.ProjectId).State);
    }

    [Fact]
    public void Needs_analysis_policy_is_explicit()
    {
        MediaAnalysisDto A(string tr, string si, bool stale = false) => new(Guid.NewGuid(), tr, null, null, si, null,
            new List<VadSilenceSegmentDto>(), new List<HesitationCandidateDto>(), null, new List<EditSuggestionDto>(), null, stale);

        Assert.True(PipelineResumeService.NeedsAnalysis(null));
        Assert.True(PipelineResumeService.NeedsAnalysis(A("not_run", "completed")));
        Assert.True(PipelineResumeService.NeedsAnalysis(A("completed", "not_run")));
        Assert.True(PipelineResumeService.NeedsAnalysis(A("completed", "completed", stale: true)));
        Assert.False(PipelineResumeService.NeedsAnalysis(A("completed", "completed")));
        Assert.False(PipelineResumeService.NeedsAnalysis(A("failed", "unavailable")));
        Assert.False(PipelineResumeService.NeedsAnalysis(A("skipped", "skipped")));
    }

    private static Task WaitForAsync(Func<bool> condition) => WaitForAsync(() => Task.FromResult(condition()));

    private static async Task WaitForAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (!await condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("condição não atendida");
            await Task.Delay(20);
        }
    }
}
