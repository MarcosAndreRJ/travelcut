using TravelCut.Application.Audio;
using TravelCut.Application.Ports;
using TravelCut.Contracts.Dtos;
using TravelCut.Domain.Entities;
using TravelCut.Domain.Enums;
using TravelCut.Infrastructure.Analysis;
using TravelCut.Infrastructure.Persistence;
using TravelCut.Infrastructure.Repositories;
using Xunit;

namespace TravelCut.UnitTests.Audio;

public class PipelineJobManagerTests : IDisposable
{
    private readonly List<PipelineHarness> _harnesses = new();

    public void Dispose()
    {
        foreach (var h in _harnesses) h.Dispose();
    }

    private PipelineHarness NewHarness(
        FakeWhisperService? whisper = null,
        FakeSilenceService? silence = null,
        Func<TravelCutDbContext, ITranscriptRepository>? transcriptRepositoryFactory = null)
    {
        var h = new PipelineHarness(whisper, silence, transcriptRepositoryFactory);
        _harnesses.Add(h);
        return h;
    }

    /// <summary>Transcrição fake real-shaped: "cachoeira" termina em 1,0 s; "olha" começa em 3,4 s.</summary>
    private static TranscriptionOutcome CompletedTranscription() => new(
        Status: "completed",
        Transcript: new TranscriptDto(
            TranscriptId: Guid.NewGuid(),
            MediaId: Guid.Empty,
            ModelId: "large-v3",
            Language: "pt",
            FullText: "a cachoeira olha que linda",
            AudioDurationSeconds: 10.0,
            Words: new List<TranscriptWordDto>
            {
                new(0, 0.5, 1.0, "cachoeira", 0.95),
                new(1, 3.4, 3.8, "olha", 0.90)
            }),
        Reason: null,
        Engine: "fake-whisper 1.0",
        ModelId: "large-v3");

    private static SilenceDetectionOutcome CompletedSilence(params (double Start, double End)[] intervals) => new(
        Status: "completed",
        Segments: intervals.Select(i => new VadSilenceSegmentDto(i.Start, i.End, i.End - i.Start, -30.0, "high")).ToList(),
        Reason: null);

    private static StartPipelineRequest Start(Guid projectId, bool force = false, List<string>? stages = null, List<Guid>? mediaIds = null) =>
        new(projectId, mediaIds, stages, null, force);

    [Fact]
    public async Task WhisperAndFfmpegUnavailable_StagesAreUnavailable_AndNothingIsInvented()
    {
        var h = NewHarness(whisper: new FakeWhisperService { Available = false }, silence: new FakeSilenceService { FfmpegAvailable = false });
        var project = h.CreateProject();
        var media = h.AddMedia(project, "1-Bruto/clip.mp4");

        await h.Manager.StartAsync(Start(project.ProjectId));
        var status = await h.WaitForFinishAsync(project.ProjectId);

        Assert.Equal("completed_with_issues", status.State);
        var stages = status.Stages.ToDictionary(s => s.Stage);
        Assert.Equal("unavailable", stages["transcription"].State);
        Assert.Equal("unavailable", stages["silence"].State);
        Assert.Equal("skipped", stages["hesitation"].State);
        Assert.Equal("completed", stages["scene"].State);
        Assert.Equal("skipped", stages["candidates"].State);
        Assert.Equal("Transcrição (Whisper large-v3)", stages["transcription"].Label);
        Assert.False(string.IsNullOrWhiteSpace(stages["transcription"].Reason));

        Assert.Equal(0, h.Db.Transcripts.Count());
        Assert.Equal(0, h.Db.EditSuggestions.Count());

        var analysis = await h.Manager.GetMediaAnalysisAsync(project.ProjectId, media.MediaId);
        Assert.NotNull(analysis);
        Assert.Equal("unavailable", analysis!.TranscriptionStatus);
        Assert.Null(analysis.Transcript);
        Assert.Empty(analysis.Silences);
        Assert.Empty(analysis.CutCandidates);
        Assert.Equal("indeterminado", analysis.Scene!.Category);
        Assert.Null(analysis.Scene.HasSpeech);
        Assert.True(analysis.Scene.IsHypothesis);
    }

    [Fact]
    public async Task WhisperUnavailableButSilenceAvailable_CandidatesComeOnlyFromRealSilence()
    {
        var h = NewHarness(whisper: new FakeWhisperService { Available = false }, silence: new FakeSilenceService
        {
            Outcome = CompletedSilence((1.0, 3.4))
        });
        var project = h.CreateProject();
        h.AddMedia(project, "1-Bruto/clip.mp4");

        await h.Manager.StartAsync(Start(project.ProjectId));
        var status = await h.WaitForFinishAsync(project.ProjectId);

        Assert.Equal("completed_with_issues", status.State);
        Assert.Equal("completed_with_issues", status.Stages.Single(s => s.Stage == "candidates").State);
        Assert.Equal(0, h.Db.Transcripts.Count());
        var suggestion = Assert.Single(h.Db.EditSuggestions);
        Assert.Equal("vad-analyzer", suggestion.Author);
        Assert.Equal(1000, suggestion.SourceInterval.StartTicks);
        Assert.Equal(3400, suggestion.SourceInterval.EndTicks);
        Assert.Contains("sem transcrição para contexto", suggestion.Rationale);
        Assert.True(suggestion.NeedsHumanReview);
    }

    [Fact]
    public async Task CompletedFakes_PersistTranscript_AndGenerateCandidateWithEvidence()
    {
        var whisper = new FakeWhisperService { Outcome = CompletedTranscription() };
        var silence = new FakeSilenceService { Outcome = CompletedSilence((1.0, 3.4)) };
        var h = NewHarness(whisper, silence);
        var project = h.CreateProject();
        var media = h.AddMedia(project, "1-Bruto/clip.mp4");

        await h.Manager.StartAsync(Start(project.ProjectId));
        var status = await h.WaitForFinishAsync(project.ProjectId);

        Assert.Equal("completed", status.State);
        Assert.All(status.Stages, s => Assert.Equal("completed", s.State));

        var transcript = Assert.Single(h.Db.Transcripts.Select(t => t.MediaId).ToList());
        Assert.Equal(media.MediaId, transcript);
        var words = h.Db.TranscriptWords.Count();
        Assert.Equal(2, words);

        var suggestion = Assert.Single(h.Db.EditSuggestions);
        Assert.Equal("vad-analyzer", suggestion.Author);
        Assert.Equal("Proposed", suggestion.Status.ToString());
        Assert.Equal(
            "Silêncio de 2,4 s (silencedetect −30 dB) entre 'cachoeira' e 'olha'.",
            suggestion.Rationale);

        var analysis = await h.Manager.GetMediaAnalysisAsync(project.ProjectId, media.MediaId);
        Assert.NotNull(analysis);
        Assert.Equal("completed", analysis!.TranscriptionStatus);
        Assert.NotNull(analysis.Transcript);
        Assert.Single(analysis.CutCandidates);
        Assert.NotNull(analysis.AnalyzedAtUtc);
    }

    [Fact]
    public async Task SecondRun_ReusesCache_AndForceBypassesIt()
    {
        var whisper = new FakeWhisperService { Outcome = CompletedTranscription() };
        var silence = new FakeSilenceService { Outcome = CompletedSilence((1.0, 3.4)) };
        var h = NewHarness(whisper, silence);
        var project = h.CreateProject();
        h.AddMedia(project, "1-Bruto/clip.mp4");

        await h.Manager.StartAsync(Start(project.ProjectId));
        await h.WaitForFinishAsync(project.ProjectId);
        Assert.Equal(1, whisper.TranscribeCalls);
        Assert.Equal(1, silence.DetectCalls);

        await h.Manager.StartAsync(Start(project.ProjectId));
        var second = await h.WaitForFinishAsync(project.ProjectId);
        Assert.Equal(1, whisper.TranscribeCalls);
        Assert.Equal(1, silence.DetectCalls);
        Assert.True(second.Media.Single().FromCache);

        await h.Manager.StartAsync(Start(project.ProjectId, force: true));
        var forced = await h.WaitForFinishAsync(project.ProjectId);
        Assert.Equal(2, whisper.TranscribeCalls);
        Assert.Equal(2, silence.DetectCalls);
        Assert.False(forced.Media.Single().FromCache);
    }

    [Fact]
    public async Task SecondStartWhileRunning_ThrowsPipelineInProgress()
    {
        var whisper = new FakeWhisperService { WaitForCancellation = true };
        var h = NewHarness(whisper, new FakeSilenceService { FfmpegAvailable = false });
        var project = h.CreateProject();
        h.AddMedia(project, "1-Bruto/clip.mp4");

        var jobId = await h.Manager.StartAsync(Start(project.ProjectId));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => h.Manager.StartAsync(Start(project.ProjectId)));
        Assert.Equal("PIPELINE_IN_PROGRESS", ex.Message);

        Assert.True(h.Manager.Cancel(jobId));
        await h.WaitForFinishAsync(project.ProjectId);
    }

    [Fact]
    public async Task Cancel_MarksCurrentAndRemainingMediaAsCanceled()
    {
        var whisper = new FakeWhisperService { WaitForCancellation = true };
        var h = NewHarness(whisper, new FakeSilenceService { FfmpegAvailable = false });
        var project = h.CreateProject();
        h.AddMedia(project, "1-Bruto/a.mp4");
        h.AddMedia(project, "1-Bruto/b.mp4");

        var jobId = await h.Manager.StartAsync(Start(project.ProjectId, stages: new List<string> { "transcription" }));
        await WaitUntilAsync(() => whisper.TranscribeCalls >= 1);

        Assert.True(h.Manager.Cancel(jobId));
        var status = await h.WaitForFinishAsync(project.ProjectId);

        Assert.Equal("canceled", status.State);
        Assert.Equal(2, status.Media.Count);
        Assert.All(status.Media, m => Assert.Equal("canceled", m.StageStates["transcription"]));
        var stage = Assert.Single(status.Stages);
        Assert.Equal(2, stage.TotalItems);
        Assert.Equal(2, stage.ProcessedItems);
        Assert.False(h.Manager.Cancel(jobId), "job já finalizado não pode ser cancelado de novo");
    }

    [Fact]
    public async Task LongTranscription_ExposesRealActivity_WhileMediaCountStaysZero()
    {
        var whisper = new FakeWhisperService
        {
            WaitForCancellation = true,
            ProgressDelivered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        whisper.ProgressScript.Add(new TranscriptionProgressUpdate("loading_model", null));
        whisper.ProgressScript.Add(new TranscriptionProgressUpdate("transcribing", 37.5));
        var h = NewHarness(whisper, new FakeSilenceService { FfmpegAvailable = false });
        var project = h.CreateProject();
        h.AddMedia(project, "1-Bruto/longo.mp4");

        var jobId = await h.Manager.StartAsync(Start(project.ProjectId, stages: new List<string> { "transcription" }));
        await whisper.ProgressDelivered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var status = await h.Manager.GetStatusAsync(project.ProjectId);
        var activity = status.Activity!;

        Assert.Equal("running", status.State);
        Assert.Equal("1-Bruto/longo.mp4", status.CurrentMedia);
        Assert.Equal("transcription", activity.CurrentStage);
        Assert.Equal("Transcrição (Whisper large-v3)", activity.CurrentStageLabel);
        Assert.Equal("Transcrevendo", activity.Detail);
        Assert.Equal(37.5, activity.Percent);
        Assert.NotNull(activity.LastActivityAtUtc);
        Assert.False(activity.CancelRequested);
        Assert.Equal(0, activity.MediaCompleted);
        Assert.Equal(1, activity.MediaTotal);
        Assert.Equal(0, Assert.Single(status.Stages).ProcessedItems);

        Assert.True(h.Manager.Cancel(jobId));
        var canceling = await h.Manager.GetStatusAsync(project.ProjectId);
        Assert.True(canceling.State == "canceled" || canceling.Activity!.CancelRequested);

        var final = await h.WaitForFinishAsync(project.ProjectId);
        Assert.Equal("canceled", final.State);
        Assert.Null(final.Activity!.CurrentStage);
        Assert.Null(final.Activity.Percent);
    }

    [Fact]
    public async Task Transcription_WithoutEnginePercent_KeepsPercentNull()
    {
        var whisper = new FakeWhisperService
        {
            WaitForCancellation = true,
            ProgressDelivered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        whisper.ProgressScript.Add(new TranscriptionProgressUpdate("transcribing", null));
        var h = NewHarness(whisper, new FakeSilenceService { FfmpegAvailable = false });
        var project = h.CreateProject();
        h.AddMedia(project, "1-Bruto/longo.mp4");

        var jobId = await h.Manager.StartAsync(Start(project.ProjectId, stages: new List<string> { "transcription" }));
        await whisper.ProgressDelivered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var activity = (await h.Manager.GetStatusAsync(project.ProjectId)).Activity!;
        Assert.Equal("Transcrevendo", activity.Detail);
        Assert.Null(activity.Percent);

        h.Manager.Cancel(jobId);
        await h.WaitForFinishAsync(project.ProjectId);
    }

    [Fact]
    public async Task Finished_ReportsCompletedMedia_AndClearsCurrentStage()
    {
        var whisper = new FakeWhisperService { Outcome = CompletedTranscription() };
        whisper.ProgressScript.Add(new TranscriptionProgressUpdate("transcribing", 80));
        var h = NewHarness(whisper, new FakeSilenceService { FfmpegAvailable = false });
        var project = h.CreateProject();
        h.AddMedia(project, "1-Bruto/clip.mp4");

        await h.Manager.StartAsync(Start(project.ProjectId, stages: new List<string> { "transcription" }));
        var status = await h.WaitForFinishAsync(project.ProjectId);

        Assert.Equal("completed", status.State);
        Assert.Equal(1, status.Activity!.MediaCompleted);
        Assert.Null(status.Activity.CurrentStage);
        Assert.Null(status.Activity.Percent);
    }

    [Fact]
    public async Task Environment_ForceRefresh_BypassesCache_AfterInstall()
    {
        var whisper = new FakeWhisperService { Available = false };
        var h = NewHarness(whisper);

        Assert.False((await h.Manager.GetEnvironmentAsync()).WhisperAvailable);

        // Instalação concluída: o cache de 60 s ainda responde o estado antigo.
        whisper.Available = true;
        Assert.False((await h.Manager.GetEnvironmentAsync()).WhisperAvailable);
        Assert.Equal(0, whisper.InvalidateCalls);

        Assert.True((await h.Manager.GetEnvironmentAsync(forceRefresh: true)).WhisperAvailable);
        Assert.Equal(1, whisper.InvalidateCalls);
    }

    [Fact]
    public async Task Diagnostics_ListWhisperExecutionsOfLatestJob_WithLogs()
    {
        var whisper = new FakeWhisperService { Outcome = CompletedTranscription() };
        var h = NewHarness(whisper, new FakeSilenceService { FfmpegAvailable = false });
        var project = h.CreateProject();
        var media = h.AddMedia(project, "1-Bruto/clip.mp4");

        Assert.Empty(h.Manager.GetDiagnostics(project.ProjectId).Executions);

        var jobId = await h.Manager.StartAsync(Start(project.ProjectId, stages: new List<string> { "transcription" }));
        await h.WaitForFinishAsync(project.ProjectId);
        var diagnostics = h.Manager.GetDiagnostics(project.ProjectId);

        Assert.Equal(jobId, diagnostics.JobId);
        var execution = Assert.Single(diagnostics.Executions);
        Assert.Equal(media.MediaId, execution.MediaId);
        Assert.Equal("fake-python", execution.PythonExecutable);
        Assert.Equal("completed", execution.ProcessState);
        Assert.NotNull(execution.FinishedAtUtc);
        Assert.Contains(execution.LogLines, l => l.Text == "aviso de teste");
        Assert.Contains("fake-python", execution.DiagnosticScript);
    }

    [Fact]
    public async Task IdleProject_HasNoActivity()
    {
        var h = NewHarness();
        var project = h.CreateProject();

        var status = await h.Manager.GetStatusAsync(project.ProjectId);

        Assert.Equal("idle", status.State);
        Assert.Null(status.Activity);
    }

    [Fact]
    public async Task RerunWithForce_PreservesDecidedSuggestions_AndReplacesProposedOfSameAuthor()
    {
        var whisper = new FakeWhisperService { Outcome = CompletedTranscription() };
        var silence = new FakeSilenceService { Outcome = CompletedSilence((1.0, 3.4), (5.0, 7.5)) };
        var h = NewHarness(whisper, silence);
        var project = h.CreateProject();
        h.AddMedia(project, "1-Bruto/clip.mp4");

        await h.Manager.StartAsync(Start(project.ProjectId));
        await h.WaitForFinishAsync(project.ProjectId);

        var first = h.Db.EditSuggestions.ToList().OrderBy(s => s.SourceInterval.StartTicks).ToList();
        Assert.Equal(2, first.Count);
        var decided = first[0];
        decided.Status = SuggestionStatus.Approved;
        h.Db.SaveChanges();
        var oldProposed = first[1];

        silence.Outcome = CompletedSilence((9.0, 11.5));
        await h.Manager.StartAsync(Start(project.ProjectId, force: true));
        await h.WaitForFinishAsync(project.ProjectId);

        var now = h.Db.EditSuggestions.ToList().OrderBy(s => s.SourceInterval.StartTicks).ToList();
        Assert.Equal(2, now.Count);
        Assert.Contains(now, s => s.SuggestionId == decided.SuggestionId && s.Status == SuggestionStatus.Approved);
        Assert.DoesNotContain(now, s => s.SuggestionId == oldProposed.SuggestionId);
        var replacement = Assert.Single(now.Where(s => s.Status == SuggestionStatus.Proposed));
        Assert.Equal(9000, replacement.SourceInterval.StartTicks);
    }

    [Fact]
    public async Task RerunWithForce_KeepsSuggestionDecidedOnlyViaDecisionRow()
    {
        // Caso real: decision.submit grava só EditDecision; o Status salvo da sugestão continua Proposed.
        var whisper = new FakeWhisperService { Outcome = CompletedTranscription() };
        var silence = new FakeSilenceService { Outcome = CompletedSilence((1.0, 3.4)) };
        var h = NewHarness(whisper, silence);
        var project = h.CreateProject();
        h.AddMedia(project, "1-Bruto/clip.mp4");

        await h.Manager.StartAsync(Start(project.ProjectId));
        await h.WaitForFinishAsync(project.ProjectId);
        var suggestion = Assert.Single(h.Db.EditSuggestions);
        Assert.Equal(SuggestionStatus.Proposed, suggestion.Status);
        h.Db.EditDecisions.Add(new EditDecision { SuggestionId = suggestion.SuggestionId, Action = "approve", Version = 1 });
        h.Db.SaveChanges();

        await h.Manager.StartAsync(Start(project.ProjectId, force: true));
        await h.WaitForFinishAsync(project.ProjectId);

        var kept = Assert.Single(h.Db.EditSuggestions.ToList());
        Assert.Equal(suggestion.SuggestionId, kept.SuggestionId);
        Assert.Single(h.Db.EditDecisions.ToList(), d => d.SuggestionId == suggestion.SuggestionId);
    }

    [Fact]
    public async Task RerunWithSameSilence_DoesNotDuplicateDecidedCandidate()
    {
        var whisper = new FakeWhisperService { Outcome = CompletedTranscription() };
        var silence = new FakeSilenceService { Outcome = CompletedSilence((1.0, 3.4)) };
        var h = NewHarness(whisper, silence);
        var project = h.CreateProject();
        h.AddMedia(project, "1-Bruto/clip.mp4");

        await h.Manager.StartAsync(Start(project.ProjectId));
        await h.WaitForFinishAsync(project.ProjectId);
        var only = Assert.Single(h.Db.EditSuggestions);
        only.Status = SuggestionStatus.Rejected;
        h.Db.SaveChanges();

        await h.Manager.StartAsync(Start(project.ProjectId, force: true));
        await h.WaitForFinishAsync(project.ProjectId);

        var all = h.Db.EditSuggestions.ToList();
        var kept = Assert.Single(all);
        Assert.Equal(SuggestionStatus.Rejected, kept.Status);
    }

    [Fact]
    public async Task StageTotals_UseRealMediaDenominator_AndStageFilterApplies()
    {
        var h = NewHarness(
            whisper: new FakeWhisperService { Available = false },
            silence: new FakeSilenceService { Outcome = CompletedSilence() });
        var project = h.CreateProject();
        h.AddMedia(project, "1-Bruto/a.mp4");
        h.AddMedia(project, "1-Bruto/b.mp4");

        await h.Manager.StartAsync(Start(project.ProjectId));
        var status = await h.WaitForFinishAsync(project.ProjectId);

        Assert.Equal(2, status.Media.Count);
        Assert.All(status.Stages, s => Assert.Equal(2, s.TotalItems));
        Assert.All(status.Stages, s => Assert.Equal(2, s.ProcessedItems));
        Assert.Equal("unavailable", status.Stages.Single(s => s.Stage == "transcription").State);

        await h.Manager.StartAsync(Start(project.ProjectId, stages: new List<string> { "scene" }));
        var filtered = await h.WaitForFinishAsync(project.ProjectId);
        var only = Assert.Single(filtered.Stages);
        Assert.Equal("scene", only.Stage);
        Assert.Equal(2, only.TotalItems);
        Assert.Equal(2, only.ProcessedItems);
        Assert.Equal("completed", only.State);
    }

    [Fact]
    public async Task NoJob_StatusIsIdleWithoutStages()
    {
        var h = NewHarness();
        var project = h.CreateProject();

        var status = await h.Manager.GetStatusAsync(project.ProjectId);

        Assert.Equal("idle", status.State);
        Assert.Null(status.JobId);
        Assert.Empty(status.Stages);
        Assert.Empty(status.Media);
        Assert.Null(status.CurrentMedia);
        Assert.NotNull(status.Environment);
        Assert.False(status.Environment!.WhisperAvailable);
        Assert.Equal("large-v3", status.Environment.WhisperModel);
    }

    [Fact]
    public async Task InvalidRequests_AreRejectedWithoutLeavingActiveSlot()
    {
        var h = NewHarness();
        var project = h.CreateProject();
        h.AddMedia(project, "1-Bruto/clip.mp4");

        var invalidStage = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.Manager.StartAsync(Start(project.ProjectId, stages: new List<string> { "inventada" })));
        Assert.Equal("INVALID_STAGE", invalidStage.Message);

        var missing = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.Manager.StartAsync(Start(Guid.NewGuid())));
        Assert.Equal("PROJECT_NOT_FOUND", missing.Message);

        // Falha de validação não pode deixar o projeto preso em "PIPELINE_IN_PROGRESS".
        var jobId = await h.Manager.StartAsync(Start(project.ProjectId));
        Assert.NotEqual(Guid.Empty, jobId);
        await h.WaitForFinishAsync(project.ProjectId);
    }

    [Fact]
    public async Task MediaOutsideMediaKinds_AreNotSelected_AndEmptySelectionIsRejected()
    {
        var h = NewHarness();
        var project = h.CreateProject();
        h.AddMedia(project, "1-Bruto/foto.jpg", MediaKind.Image);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => h.Manager.StartAsync(Start(project.ProjectId)));
        Assert.Equal("NO_MEDIA_TO_ANALYZE", ex.Message);
    }

    [Fact]
    public async Task MediaAnalysis_ForUnknownOrNeverAnalyzedMedia_ReturnsNull()
    {
        var h = NewHarness();
        var project = h.CreateProject();
        var media = h.AddMedia(project, "1-Bruto/clip.mp4");

        Assert.Null(await h.Manager.GetMediaAnalysisAsync(project.ProjectId, media.MediaId));
        Assert.Null(await h.Manager.GetMediaAnalysisAsync(project.ProjectId, Guid.NewGuid()));
    }

    [Fact]
    public async Task PersistenceFailureInOneMedia_DoesNotContaminateNextMedia_AndReleasesProjectSlot()
    {
        // Falha simulada deixa a transcrição rastreada pelo DbContext da mídia "a-falha" (a ordem por caminho
        // processa "a-falha" antes de "b-ok"). A mídia seguinte precisa persistir, sem reenviar o estado da anterior.
        Guid failingMediaId = Guid.Empty;
        var h = NewHarness(
            whisper: new FakeWhisperService { Outcome = CompletedTranscription() },
            transcriptRepositoryFactory: db => new FailingTranscriptRepository(
                new SqliteTranscriptRepository(db), db, () => failingMediaId));
        var project = h.CreateProject();
        var failing = h.AddMedia(project, "1-Bruto/a-falha.mp4");
        var ok = h.AddMedia(project, "1-Bruto/b-ok.mp4");
        failingMediaId = failing.MediaId;
        var stages = new List<string> { PipelineStages.Transcription };

        await h.Manager.StartAsync(Start(project.ProjectId, stages: stages));
        var status = await h.WaitForFinishAsync(project.ProjectId);

        Assert.NotEqual("running", status.State);
        var persisted = h.Db.Transcripts.Select(t => t.MediaId).ToList();
        Assert.Contains(ok.MediaId, persisted);
        Assert.DoesNotContain(failing.MediaId, persisted);

        // Slot do projeto liberado após a falha: uma nova análise é aceita.
        var second = await h.Manager.StartAsync(Start(project.ProjectId, stages: stages));
        Assert.NotEqual(Guid.Empty, second);
        await h.WaitForFinishAsync(project.ProjectId);
    }

    /// <summary>Repositório que, para uma mídia, rastreia a entidade no contexto e depois falha (como um SaveChanges com erro).</summary>
    private sealed class FailingTranscriptRepository : ITranscriptRepository
    {
        private readonly ITranscriptRepository _inner;
        private readonly TravelCutDbContext _db;
        private readonly Func<Guid> _failingMediaId;

        public FailingTranscriptRepository(ITranscriptRepository inner, TravelCutDbContext db, Func<Guid> failingMediaId)
        {
            _inner = inner;
            _db = db;
            _failingMediaId = failingMediaId;
        }

        public Task<Transcript?> GetByMediaIdAsync(Guid mediaId, CancellationToken ct = default) =>
            _inner.GetByMediaIdAsync(mediaId, ct);

        public Task SaveAsync(Transcript transcript, CancellationToken ct = default)
        {
            if (transcript.MediaId == _failingMediaId())
            {
                _db.Transcripts.Add(transcript);
                throw new IOException("Falha simulada de persistência (teste).");
            }

            return _inner.SaveAsync(transcript, ct);
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 10000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Condição não atingida a tempo.");
            await Task.Delay(10);
        }
    }
}
