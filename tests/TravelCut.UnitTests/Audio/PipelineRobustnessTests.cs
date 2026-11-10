using TravelCut.Application.Audio;
using TravelCut.Contracts.Dtos;
using TravelCut.Domain.Entities;
using TravelCut.Domain.Enums;
using TravelCut.Infrastructure.Audio;
using Xunit;

namespace TravelCut.UnitTests.Audio;

/// <summary>Robustez do pipeline pós-F14.8 (P06-P15).</summary>
public class PipelineRobustnessTests : IDisposable
{
    private readonly List<PipelineHarness> _harnesses = new();

    public void Dispose()
    {
        foreach (var h in _harnesses) h.Dispose();
    }

    private PipelineHarness NewHarness(FakeWhisperService? whisper = null, FakeSilenceService? silence = null)
    {
        var h = new PipelineHarness(whisper, silence);
        _harnesses.Add(h);
        return h;
    }

    internal static MediaMetadata Metadata(int audioChannels, string status = MediaMetadataStatus.Ok) => new()
    {
        Codec = "h264",
        Width = 1920,
        Height = 1080,
        DurationSeconds = 10,
        AudioChannels = audioChannels,
        MetadataStatus = status
    };

    private static StartPipelineRequest Start(Guid projectId, bool force = false, List<string>? stages = null) =>
        new(projectId, null, stages, null, force);

    private static TranscriptionOutcome Completed(double duration = 10.0) => new(
        "completed",
        new TranscriptDto(Guid.NewGuid(), Guid.Empty, "large-v3", "pt", "a cachoeira olha", duration,
            new List<TranscriptWordDto> { new(0, 0.5, 1.0, "cachoeira", 0.95), new(1, 3.4, 3.8, "olha", 0.9) }),
        null, "fake-whisper 1.0", "large-v3");

    // ---------------------------------------------------------------- P06

    [Fact]
    public async Task P06_CancelMidMedia_PersistsCompletedStagesInAnalysisCache()
    {
        var whisper = new FakeWhisperService { Outcome = Completed() };
        var silence = new FakeSilenceService { WaitForCancellation = true };
        var h = NewHarness(whisper, silence);
        var project = h.CreateProject();
        var media = h.AddMedia(project, "1-Bruto/a.mp4", metadata: Metadata(2));

        var jobId = await h.Manager.StartAsync(Start(project.ProjectId, stages: new List<string> { "transcription", "silence" }));
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (silence.DetectCalls < 1 && DateTime.UtcNow < deadline) await Task.Delay(20);
        Assert.True(h.Manager.Cancel(jobId));
        var status = await h.WaitForFinishAsync(project.ProjectId);

        Assert.Equal("canceled", status.State);
        Assert.Single(h.Db.Transcripts); // transcrição já persistida no banco
        var analysis = await h.Manager.GetMediaAnalysisAsync(project.ProjectId, media.MediaId);
        Assert.NotNull(analysis);
        Assert.Equal("completed", analysis!.TranscriptionStatus);
        Assert.NotNull(analysis.Transcript);
        Assert.Equal("not_run", analysis.SilenceStatus);
    }

    // ---------------------------------------------------------------- P12

    private static TranscriptionOutcome Failed(string? code) => new("failed", null, "falha de teste", null, "large-v3", code);

    private static readonly List<string> TranscriptionOnly = new() { "transcription" };

    [Fact]
    public async Task P12_TransientFailure_IsRetriedOnce_AndRecovers()
    {
        var whisper = new FakeWhisperService();
        whisper.ScriptedOutcomes.Enqueue(Failed(TranscriptionFailureCodes.Transient));
        whisper.ScriptedOutcomes.Enqueue(Completed());
        var h = NewHarness(whisper, new FakeSilenceService { FfmpegAvailable = false });
        var project = h.CreateProject();
        h.AddMedia(project, "1-Bruto/a.mp4", metadata: Metadata(2));

        await h.Manager.StartAsync(Start(project.ProjectId, stages: TranscriptionOnly));
        var status = await h.WaitForFinishAsync(project.ProjectId);

        Assert.Equal("completed", status.State);
        Assert.Equal(2, whisper.TranscribeCalls);
        Assert.Empty(whisper.DeviceOverrides);
    }

    [Fact]
    public async Task P12_PersistentTransientFailure_RetriesOnlyOnce_AndReportsReason()
    {
        var whisper = new FakeWhisperService { Outcome = Failed(TranscriptionFailureCodes.Transient) };
        var h = NewHarness(whisper, new FakeSilenceService { FfmpegAvailable = false });
        var project = h.CreateProject();
        h.AddMedia(project, "1-Bruto/a.mp4", metadata: Metadata(2));

        await h.Manager.StartAsync(Start(project.ProjectId, stages: TranscriptionOnly));
        var status = await h.WaitForFinishAsync(project.ProjectId);

        Assert.Equal("failed", status.State);
        Assert.Equal(2, whisper.TranscribeCalls);
        Assert.Contains("falha de teste", status.Media.Single().Message);
    }

    [Fact]
    public async Task P12_NonTransientFailure_IsNotRetried()
    {
        var whisper = new FakeWhisperService { Outcome = Failed(null) };
        var h = NewHarness(whisper, new FakeSilenceService { FfmpegAvailable = false });
        var project = h.CreateProject();
        h.AddMedia(project, "1-Bruto/a.mp4", metadata: Metadata(2));

        await h.Manager.StartAsync(Start(project.ProjectId, stages: TranscriptionOnly));
        await h.WaitForFinishAsync(project.ProjectId);

        Assert.Equal(1, whisper.TranscribeCalls);
    }

    [Fact]
    public async Task P12_CudaOutOfMemory_FallsBackToCpu_WithSameModel()
    {
        var whisper = new FakeWhisperService();
        whisper.ScriptedOutcomes.Enqueue(Failed(TranscriptionFailureCodes.CudaOutOfMemory));
        whisper.ScriptedOutcomes.Enqueue(Completed());
        var h = NewHarness(whisper, new FakeSilenceService { FfmpegAvailable = false });
        var project = h.CreateProject();
        h.AddMedia(project, "1-Bruto/a.mp4", metadata: Metadata(2));

        await h.Manager.StartAsync(Start(project.ProjectId, stages: TranscriptionOnly));
        var status = await h.WaitForFinishAsync(project.ProjectId);

        Assert.Equal("completed", status.State);
        Assert.Equal(new[] { "cpu" }, whisper.DeviceOverrides);
        Assert.Single(h.Db.Transcripts);
    }

    [Fact]
    public async Task P12_RetryFailed_ReenqueuesOnlyFailedMedia()
    {
        var whisper = new FakeWhisperService { Outcome = Completed() };
        bool failB = true;
        whisper.OutcomeByPath = path => failB && path.EndsWith("b.mp4", StringComparison.OrdinalIgnoreCase) ? Failed(null) : null;
        var h = NewHarness(whisper, new FakeSilenceService { FfmpegAvailable = false });
        var project = h.CreateProject();
        h.AddMedia(project, "1-Bruto/a.mp4", metadata: Metadata(2));
        var bad = h.AddMedia(project, "1-Bruto/b.mp4", metadata: Metadata(2));

        await h.Manager.StartAsync(Start(project.ProjectId, stages: TranscriptionOnly));
        var first = await h.WaitForFinishAsync(project.ProjectId);
        Assert.Equal("completed_with_issues", first.State);
        Assert.Equal(2, whisper.TranscribeCalls);

        failB = false;
        var result = await h.Manager.RetryFailedAsync(project.ProjectId);
        var status = await h.WaitForFinishAsync(project.ProjectId);

        Assert.Equal(PipelineEnqueueResult.Started, result.Outcome);
        Assert.Equal("completed", status.State);
        Assert.Equal(bad.MediaId, Assert.Single(status.Media).MediaId);
        Assert.Equal(3, whisper.TranscribeCalls);
    }

    // ---------------------------------------------------------------- P15

    [Fact]
    public async Task P15_FinishedJobs_AreEvicted_KeepingLatestOfProject()
    {
        var h = NewHarness(new FakeWhisperService { Outcome = Completed() }, new FakeSilenceService { FfmpegAvailable = false });
        var project = h.CreateProject();
        h.AddMedia(project, "1-Bruto/a.mp4", metadata: Metadata(2));

        Guid last = Guid.Empty;
        for (int i = 0; i < PipelineJobManager.MaxRetainedFinishedJobs + 6; i++)
        {
            last = await h.Manager.StartAsync(Start(project.ProjectId, stages: TranscriptionOnly));
            await h.WaitForFinishAsync(project.ProjectId);
            // Aguarda o slot liberar antes do próximo start (o estado final aparece depois da liberação, mas o finally ainda roda).
            await Task.Delay(15);
        }

        Assert.True(h.Manager.TrackedJobCount <= PipelineJobManager.MaxRetainedFinishedJobs + 2, $"jobs retidos: {h.Manager.TrackedJobCount}");
        var status = await h.Manager.GetStatusAsync(project.ProjectId);
        Assert.Equal(last, status.JobId);
    }

    // ---------------------------------------------------------------- P09

    [Fact]
    public void P09_Validator_RejectsZeroDuration_EvenWithoutWords()
    {
        var result = TranscriptValidator.Validate(new WhisperTranscriptPayload("", 0, Array.Empty<WhisperRawWord>()));
        Assert.False(result.IsValid);
        Assert.True(TranscriptValidator.Validate(new WhisperTranscriptPayload("", 12.0, Array.Empty<WhisperRawWord>())).IsValid);
    }

    [Theory]
    [InlineData(10.0, 10.5, false)]
    [InlineData(100.0, 104.0, false)]
    [InlineData(100.0, 90.0, true)]
    [InlineData(10.0, 3.0, true)]
    [InlineData(0.0, 3.0, false)] // duração da mídia desconhecida: sem comparação
    public void P09_DurationMismatch_UsesAbsoluteAndRelativeTolerance(double media, double transcript, bool mismatch)
    {
        Assert.Equal(mismatch, TranscriptValidator.CheckDurationMismatch(transcript, media) != null);
    }

    [Fact]
    public async Task P09_TruncatedTranscript_IsCompletedWithIssues_NotCachedNorPersisted()
    {
        var whisper = new FakeWhisperService { Outcome = Completed(duration: 3.0) };
        var h = NewHarness(whisper, new FakeSilenceService { FfmpegAvailable = false });
        var project = h.CreateProject();
        h.AddMedia(project, "1-Bruto/a.mp4", metadata: Metadata(2)); // metadados: 10 s

        var stages = new List<string> { "transcription" };
        await h.Manager.StartAsync(Start(project.ProjectId, stages: stages));
        var status = await h.WaitForFinishAsync(project.ProjectId);

        Assert.Equal("completed_with_issues", status.State);
        Assert.Contains("diverge", status.Media.Single().Message);
        Assert.Empty(h.Db.Transcripts);

        // Sem cache: a segunda execução transcreve de novo.
        await h.Manager.StartAsync(Start(project.ProjectId, stages: stages));
        await h.WaitForFinishAsync(project.ProjectId);
        Assert.Equal(2, whisper.TranscribeCalls);
    }

    [Fact]
    public async Task P09_ValidSilentTranscript_RecordsHasSpeechFalseExplicitly()
    {
        var empty = new TranscriptionOutcome("completed",
            new TranscriptDto(Guid.NewGuid(), Guid.Empty, "large-v3", "pt", "", 10.0, new List<TranscriptWordDto>()),
            null, "fake-whisper 1.0", "large-v3");
        var h = NewHarness(new FakeWhisperService { Outcome = empty }, new FakeSilenceService { FfmpegAvailable = false });
        var project = h.CreateProject();
        var media = h.AddMedia(project, "1-Bruto/a.mp4", metadata: Metadata(2));

        await h.Manager.StartAsync(Start(project.ProjectId, stages: new List<string> { "transcription", "scene" }));
        await h.WaitForFinishAsync(project.ProjectId);

        var analysis = await h.Manager.GetMediaAnalysisAsync(project.ProjectId, media.MediaId);
        Assert.Equal("completed", analysis!.TranscriptionStatus);
        Assert.False(analysis.Scene!.HasSpeech);
    }

    // ---------------------------------------------------------------- P08

    [Fact]
    public async Task P08_MediaWithoutAudioTrack_IsSkipped_WithoutStartingWhisperOrSilenceDetection()
    {
        var whisper = new FakeWhisperService { Outcome = Completed() };
        var silence = new FakeSilenceService { Outcome = new SilenceDetectionOutcome("completed", new List<VadSilenceSegmentDto>(), null) };
        var h = NewHarness(whisper, silence);
        var project = h.CreateProject();
        var media = h.AddMedia(project, "1-Bruto/mudo.mp4", metadata: Metadata(audioChannels: 0));

        await h.Manager.StartAsync(Start(project.ProjectId));
        var status = await h.WaitForFinishAsync(project.ProjectId);

        Assert.Equal(0, whisper.TranscribeCalls);
        Assert.Equal(0, silence.DetectCalls);
        var m = Assert.Single(status.Media);
        Assert.Equal("skipped", m.StageStates["transcription"]);
        Assert.Equal("skipped", m.StageStates["silence"]);
        Assert.Contains("sem áudio", m.Message);
        Assert.DoesNotContain(status.Media, x => x.StageStates["transcription"] == "failed");

        var analysis = await h.Manager.GetMediaAnalysisAsync(project.ProjectId, media.MediaId);
        Assert.Equal("skipped", analysis!.TranscriptionStatus);
        Assert.Equal("sem áudio", analysis.TranscriptionReason);
    }

    [Fact]
    public async Task P08_MediaWithUnreliableMetadata_StillTranscribes()
    {
        var whisper = new FakeWhisperService { Outcome = Completed() };
        var h = NewHarness(whisper, new FakeSilenceService { FfmpegAvailable = false });
        var project = h.CreateProject();
        // ffprobe falhou: AudioChannels=0 é "desconhecido", não "sem áudio".
        h.AddMedia(project, "1-Bruto/a.mp4", metadata: Metadata(0, MediaMetadataStatus.Failed));

        await h.Manager.StartAsync(Start(project.ProjectId, stages: new List<string> { "transcription" }));
        await h.WaitForFinishAsync(project.ProjectId);

        Assert.Equal(1, whisper.TranscribeCalls);
    }
}
