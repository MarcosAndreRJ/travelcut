using TravelCut.Infrastructure.Audio;
using Xunit;

namespace TravelCut.UnitTests.Audio;

public class WhisperTranscriptValidatorTests
{
    private static WhisperRawWord W(string text, double start, double end, double p = 0.9) => new(text, start, end, p);

    private static WhisperTranscriptPayload Payload(double duration, params WhisperRawWord[] words) =>
        new("texto de teste", duration, words);

    [Fact]
    public void ValidPayload_IsAccepted_WithSequentialSeqAndProbabilityAsConfidence()
    {
        var result = TranscriptValidator.Validate(Payload(10.0,
            W("vamos", 0.1234, 0.5), W("pegar", 0.6, 0.9, 0.5), W("agora", 1.0, 1.4, 0.99)));

        Assert.True(result.IsValid);
        Assert.Null(result.Error);
        Assert.False(result.JitterCorrected);
        Assert.Equal(new[] { 1, 2, 3 }, result.Words.Select(w => w.Seq).ToArray());
        Assert.Equal(0.123, result.Words[0].StartSeconds);
        Assert.Equal(0.5, result.Words[1].Confidence);
    }

    [Fact]
    public void JitterWithinTolerance_IsAccepted_AndSorted()
    {
        var result = TranscriptValidator.Validate(Payload(10.0,
            W("a", 1.00, 1.2), W("b", 0.98, 1.1), W("c", 2.0, 2.3)));

        Assert.True(result.IsValid);
        Assert.True(result.JitterCorrected);
        Assert.Equal(new[] { "b", "a", "c" }, result.Words.Select(w => w.Text).ToArray());
    }

    [Fact]
    public void BackwardStartBeyondTolerance_IsRejected()
    {
        var result = TranscriptValidator.Validate(Payload(10.0,
            W("a", 2.0, 2.2), W("b", 1.5, 1.8)));

        Assert.False(result.IsValid);
        Assert.Contains("monotônico", result.Error);
        Assert.Empty(result.Words);
    }

    [Fact]
    public void StartGreaterThanEnd_IsRejected()
    {
        var result = TranscriptValidator.Validate(Payload(10.0, W("a", 2.0, 1.9)));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void WordBeyondDurationPlusHalfSecond_IsRejected_ButToleratedWithinHalfSecond()
    {
        Assert.False(TranscriptValidator.Validate(Payload(10.0, W("a", 9.0, 10.6))).IsValid);
        Assert.True(TranscriptValidator.Validate(Payload(10.0, W("a", 9.0, 10.4))).IsValid);
    }

    [Fact]
    public void NegativeStart_IsRejected()
    {
        Assert.False(TranscriptValidator.Validate(Payload(10.0, W("a", -0.5, 0.2))).IsValid);
    }

    [Fact]
    public void ProbabilityOutOfRange_IsRejected()
    {
        Assert.False(TranscriptValidator.Validate(Payload(10.0, W("a", 0.0, 0.2, 1.2))).IsValid);
        Assert.False(TranscriptValidator.Validate(Payload(10.0, W("a", 0.0, 0.2, -0.1))).IsValid);
    }

    [Fact]
    public void NonFiniteTimestamp_IsRejected()
    {
        Assert.False(TranscriptValidator.Validate(Payload(10.0, W("a", double.NaN, 0.2))).IsValid);
        Assert.False(TranscriptValidator.Validate(Payload(double.PositiveInfinity, W("a", 0.0, 0.2))).IsValid);
    }

    [Fact]
    public void WordWithoutText_IsRejected()
    {
        Assert.False(TranscriptValidator.Validate(Payload(10.0, W("   ", 0.0, 0.2))).IsValid);
    }

    [Fact]
    public void EmptyTranscriptWithValidAudio_IsValid_WithZeroWords()
    {
        var result = TranscriptValidator.Validate(new WhisperTranscriptPayload("", 7.0, Array.Empty<WhisperRawWord>()));

        Assert.True(result.IsValid);
        Assert.Empty(result.Words);
    }

    [Fact]
    public void TextWithoutTimedWords_IsRejected()
    {
        // Texto sem palavras com timestamps é falha de reconhecimento, não silêncio.
        var result = TranscriptValidator.Validate(new WhisperTranscriptPayload("olá", 7.0, Array.Empty<WhisperRawWord>()));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void WordsWithEmptyFullText_AreRejected()
    {
        var result = TranscriptValidator.Validate(new WhisperTranscriptPayload("  ", 7.0, new[] { W("a", 0.0, 0.2) }));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void NegativeOrNonFiniteDuration_IsRejected()
    {
        Assert.False(TranscriptValidator.Validate(new WhisperTranscriptPayload("x", -1.0, Array.Empty<WhisperRawWord>())).IsValid);
        Assert.False(TranscriptValidator.Validate(new WhisperTranscriptPayload("x", double.NaN, Array.Empty<WhisperRawWord>())).IsValid);
    }

    [Fact]
    public void ValidateEnvelope_ChecksProtocolAndVersion()
    {
        Assert.Null(TranscriptValidator.ValidateEnvelope("travelcut.whisper", 1));
        Assert.NotNull(TranscriptValidator.ValidateEnvelope("outro", 1));
        Assert.NotNull(TranscriptValidator.ValidateEnvelope("travelcut.whisper", 2));
        Assert.NotNull(TranscriptValidator.ValidateEnvelope(null, null));
    }
}
