using Microsoft.Extensions.Logging.Abstractions;
using TravelCut.Application.Audio;
using TravelCut.Contracts.Dtos;
using TravelCut.Infrastructure.Audio;
using Xunit;

namespace TravelCut.UnitTests.Audio;

public class CutCandidateServiceTests
{
    private readonly CutCandidateService _service = new(NullLogger<CutCandidateService>.Instance);
    private readonly Guid _mediaId = Guid.NewGuid();

    private TranscriptDto Transcript(params TranscriptWordDto[] words) => new(
        TranscriptId: Guid.NewGuid(),
        MediaId: _mediaId,
        ModelId: "large-v3",
        Language: "pt",
        FullText: string.Join(" ", words.Select(w => w.Text)),
        AudioDurationSeconds: 20.0,
        Words: words.ToList());

    private static TranscriptWordDto W(int seq, double start, double end, string text) =>
        new(seq, start, end, text, 0.9);

    private static VadSilenceSegmentDto Silence(double start, double end, string confidence = "high") =>
        new(StartSeconds: start, EndSeconds: end, DurationSeconds: end - start, NoiseDb: -30.0, Confidence: confidence);

    private static HesitationCandidateDto Hesitation(double start, double end, string word, string type, string band = "high") =>
        new(
            StartSeconds: start,
            EndSeconds: end,
            DetectedWord: word,
            Type: type,
            ConfidenceBand: band,
            ContextBefore: "eu acho que",
            ContextAfter: "vai dar certo",
            NeedsHumanReview: true);

    [Fact]
    public void Silence_WithoutSpeechWithinHalfSecondOnEitherSide_IsAutoCut()
    {
        // Fala ausente em ±0,5 s dos dois lados, duração 2,4 s, confiança alta, sem risada nem pergunta.
        var transcript = Transcript(W(0, 0.0, 0.3, "cachoeira"), W(1, 4.5, 4.8, "olha"));

        var candidates = _service.GenerateCutCandidates(_mediaId, transcript, new() { Silence(1.0, 3.4) }, new());

        var c = Assert.Single(candidates);
        Assert.Equal("silence", c.Kind);
        Assert.Equal("vad-analyzer", c.Author);
        Assert.Equal("cut", c.SuggestedAction);
        Assert.False(c.NeedsHumanReview);
        Assert.Equal("high", c.ConfidenceBand);
        Assert.Equal("Silêncio de 2,4 s (silencedetect −30 dB) entre 'cachoeira' e 'olha'.", c.Rationale);
    }

    [Fact]
    public void Silence_WithWordsWithinHalfSecondOfSilence_NeedsHumanReview()
    {
        // "olha" começa em 3,4 s, dentro da janela de 0,5 s após o fim do silêncio (3,4 s): fala próxima.
        var transcript = Transcript(W(0, 0.0, 0.3, "cachoeira"), W(1, 3.5, 3.8, "olha"));

        var candidates = _service.GenerateCutCandidates(_mediaId, transcript, new() { Silence(1.0, 3.4) }, new());

        var c = Assert.Single(candidates);
        Assert.True(c.NeedsHumanReview);
        Assert.Equal("cut", c.SuggestedAction);
    }

    [Fact]
    public void Silence_WordEndingWithinHalfSecondBeforeSilence_NeedsHumanReview()
    {
        // Palavra termina em 0,6 s; silêncio começa em 1,0 s: está a 0,4 s (dentro de ±0,5 s).
        var transcript = Transcript(W(0, 0.0, 0.6, "fim"), W(1, 4.5, 4.8, "olha"));

        var c = Assert.Single(_service.GenerateCutCandidates(_mediaId, transcript, new() { Silence(1.0, 3.4) }, new()));

        Assert.True(c.NeedsHumanReview);
    }

    [Fact]
    public void Silence_ShorterThanTwoSeconds_IsNotAutoCut()
    {
        var transcript = Transcript(W(0, 0.0, 0.3, "cachoeira"), W(1, 4.5, 4.8, "olha"));

        var c = Assert.Single(_service.GenerateCutCandidates(_mediaId, transcript, new() { Silence(1.0, 2.9) }, new()));

        Assert.True(c.NeedsHumanReview);
        Assert.Equal("trim", c.SuggestedAction);
    }

    [Fact]
    public void Silence_PrecededByQuestion_IsNotAutoCut()
    {
        var transcript = Transcript(W(0, 0.0, 0.3, "certo?"), W(1, 4.5, 4.8, "vamos"));

        var c = Assert.Single(_service.GenerateCutCandidates(_mediaId, transcript, new() { Silence(1.0, 3.4) }, new()));

        Assert.True(c.NeedsHumanReview);
        Assert.Equal("review", c.SuggestedAction);
    }

    [Fact]
    public void Silence_PrecededByExclamation_IsNotAutoCut_EvenWhenHumorPreservationIsOff()
    {
        // Sem preservação de pausas expressivas a ação não vira "review", mas o corte automático continua bloqueado.
        var transcript = Transcript(W(0, 0.0, 0.3, "uau!"), W(1, 4.5, 4.8, "vamos"));
        var options = new CutCandidateGenerationOptions(PreserveHumorousPauses: false);

        var c = Assert.Single(_service.GenerateCutCandidates(_mediaId, transcript, new() { Silence(1.0, 3.4) }, new(), options));

        Assert.True(c.NeedsHumanReview);
        Assert.Equal("cut", c.SuggestedAction);
    }

    [Fact]
    public void Silence_WithLaughterWithinWindow_IsNotAutoCut()
    {
        // Sem fala na janela de 0,5 s, mas risada a 1,5 s do silêncio.
        var transcript = Transcript(W(0, 0.0, 0.3, "boa"), W(1, 4.5, 4.8, "kkk"));

        var c = Assert.Single(_service.GenerateCutCandidates(_mediaId, transcript, new() { Silence(1.0, 3.4) }, new()));

        Assert.True(c.NeedsHumanReview);
        Assert.Equal("review", c.SuggestedAction);
    }

    [Fact]
    public void Silence_WithMediumConfidence_IsNotAutoCut()
    {
        var transcript = Transcript(W(0, 0.0, 0.3, "cachoeira"), W(1, 4.5, 4.8, "olha"));

        var c = Assert.Single(_service.GenerateCutCandidates(_mediaId, transcript, new() { Silence(1.0, 3.4, "medium") }, new()));

        Assert.True(c.NeedsHumanReview);
    }

    [Fact]
    public void Silence_WithoutOneSideContext_IsNotAutoCut()
    {
        // Nenhuma palavra antes do silêncio: o contexto do lado anterior não existe, então revisão humana.
        var transcript = Transcript(W(0, 4.5, 4.8, "olha"));

        var c = Assert.Single(_service.GenerateCutCandidates(_mediaId, transcript, new() { Silence(1.0, 3.4) }, new()));

        Assert.True(c.NeedsHumanReview);
    }

    [Fact]
    public void Silence_WithoutTranscript_IsCappedToMediumAndNeedsReview()
    {
        var candidates = _service.GenerateCutCandidates(_mediaId, null, new() { Silence(1.0, 3.4, "high") }, new());

        var c = Assert.Single(candidates);
        Assert.Equal("medium", c.ConfidenceBand);
        Assert.True(c.NeedsHumanReview);
        Assert.Contains("sem transcrição para contexto", c.Rationale);
    }

    [Fact]
    public void Laughter_WithinOneAndHalfSecondsOfSilence_ForcesReviewNotCut()
    {
        var transcript = Transcript(
            W(0, 0.5, 1.0, "boa"),
            W(1, 3.6, 3.9, "kkk"),
            W(2, 4.0, 4.4, "olha"));

        var candidates = _service.GenerateCutCandidates(_mediaId, transcript, new() { Silence(1.0, 3.4) }, new());

        var c = Assert.Single(candidates);
        Assert.Equal("review", c.SuggestedAction);
        Assert.True(c.NeedsHumanReview);
        Assert.Contains("Risada", c.Rationale);
    }

    [Fact]
    public void LaughterFarAwayFromSilence_DoesNotChangeAction()
    {
        var transcript = Transcript(
            W(0, 0.5, 1.0, "boa"),
            W(1, 3.4, 3.8, "olha"),
            W(2, 9.0, 9.4, "haha"));

        var candidates = _service.GenerateCutCandidates(_mediaId, transcript, new() { Silence(1.0, 3.4) }, new());

        Assert.Equal("cut", Assert.Single(candidates).SuggestedAction);
    }

    [Fact]
    public void ShortPauseAfterQuestion_IsExpressive_ReviewNotCut()
    {
        var transcript = Transcript(
            W(0, 0.2, 0.9, "certo?"),
            W(1, 1.9, 2.3, "vamos"));

        var candidates = _service.GenerateCutCandidates(_mediaId, transcript, new() { Silence(0.9, 1.9) }, new());

        var c = Assert.Single(candidates);
        Assert.Equal("pause", c.Kind);
        Assert.Equal("review", c.SuggestedAction);
        Assert.True(c.NeedsHumanReview);
        Assert.Contains("possível pausa expressiva", c.Rationale);
    }

    [Fact]
    public void SilenceBelowThreshold_ProducesNoCandidate()
    {
        var candidates = _service.GenerateCutCandidates(_mediaId, null, new() { Silence(1.0, 1.5) }, new());

        Assert.Empty(candidates);
    }

    [Fact]
    public void NoSilenceAndNoHesitation_ProducesNoCandidate()
    {
        var candidates = _service.GenerateCutCandidates(_mediaId, Transcript(W(0, 0, 1, "oi")), new(), new());

        Assert.Empty(candidates);
    }

    [Fact]
    public void Hesitation_UsesHesitationAuthor_AlwaysNeedsReview_AndCitesContext()
    {
        var candidates = _service.GenerateCutCandidates(
            _mediaId,
            null,
            new(),
            new() { Hesitation(3.0, 3.5, "vamos", "repetition") });

        var c = Assert.Single(candidates);
        Assert.Equal("repetition", c.Kind);
        Assert.Equal("hesitation-analyzer", c.Author);
        Assert.True(c.NeedsHumanReview);
        Assert.Equal("cut", c.SuggestedAction);
        Assert.Contains("Repetição de palavra 'vamos'", c.Rationale);
        Assert.Contains("entre 'eu acho que' e 'vai dar certo'", c.Rationale);
    }

    [Fact]
    public void Hesitation_WithLaughterNearby_IsReview()
    {
        var transcript = Transcript(W(0, 3.2, 3.5, "haha"));

        var candidates = _service.GenerateCutCandidates(
            _mediaId,
            transcript,
            new(),
            new() { Hesitation(3.0, 3.5, "tipo", "filler") });

        Assert.Equal("review", Assert.Single(candidates).SuggestedAction);
    }

    [Fact]
    public void Candidates_AreOrderedChronologically()
    {
        var candidates = _service.GenerateCutCandidates(
            _mediaId,
            null,
            new() { Silence(10.0, 12.5), Silence(2.0, 4.5) },
            new() { Hesitation(5.0, 5.5, "aí", "repetition") });

        Assert.Equal(new[] { 2.0, 5.0, 10.0 }, candidates.Select(c => c.StartSeconds).ToArray());
    }
}
