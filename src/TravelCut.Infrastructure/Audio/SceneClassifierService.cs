using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using TravelCut.Application.Audio;
using TravelCut.Contracts.Dtos;

namespace TravelCut.Infrastructure.Audio;

/// <summary>
/// Classificação de cena por evidências reais, sem modelo multimodal e sem números de confiança.
/// Ordem de decisão:
/// 1. Câmera de drone no metadado (ex.: DJI/Mavic/Mini) → "drone", banda medium, hipótese (sem confirmação visual).
/// 2. Fala em proporção relevante da duração (transcrição) → "conversa".
/// 3. Palavra-chave na transcrição → categoria, banda low, hipótese (evidência fraca).
/// 4. Sem evidência → "indeterminado", banda low, hipótese.
/// Resolução e FPS nunca definem a cena. Palavras-chave só entram como evidência fraca.
/// </summary>
public class SceneClassifierService : ISceneClassifierService
{
    public const double ConversationSpeechRatio = 0.3;
    public const double HighConfidenceSpeechRatio = 0.6;
    public const double SpeechPresenceRatio = 0.05;
    public const double SilenceOnlyRatio = 0.9;
    private const int MaxKeywordEvidence = 3;

    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");

    private static readonly Regex DroneCameraPattern = new(
        @"\b(DJI|drone|Mavic|Mini)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex WordPattern = new(@"\p{L}+", RegexOptions.Compiled);

    private static readonly (string Category, string[] Keywords)[] KeywordGroups =
    [
        ("estrada", ["estrada", "rodovia", "pedágio", "asfalto", "volante"]),
        ("loja", ["loja", "mercado", "feira", "vendedor", "artesanato"]),
        ("paisagem", ["cachoeira", "praia", "montanha", "mirante", "lago", "floresta", "trilha"])
    ];

    private readonly ILogger<SceneClassifierService> _logger;

    public SceneClassifierService(ILogger<SceneClassifierService> logger)
    {
        _logger = logger;
    }

    public SceneClassificationDto ClassifyScene(
        MediaDetailsDto mediaDetails,
        TranscriptDto? transcript,
        List<VadSilenceSegmentDto>? silences)
    {
        var evidence = new List<string>();
        double duration = ResolveDuration(mediaDetails, transcript);
        bool? hasSpeech = null;

        // 1. Metadado de câmera
        string? camera = string.IsNullOrWhiteSpace(mediaDetails.CameraModel) ? null : mediaDetails.CameraModel.Trim();
        bool droneCamera = camera != null && DroneCameraPattern.IsMatch(camera);
        if (camera != null)
        {
            evidence.Add($"metadado: câmera '{camera}'");
        }

        // 2. Transcrição: proporção de tempo com fala
        double? speechRatio = null;
        if (transcript != null)
        {
            if (duration > 0)
            {
                speechRatio = Math.Clamp(SpeechSeconds(transcript, duration) / duration, 0.0, 1.0);
                evidence.Add($"fala em {Percent(speechRatio.Value)} da duração (transcrição)");
            }
            else if (transcript.Words.Count == 0)
            {
                evidence.Add("transcrição sem palavras");
            }
            // Sem palavras: não há fala (dado conhecido). Duração desconhecida: não dá para medir a proporção,
            // então hasSpeech fica null — nunca inferido como "há fala".
            if (transcript.Words.Count == 0)
            {
                hasSpeech = false;
            }
            else if (speechRatio is { } measuredRatio)
            {
                hasSpeech = measuredRatio >= SpeechPresenceRatio;
            }
            else
            {
                hasSpeech = null;
            }
        }

        // 3. VAD: silêncio cobre quase toda a mídia
        if (silences != null && duration > 0)
        {
            double silentRatio = Math.Clamp(SilenceSeconds(silences, duration) / duration, 0.0, 1.0);
            evidence.Add($"silêncio em {Percent(silentRatio)} da duração (silencedetect)");
            if (transcript == null)
            {
                hasSpeech = silentRatio >= SilenceOnlyRatio ? false : null;
            }
        }

        if (transcript == null && silences == null)
        {
            evidence.Add("sem transcrição nem silêncios: nenhuma evidência de conteúdo");
        }

        // 4. Palavras-chave da transcrição (evidência fraca, nunca decide sozinha se houver fala)
        var keywordHits = FindKeywordHits(transcript?.FullText);
        foreach (var (group, keyword) in keywordHits.Take(MaxKeywordEvidence))
        {
            evidence.Add($"palavra-chave '{keyword}' na transcrição (evidência fraca: {group})");
        }

        string category;
        string band;
        bool isHypothesis;

        if (droneCamera)
        {
            category = "drone";
            band = "medium";
            isHypothesis = true;
            evidence.Add("hipótese pelo metadado de câmera; sem confirmação visual");
        }
        else if (speechRatio is { } ratio && ratio >= ConversationSpeechRatio)
        {
            category = "conversa";
            band = ratio >= HighConfidenceSpeechRatio ? "high" : "medium";
            isHypothesis = false;
        }
        else if (keywordHits.Count > 0)
        {
            category = keywordHits[0].Category;
            band = "low";
            isHypothesis = true;
        }
        else
        {
            category = "indeterminado";
            band = "low";
            isHypothesis = true;
            evidence.Add("sem evidência suficiente para classificar a cena");
        }

        _logger.LogDebug("Cena classificada para mídia {MediaId}: {Category} ({Band}), hipótese={IsHypothesis}",
            mediaDetails.MediaId, category, band, isHypothesis);

        return new SceneClassificationDto(
            MediaId: mediaDetails.MediaId,
            Category: category,
            ConfidenceBand: band,
            IsHypothesis: isHypothesis,
            Evidence: evidence,
            HasSpeech: hasSpeech);
    }

    private static double ResolveDuration(MediaDetailsDto details, TranscriptDto? transcript)
    {
        if (details.DurationSeconds > 0) return details.DurationSeconds;
        if (transcript != null && transcript.AudioDurationSeconds > 0) return transcript.AudioDurationSeconds;
        return 0.0;
    }

    private static double SpeechSeconds(TranscriptDto transcript, double duration)
    {
        double total = 0.0;
        foreach (var w in transcript.Words)
        {
            double start = Math.Max(0.0, w.StartSeconds);
            double end = Math.Min(duration, w.EndSeconds);
            if (end > start) total += end - start;
        }
        return total;
    }

    private static double SilenceSeconds(List<VadSilenceSegmentDto> silences, double duration)
    {
        var intervals = silences
            .Select(s => (Start: Math.Max(0.0, s.StartSeconds), End: Math.Min(duration, s.EndSeconds)))
            .Where(i => i.End > i.Start)
            .OrderBy(i => i.Start)
            .ToList();

        double total = 0.0;
        double curStart = double.NaN, curEnd = double.NaN;
        foreach (var (start, end) in intervals)
        {
            if (double.IsNaN(curEnd) || start > curEnd)
            {
                if (!double.IsNaN(curEnd)) total += curEnd - curStart;
                curStart = start;
                curEnd = end;
            }
            else
            {
                curEnd = Math.Max(curEnd, end);
            }
        }
        if (!double.IsNaN(curEnd)) total += curEnd - curStart;
        return total;
    }

    private static List<(string Category, string Keyword)> FindKeywordHits(string? text)
    {
        var hits = new List<(string Category, string Keyword)>();
        if (string.IsNullOrWhiteSpace(text)) return hits;

        var tokens = WordPattern.Matches(text.ToLowerInvariant()).Select(m => m.Value).ToHashSet();
        foreach (var (category, keywords) in KeywordGroups)
        {
            foreach (var keyword in keywords)
            {
                if (tokens.Contains(keyword)) hits.Add((category, keyword));
            }
        }
        return hits;
    }

    private static string Percent(double ratio) =>
        $"{Math.Round(ratio * 100, MidpointRounding.AwayFromZero).ToString("0", Pt)}%";
}
