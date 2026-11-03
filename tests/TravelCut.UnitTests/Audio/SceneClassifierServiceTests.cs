using Microsoft.Extensions.Logging.Abstractions;
using TravelCut.Application.Audio;
using TravelCut.Contracts.Dtos;
using TravelCut.Infrastructure.Audio;
using Xunit;

namespace TravelCut.UnitTests.Audio;

public class SceneClassifierServiceTests
{
    private readonly SceneClassifierService _service = new(NullLogger<SceneClassifierService>.Instance);

    private static MediaDetailsDto Details(string? cameraModel = null, double duration = 30.0, double frameRate = 29.97) => new(
        MediaId: Guid.NewGuid(),
        ProjectId: Guid.NewGuid(),
        RelativePath: "1-Bruto/clip.mp4",
        FullPath: @"C:\Projeto\1-Bruto\clip.mp4",
        ByteLength: 10_000_000,
        Kind: "video",
        Fingerprint: "fp-test",
        LastWriteUtc: DateTime.UtcNow,
        Codec: "h264",
        Width: 3840,
        Height: 2160,
        FrameRate: frameRate,
        TimeBase: "1/30000",
        DurationSeconds: duration,
        DurationTicks: (long)(duration * 10_000_000),
        AudioChannels: 2,
        AudioSampleRate: 48000,
        CreationTimeUtc: DateTime.UtcNow.AddMinutes(-10),
        SourceDateEvidence: "quicktime.creationdate",
        IsVariableFrameRate: false,
        CameraModel: cameraModel);

    private static TranscriptDto Transcript(Guid mediaId, double audioDuration, string fullText, params (double Start, double End, string Text)[] words) => new(
        TranscriptId: Guid.NewGuid(),
        MediaId: mediaId,
        ModelId: "large-v3",
        Language: "pt",
        FullText: fullText,
        AudioDurationSeconds: audioDuration,
        Words: words.Select((w, i) => new TranscriptWordDto(i, w.Start, w.End, w.Text, 0.9)).ToList());

    [Fact]
    public void DroneCameraMetadata_IsMediumHypothesis_NotConfirmedScene()
    {
        var details = Details(cameraModel: "DJI Mini 3");

        var result = _service.ClassifyScene(details, null, null);

        Assert.Equal("drone", result.Category);
        Assert.Equal("medium", result.ConfidenceBand);
        Assert.True(result.IsHypothesis);
        Assert.Contains("metadado: câmera 'DJI Mini 3'", result.Evidence);
        Assert.Null(result.HasSpeech);
    }

    [Fact]
    public void NoEvidence_IsIndeterminadoLowHypothesis_EvenWithHighResolutionAnd60Fps()
    {
        var details = Details(cameraModel: null, frameRate: 59.94);

        var result = _service.ClassifyScene(details, null, null);

        Assert.Equal("indeterminado", result.Category);
        Assert.Equal("low", result.ConfidenceBand);
        Assert.True(result.IsHypothesis);
        Assert.Null(result.HasSpeech);
        Assert.Contains(result.Evidence, e => e.Contains("sem evidência suficiente"));
    }

    [Fact]
    public void DenseSpeechInTranscript_IsConversa_FromMeasuredEvidence()
    {
        var details = Details(duration: 30.0);
        var words = Enumerable.Range(0, 40).Select(i => (i * 0.5, i * 0.5 + 0.5, "palavra")).ToArray();
        var transcript = Transcript(details.MediaId, 30.0, "vamos explicar a viagem", words);

        var result = _service.ClassifyScene(details, transcript, new List<VadSilenceSegmentDto>());

        Assert.Equal("conversa", result.Category);
        Assert.False(result.IsHypothesis);
        Assert.True(result.HasSpeech);
        Assert.Contains("fala em 67% da duração (transcrição)", result.Evidence);
    }

    [Fact]
    public void KeywordWithLittleSpeech_IsWeakEvidenceOnly()
    {
        var details = Details(duration: 30.0);
        var transcript = Transcript(details.MediaId, 30.0, "cachoeira linda", (0.0, 0.5, "cachoeira"));

        var result = _service.ClassifyScene(details, transcript, null);

        Assert.Equal("paisagem", result.Category);
        Assert.Equal("low", result.ConfidenceBand);
        Assert.True(result.IsHypothesis);
        Assert.Contains(result.Evidence, e => e.Contains("palavra-chave 'cachoeira'") && e.Contains("evidência fraca"));
        Assert.False(result.HasSpeech);
    }

    [Fact]
    public void KeywordsDoNotOverrideMeasuredConversation()
    {
        var details = Details(duration: 30.0);
        var words = Enumerable.Range(0, 40).Select(i => (i * 0.5, i * 0.5 + 0.5, "palavra")).ToArray();
        var transcript = Transcript(details.MediaId, 30.0, "olha a loja e o mercado", words);

        var result = _service.ClassifyScene(details, transcript, null);

        Assert.Equal("conversa", result.Category);
        Assert.Contains(result.Evidence, e => e.Contains("palavra-chave 'loja'"));
    }

    [Fact]
    public void SilenceCoveringMostOfMedia_WithoutTranscript_MeansNoSpeech()
    {
        var details = Details(duration: 30.0);
        var silences = new List<VadSilenceSegmentDto>
        {
            new(StartSeconds: 0.0, EndSeconds: 28.0, DurationSeconds: 28.0, NoiseDb: -30.0, Confidence: "high")
        };

        var result = _service.ClassifyScene(details, null, silences);

        Assert.Equal("indeterminado", result.Category);
        Assert.False(result.HasSpeech);
        Assert.Contains(result.Evidence, e => e.Contains("silêncio em 93% da duração"));
    }

    [Fact]
    public void NoTranscriptAndNoSilences_LeavesSpeechUnknown()
    {
        var result = _service.ClassifyScene(Details(), null, null);

        Assert.Null(result.HasSpeech);
        Assert.Contains(result.Evidence, e => e.Contains("sem transcrição nem silêncios"));
    }
}
