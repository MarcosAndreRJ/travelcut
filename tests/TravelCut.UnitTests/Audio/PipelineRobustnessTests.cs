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
