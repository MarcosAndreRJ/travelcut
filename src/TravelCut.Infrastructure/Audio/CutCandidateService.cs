using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using TravelCut.Application.Audio;
using TravelCut.Contracts.Dtos;

namespace TravelCut.Infrastructure.Audio;

/// <summary>
/// Candidatos a corte a partir de evidências reais (silêncios do silencedetect e hesitações lexicais).
/// Regras:
/// - Nunca gera candidato sem silêncio/hesitação real.
/// - Rationale sempre cita a evidência (duração, limiar, palavras vizinhas).
/// - Risada ("kkk", "haha", "risos") a até 1,5 s ou pausa curta após pergunta/exclamação → SuggestedAction "review", nunca "cut".
/// - Hesitações e repetições sempre exigem revisão humana.
/// - Corte automático (NeedsHumanReview=false) de silêncio só quando todas as condições valem (ver BuildSilenceCandidate); o resto exige revisão.
/// </summary>
public class CutCandidateService : ICutCandidateService
{
    public const string SilenceAuthor = "vad-analyzer";
    public const string HesitationAuthor = "hesitation-analyzer";
    public const double LaughterWindowSeconds = 1.5;

    /// <summary>Corte automático de silêncio exige duração mínima de 2 s.</summary>
    public const double AutoCutMinDurationSeconds = 2.0;

    /// <summary>Margem em que não pode haver palavra (fala) antes ou depois do silêncio para corte automático.</summary>
    public const double AutoCutSpeechMarginSeconds = 0.5;

    private const double BoundaryToleranceSeconds = 0.1;
    private const string SilenceToolLabel = "silencedetect";

    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");

    // Token normalizado (sem pontuação, minúsculo) que representa risada.
    private static readonly Regex LaughterToken = new(
        @"^(k{2,}|(ha){2,}|(he){2,}|(rs){2,}|risos?|risadas?|\[risos?\]|\(risos?\)|\[risada\])$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex NonTokenChars = new(@"[^\p{L}\[\]\(\)]", RegexOptions.Compiled);

    private readonly ILogger<CutCandidateService> _logger;

    public CutCandidateService(ILogger<CutCandidateService> logger)
    {
        _logger = logger;
    }

    public List<EditSuggestionDto> GenerateCutCandidates(
        Guid mediaId,
        TranscriptDto? transcript,
        List<VadSilenceSegmentDto> silences,
        List<HesitationCandidateDto> hesitations,
        CutCandidateGenerationOptions? options = null)
    {
        var opts = options ?? new CutCandidateGenerationOptions();
        var words = transcript == null
            ? new List<TranscriptWordDto>()
            : transcript.Words.Where(w => w.EndSeconds >= w.StartSeconds).OrderBy(w => w.StartSeconds).ToList();
        var laughters = words.Where(w => IsLaughter(w.Text)).ToList();

        var result = new List<EditSuggestionDto>();

        foreach (var silence in silences)
        {
            var candidate = BuildSilenceCandidate(mediaId, silence, transcript != null, words, laughters, opts);
            if (candidate != null) result.Add(candidate);
        }

        if (opts.IncludeHesitations)
        {
            foreach (var hesitation in hesitations)
            {
                var candidate = BuildHesitationCandidate(mediaId, hesitation, laughters);
                if (candidate != null) result.Add(candidate);
            }
        }

        _logger.LogDebug("Candidatos a corte gerados para mídia {MediaId}: {Count}", mediaId, result.Count);

        return result.OrderBy(c => c.StartSeconds).ThenBy(c => c.EndSeconds).ToList();
    }

    private static EditSuggestionDto? BuildSilenceCandidate(
        Guid mediaId,
        VadSilenceSegmentDto silence,
        bool hasTranscript,
        List<TranscriptWordDto> words,
        List<TranscriptWordDto> laughters,
        CutCandidateGenerationOptions opts)
    {
        double duration = silence.EndSeconds - silence.StartSeconds;
        if (duration <= 0 || duration < opts.ShortSilenceThresholdSeconds) return null;

        var previous = words.Where(w => w.EndSeconds <= silence.StartSeconds + BoundaryToleranceSeconds).LastOrDefault();
        var next = words.FirstOrDefault(w => w.StartSeconds >= silence.EndSeconds - BoundaryToleranceSeconds);

        bool isLong = duration >= opts.LongSilenceThresholdSeconds;
        bool expressive = opts.PreserveHumorousPauses && previous != null && EndsExpressively(previous.Text);
        bool laughter = HasLaughterNear(laughters, silence.StartSeconds, silence.EndSeconds);

        string band = NormalizeBand(silence.Confidence);
        if (!hasTranscript)
        {
            // Sem transcrição não há contexto de fala: a evidência é só acústica.
            band = CapBand(band, "medium");
        }

        // Corte automático (NeedsHumanReview=false, "cut") SOMENTE quando TODAS as condições valem:
        // - transcrição disponível (sem ela não há como confirmar ausência de fala);
        // - nenhuma palavra em ±0,5 s do silêncio (fala nos dois lados ausente);
        // - duração ≥ 2 s;
        // - sem risada a até 1,5 s;
        // - não precedido por palavra terminada em "?" ou "!";
        // - confiança "high" e palavras de contexto dos dois lados.
        bool precededByQuestionOrExclamation = previous != null && EndsExpressively(previous.Text);
        bool speechNearby = hasTranscript && HasSpeechWithin(words, silence.StartSeconds, silence.EndSeconds, AutoCutSpeechMarginSeconds);

        bool autoCut = hasTranscript
            && !speechNearby
            && duration >= AutoCutMinDurationSeconds
            && !laughter
            && !precededByQuestionOrExclamation
            && band == "high"
            && previous != null
            && next != null;

        bool needsReview = !autoCut;

        string action = laughter || expressive ? "review" : isLong ? "cut" : "trim";
        string kind = isLong ? "silence" : "pause";

        var parts = new List<string>
        {
            $"{(isLong ? "Silêncio" : "Pausa")} de {Seconds(duration)} s ({SilenceToolLabel} {Db(silence.NoiseDb)} dB) {Context(hasTranscript, previous, next)}."
        };
        if (expressive)
        {
            parts.Add($"Palavra anterior termina em '{previous!.Text.Trim()}': possível pausa expressiva; não cortar automaticamente.");
        }
        if (laughter)
        {
            parts.Add($"Risada ou 'kkk/haha' a até {Seconds(LaughterWindowSeconds)} s: preservar e revisar.");
        }
        if (!isLong)
        {
            parts.Add("Avaliar se é ritmo dramático ou respiro para encurtar.");
        }

        return new EditSuggestionDto(
            SuggestionId: Guid.NewGuid(),
            MediaId: mediaId,
            StartSeconds: silence.StartSeconds,
            EndSeconds: silence.EndSeconds,
            Kind: kind,
            Rationale: string.Join(" ", parts),
            ConfidenceBand: band,
            NeedsHumanReview: needsReview,
            SuggestedAction: action,
            Status: "proposed",
            Author: SilenceAuthor);
    }

    private static EditSuggestionDto? BuildHesitationCandidate(
        Guid mediaId,
        HesitationCandidateDto hesitation,
        List<TranscriptWordDto> laughters)
    {
        if (hesitation.EndSeconds <= hesitation.StartSeconds || string.IsNullOrWhiteSpace(hesitation.DetectedWord))
        {
            return null;
        }

        string kind = hesitation.Type switch
        {
            "repetition" => "repetition",
            "filler" => "filler",
            "false_start" => "false_start",
            _ => "hesitation"
        };

        string label = kind switch
        {
            "repetition" => "Repetição de palavra",
            "filler" => "Vício de linguagem",
            "false_start" => "Início falso de frase",
            _ => "Hesitação ou prolongamento"
        };

        bool laughter = HasLaughterNear(laughters, hesitation.StartSeconds, hesitation.EndSeconds);

        var rationale = $"{label} '{hesitation.DetectedWord.Trim()}'";
        if (!string.IsNullOrWhiteSpace(hesitation.ContextBefore) || !string.IsNullOrWhiteSpace(hesitation.ContextAfter))
        {
            rationale += $" entre '{hesitation.ContextBefore?.Trim()}' e '{hesitation.ContextAfter?.Trim()}'";
        }
        rationale += " (evidência lexical da transcrição; revisar, nunca remover automaticamente).";
        if (laughter)
        {
            rationale += $" Risada ou 'kkk/haha' a até {Seconds(LaughterWindowSeconds)} s: preservar.";
        }

        return new EditSuggestionDto(
            SuggestionId: Guid.NewGuid(),
            MediaId: mediaId,
            StartSeconds: hesitation.StartSeconds,
            EndSeconds: hesitation.EndSeconds,
            Kind: kind,
            Rationale: rationale,
            ConfidenceBand: NormalizeBand(hesitation.ConfidenceBand),
            NeedsHumanReview: true,
            SuggestedAction: laughter ? "review" : "cut",
            Status: "proposed",
            Author: HesitationAuthor);
    }

    private static string Context(bool hasTranscript, TranscriptWordDto? previous, TranscriptWordDto? next)
    {
        if (!hasTranscript)
        {
            return "sem transcrição para contexto";
        }

        string before = previous == null ? "sem palavra transcrita antes" : $"'{previous.Text.Trim()}'";
        string after = next == null ? "sem palavra transcrita depois" : $"'{next.Text.Trim()}'";
        return $"entre {before} e {after}";
    }

    /// <summary>True se alguma palavra intersecta a janela [start - margin, end + margin] (conservador: em dúvida, há fala).</summary>
    private static bool HasSpeechWithin(List<TranscriptWordDto> words, double start, double end, double margin) =>
        words.Any(w => w.EndSeconds >= start - margin && w.StartSeconds <= end + margin);

    private static bool HasLaughterNear(List<TranscriptWordDto> laughters, double start, double end) =>
        laughters.Any(l => l.EndSeconds >= start - LaughterWindowSeconds && l.StartSeconds <= end + LaughterWindowSeconds);

    private static bool IsLaughter(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var normalized = NonTokenChars.Replace(text.ToLowerInvariant(), string.Empty);
        return LaughterToken.IsMatch(normalized);
    }

    private static bool EndsExpressively(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        char last = text.TrimEnd()[^1];
        return last is '?' or '!';
    }

    private static string NormalizeBand(string? band)
    {
        var value = band?.Trim().ToLowerInvariant();
        return value is "low" or "medium" or "high" ? value : "low";
    }

    private static string CapBand(string band, string max)
    {
        int rank(string b) => b switch { "low" => 0, "medium" => 1, _ => 2 };
        return rank(band) > rank(max) ? max : band;
    }

    private static string Seconds(double value) => value.ToString("0.0", Pt);

    private static string Db(double value)
    {
        string sign = value < 0 ? "−" : string.Empty;
        return sign + Math.Abs(value).ToString("0.#", Pt);
    }
}
