using Microsoft.Extensions.Logging.Abstractions;
using TravelCut.Contracts.Dtos;
using TravelCut.Infrastructure.Audio;
using Xunit;

namespace TravelCut.UnitTests.Audio;

public class SceneClassifierSpeechTests
{
    private readonly SceneClassifierService _service = new(NullLogger<SceneClassifierService>.Instance);

    private static MediaDetailsDto Details(double duration) => new(
        MediaId: Guid.NewGuid(),
        ProjectId: Guid.NewGuid(),
        RelativePath: "1-Bruto/clip.mp4",
        FullPath: @"C:\Projeto\1-Bruto\clip.mp4",
        ByteLength: 1_000,
        Kind: "video",
        Fingerprint: "fp",
        LastWriteUtc: DateTime.UtcNow,
        Codec: "h264",
        Width: 1920,
        Height: 1080,
        FrameRate: 30,
        TimeBase: "1/30",
        DurationSeconds: duration,
        DurationTicks: 0,
        AudioChannels: 2,
        AudioSampleRate: 48000,
        CreationTimeUtc: null,
        SourceDateEvidence: string.Empty,
        IsVariableFrameRate: false,
        CameraModel: null);

    private static TranscriptDto Transcript(double audioDuration, params TranscriptWordDto[] words) => new(
        TranscriptId: Guid.NewGuid(),
        MediaId: Guid.NewGuid(),
        ModelId: "large-v3",
        Language: "pt",
        FullText: string.Join(" ", words.Select(w => w.Text)),
        AudioDurationSeconds: audioDuration,
        Words: words.ToList());

    [Fact]
    public void UnknownDuration_WithWords_LeavesHasSpeechNull_NotAssumedTrue()
    {
        // Duração 0 e sem duração na transcrição: a proporção de fala não pode ser calculada.
        var transcript = Transcript(0.0, new TranscriptWordDto(0, 0.5, 1.0, "olá", 0.9));

        var result = _service.ClassifyScene(Details(0.0), transcript, null);

        Assert.Null(result.HasSpeech);
    }

    [Fact]
    public void EmptyTranscript_MeansNoSpeech()
    {
        var transcript = Transcript(10.0);

        var result = _service.ClassifyScene(Details(10.0), transcript, null);

        Assert.False(result.HasSpeech);
    }

    [Fact]
    public void KnownDuration_WithSpeech_SetsHasSpeechTrue()
    {
        var transcript = Transcript(10.0, new TranscriptWordDto(0, 0.0, 4.0, "conversa", 0.9));

        var result = _service.ClassifyScene(Details(10.0), transcript, null);

        Assert.True(result.HasSpeech);
    }
}
