using TravelCut.Contracts.Dtos;

namespace TravelCut.Infrastructure.Audio;

// Payload normalizado vindo do worker (já convertido de JSON, sem interpretação).
public sealed record WhisperRawWord(string Text, double Start, double End, double Probability);

public sealed record WhisperTranscriptPayload(
    string Text,
    double AudioDurationSeconds,
    IReadOnlyList<WhisperRawWord> Words
);

public sealed record TranscriptValidationResult(
    bool IsValid,
    string? Error,
    IReadOnlyList<TranscriptWordDto> Words,
    bool JitterCorrected
);

// Valida a saída do Whisper sem sintetizar nada. Regras:
// - protocolo "travelcut.whisper" versão 1;
// - palavras com texto, tempos finitos, 0 <= start <= end, probability em [0,1];
// - start não-decrescente com tolerância de 0,05 s: recuo maior que isso é falha;
//   recuo dentro da tolerância é jitter e as palavras são reordenadas (estável);
// - end <= duração do áudio + 0,5 s;
// - transcrição sem palavras com áudio válido é válida (silêncio != falha);
//   texto sem palavras com timestamps, ou palavras sem texto, é falha.
public static class TranscriptValidator
{
    public const string ExpectedProtocol = "travelcut.whisper";
    public const int ExpectedVersion = 1;
    public const double MonotonicToleranceSeconds = 0.05;
    public const double EndToleranceSeconds = 0.5;

    public static string? ValidateEnvelope(string? protocol, int? version)
    {
        if (!string.Equals(protocol, ExpectedProtocol, StringComparison.Ordinal))
        {
            return "Protocolo do worker Whisper desconhecido.";
        }

        if (version != ExpectedVersion)
        {
            return $"Versão de protocolo do worker não suportada (esperado {ExpectedVersion}).";
        }

        return null;
    }

    public static TranscriptValidationResult Validate(WhisperTranscriptPayload payload)
    {
        if (!double.IsFinite(payload.AudioDurationSeconds) || payload.AudioDurationSeconds < 0)
        {
            return Invalid("duração do áudio inválida.");
        }

        var limit = payload.AudioDurationSeconds + EndToleranceSeconds;

        for (var i = 0; i < payload.Words.Count; i++)
        {
            var w = payload.Words[i];
            if (string.IsNullOrWhiteSpace(w.Text))
            {
                return Invalid($"palavra {i} sem texto.");
            }

            if (!double.IsFinite(w.Start) || !double.IsFinite(w.End) || !double.IsFinite(w.Probability))
            {
                return Invalid($"palavra {i} com valor não numérico.");
            }

            if (w.Start < 0)
            {
                return Invalid($"palavra {i} com start negativo.");
            }

            if (w.End < w.Start)
            {
                return Invalid($"palavra {i} com end menor que start.");
            }

            if (w.Probability < 0 || w.Probability > 1)
            {
                return Invalid($"palavra {i} com probabilidade fora de [0,1].");
            }

            if (w.End > limit)
            {
                return Invalid($"palavra {i} termina além da duração do áudio.");
            }
        }

        var jitter = false;
        var maxStart = double.NegativeInfinity;
        foreach (var w in payload.Words)
        {
            if (w.Start < maxStart)
            {
                var back = maxStart - w.Start;
                if (back > MonotonicToleranceSeconds)
                {
                    return Invalid($"start não monotônico (recuo de {back.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)} s).");
                }

                jitter = true;
            }

            maxStart = Math.Max(maxStart, w.Start);
        }

        if (payload.Words.Count == 0)
        {
            return string.IsNullOrWhiteSpace(payload.Text)
                ? Valid(Array.Empty<TranscriptWordDto>(), jitterCorrected: false)
                : Invalid("texto presente sem palavras com timestamps.");
        }

        if (string.IsNullOrWhiteSpace(payload.Text))
        {
            return Invalid("palavras presentes com texto completo vazio.");
        }

        var ordered = jitter
            ? payload.Words.OrderBy(w => w.Start).ThenBy(w => w.End).ToList()
            : payload.Words.ToList();

        var words = new List<TranscriptWordDto>(ordered.Count);
        for (var i = 0; i < ordered.Count; i++)
        {
            var w = ordered[i];
            words.Add(new TranscriptWordDto(
                Seq: i + 1,
                StartSeconds: Math.Round(w.Start, 3),
                EndSeconds: Math.Round(w.End, 3),
                Text: w.Text.Trim(),
                Confidence: w.Probability));
        }

        return Valid(words, jitter);
    }

    private static TranscriptValidationResult Valid(IReadOnlyList<TranscriptWordDto> words, bool jitterCorrected) =>
        new(true, null, words, jitterCorrected);

    private static TranscriptValidationResult Invalid(string error) =>
        new(false, error, Array.Empty<TranscriptWordDto>(), false);
}
