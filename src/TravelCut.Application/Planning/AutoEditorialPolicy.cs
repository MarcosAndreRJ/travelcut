using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace TravelCut.Application.Planning;

/// <summary>Perfil da política editorial automática. Vem de <c>EditorialProfileDto.SilencePolicy</c> (default: Balanced).</summary>
public enum AutoEditorialProfile
{
    Conservative,
    Balanced,
    Aggressive,
}

/// <summary>Decisão da política para um candidato: aplicar sozinho, pedir revisão humana ou preservar (não aplicar nem poluir a fila).</summary>
public enum PolicyAction
{
    Apply,
    Review,
    Skip,
}

/// <summary>Veredito auditável: ação, razão legível, evidências verificáveis, versão da política e perfil usado.</summary>
public sealed record PolicyVerdict(
    PolicyAction Action,
    string Reason,
    IReadOnlyList<string> Evidence,
    string PolicyVersion,
    AutoEditorialProfile Profile);

/// <summary>Palavra transcrita com alinhamento (µs na base da mídia de origem).</summary>
public sealed record PolicyWord(string Text, long StartUs, long EndUs);

/// <summary>
/// Fatos objetivos sobre um candidato de corte. A política decide só por estes fatos (duração, fala/palavras alinhadas, tipo);
/// nunca pela string de confiança ("high"/"medium"/"low") produzida pelo analisador.
/// </summary>
/// <param name="Kind">silence | pause | repetition | hesitation | filler | false_start | prolongation | outros (zoom, transição...).</param>
/// <param name="StartUs">Início efetivo do intervalo (µs, mídia de origem).</param>
/// <param name="EndUs">Fim efetivo do intervalo (µs, mídia de origem).</param>
/// <param name="TranscriptKnown">Transcrição concluída (sem ela a ausência de fala não é verificável).</param>
/// <param name="Words">Palavras alinhadas da mídia (vazio se desconhecido).</param>
/// <param name="UpstreamFlaggedReview">O analisador já marcou o candidato como "precisa de revisão".</param>
/// <param name="HasExplicitDecision">Há decisão explícita do usuário no histórico (aprovar/rejeitar/adiar/desfazer...).</param>
public sealed record AutoEditCandidate(
    string Kind,
    long StartUs,
    long EndUs,
    bool TranscriptKnown,
    IReadOnlyList<PolicyWord> Words,
    bool UpstreamFlaggedReview,
    bool HasExplicitDecision);

/// <summary>Política editorial central de aplicação automática (E13).</summary>
public interface IAutoEditorialPolicy
{
    string PolicyVersion { get; }

    PolicyVerdict Evaluate(AutoEditCandidate candidate, AutoEditorialProfile profile);
}

public static class AutoEditorialProfiles
{
    public const AutoEditorialProfile Default = AutoEditorialProfile.Balanced;

    /// <summary>"conservative" | "balanced" | "aggressive" (sem diferenciar maiúsculas). Null/desconhecido → Balanced.</summary>
    public static AutoEditorialProfile FromName(string? name) => name?.Trim().ToLowerInvariant() switch
    {
        "conservative" => AutoEditorialProfile.Conservative,
        "aggressive" => AutoEditorialProfile.Aggressive,
        _ => Default,
    };

    public static string ToName(AutoEditorialProfile profile) => profile switch
    {
        AutoEditorialProfile.Conservative => "conservative",
        AutoEditorialProfile.Aggressive => "aggressive",
        _ => "balanced",
    };
}

/// <summary>
/// Implementação determinística de <see cref="IAutoEditorialPolicy"/>.
///
/// PRECEDÊNCIA (E07), da mais forte para a mais fraca:
///   1. decisão manual explícita do usuário (aprovar, rejeitar, adiar, modificar, DESFAZER/reabrir) — a automação nunca a
///      sobrescreve nem a reaplica: aprovar → desfazer → a sugestão volta a "pendente" e NÃO é autoaplicada de novo;
///   2. configuração do projeto (perfil e AutoApplyTrivialCuts, avaliados pelo chamador antes da política);
///   3. esta política automática.
///
/// Matriz (Apply só com evidência verificável; na dúvida, Review):
///   silêncio inequívoco (sem fala/risada na margem, palavras de contexto, duração ≥ limiar do perfil) → Apply;
///   pausa curta expressiva (após "?"/"!"/reticências ou risada próxima) → Skip (preservar);
///   repetição → Apply só em Aggressive com evidência forte (palavras idênticas alinhadas, curtas, sem ênfase/negação); senão Review;
///   hesitação / falso início / filler / prolongação / alinhamento incerto / sem transcrição → Review;
///   zoom, transição e demais efeitos → Review (tratados em incremento próprio).
/// </summary>
public sealed class AutoEditorialPolicy : IAutoEditorialPolicy
{
    /// <summary>Versão do conjunto de regras; incrementar a cada mudança de limiar ou regra.</summary>
    public const string Version = "auto-editorial-policy/1";

    public static readonly AutoEditorialPolicy Instance = new();

    /// <summary>Janela de busca de risada ao redor do intervalo (preservar piadas).</summary>
    private const long LaughterWindowUs = 1_500_000;

    /// <summary>Tolerância de fronteira entre silêncio e palavras vizinhas (adjacência).</summary>
    private const long BoundaryToleranceUs = 100_000;

    /// <summary>Intervalo máximo entre as palavras repetidas para valer como gagueira (e não ênfase retórica).</summary>
    private const long MaxRepetitionGapUs = 600_000;

    /// <summary>Duração máxima por palavra repetida para a evidência ser "forte".</summary>
    private const long MaxRepeatedWordUs = 1_200_000;

    private sealed record Thresholds(long MinSilenceUs, long BoundaryJitterUs, bool RequireBothNeighbors, bool RepetitionMayApply);

    private static Thresholds For(AutoEditorialProfile profile) => profile switch
    {
        AutoEditorialProfile.Conservative => new Thresholds(2_500_000, 50_000, true, false),
        AutoEditorialProfile.Aggressive => new Thresholds(1_000_000, 300_000, false, true),
        _ => new Thresholds(2_000_000, 150_000, true, false),
    };

    // Respostas/negações/intensificadores repetidos são ênfase ou resposta, não gagueira.
    private static readonly HashSet<string> EmphaticTokens = new(StringComparer.Ordinal)
    {
        "não", "nao", "sim", "nunca", "jamais", "nada", "claro", "tá", "ta", "ok", "muito", "bem", "já", "ja", "tudo",
        "mesmo", "demais", "sempre", "ainda", "agora",
    };

    private static readonly Regex NonLetter = new(@"\P{L}+", RegexOptions.Compiled);

    private static readonly Regex Laughter = new(
        @"^(?:kk+|(?:ha|he|hi|ho|hu){2,}h?|h?(?:ha|he|hi|ho|hu)h?|(?:ah){2,}h?|(?:rs)+|(?:hue)+h?)$",
        RegexOptions.Compiled);

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public string PolicyVersion => Version;

    public PolicyVerdict Evaluate(AutoEditCandidate c, AutoEditorialProfile profile)
    {
        // 1) Precedência máxima: decisão manual explícita (inclusive desfazer) nunca é reaplicada pela automação.
        if (c.HasExplicitDecision)
        {
            return Verdict(PolicyAction.Review, profile,
                "decisão manual explícita no histórico (inclusive desfazer/rejeitar) prevalece; a automação não reaplica",
                "precedência: decisão manual > config do projeto > política automática");
        }

        return c.Kind switch
        {
            "silence" => EvaluateSilence(c, profile),
            "pause" => EvaluatePause(c, profile),
            "repetition" => EvaluateRepetition(c, profile),
            "hesitation" or "filler" or "false_start" or "prolongation" =>
                Verdict(PolicyAction.Review, profile, $"{c.Kind}: ambíguo sem contexto semântico; revisão humana",
                    "tipo de candidato nunca autoaplicado"),
            _ => Verdict(PolicyAction.Review, profile, $"'{c.Kind}' não é elegível à autoaplicação nesta política",
                "tipo de candidato fora da matriz (zoom/transição/outros tratados em incremento próprio)"),
        };
    }

    private PolicyVerdict EvaluateSilence(AutoEditCandidate c, AutoEditorialProfile profile)
    {
        var t = For(profile);
        long duration = c.EndUs - c.StartUs;
        if (duration <= 0)
        {
            return Verdict(PolicyAction.Review, profile, "intervalo vazio", $"duração {Sec(duration)} s");
        }
        if (!c.TranscriptKnown)
        {
            return Verdict(PolicyAction.Review, profile, "sem transcrição: ausência de fala não verificável",
                "transcrição indisponível");
        }
        if (!AlignmentIsCertain(c.Words, c.StartUs - t.BoundaryJitterUs - LaughterWindowUs, c.EndUs + t.BoundaryJitterUs + LaughterWindowUs))
        {
            return Verdict(PolicyAction.Review, profile, "alinhamento de palavras incerto na vizinhança do silêncio",
                "palavra com duração <= 0 ou fora de ordem");
        }

        if (c.Words.Any(w => w.EndUs > c.StartUs + t.BoundaryJitterUs && w.StartUs < c.EndUs - t.BoundaryJitterUs))
        {
            return Verdict(PolicyAction.Review, profile,
                $"há fala dentro do silêncio (tolerância de borda {Sec(t.BoundaryJitterUs)} s)",
                $"tolerância de borda do perfil (jitter Whisper x silencedetect): {Sec(t.BoundaryJitterUs)} s");
        }

        var previous = c.Words.Where(w => w.EndUs <= c.StartUs + BoundaryToleranceUs).OrderBy(w => w.EndUs).LastOrDefault();
        var next = c.Words.Where(w => w.StartUs >= c.EndUs - BoundaryToleranceUs).OrderBy(w => w.StartUs).FirstOrDefault();

        bool laughter = HasLaughterNear(c);
        bool expressive = previous is not null && EndsExpressively(previous.Text);
        if (laughter || expressive)
        {
            string why = laughter ? $"risada a menos de {Sec(LaughterWindowUs)} s" : "palavra anterior termina em '?', '!' ou reticências";
            return duration < t.MinSilenceUs
                ? Verdict(PolicyAction.Skip, profile, $"pausa curta expressiva preservada ({why})", $"duração {Sec(duration)} s < {Sec(t.MinSilenceUs)} s")
                : Verdict(PolicyAction.Review, profile, $"silêncio longo possivelmente expressivo ({why}); revisar", why);
        }

        if (duration < t.MinSilenceUs)
        {
            return Verdict(PolicyAction.Review, profile,
                $"duração {Sec(duration)} s abaixo do limiar {Sec(t.MinSilenceUs)} s do perfil", $"limiar do perfil: {Sec(t.MinSilenceUs)} s");
        }

        bool neighborsOk = t.RequireBothNeighbors ? previous is not null && next is not null : previous is not null || next is not null;
        if (!neighborsOk)
        {
            return Verdict(PolicyAction.Review, profile, "sem palavras de contexto confirmando o silêncio", "contexto de fala ausente");
        }

        if (c.UpstreamFlaggedReview && profile != AutoEditorialProfile.Aggressive)
        {
            return Verdict(PolicyAction.Review, profile, "analisador sinalizou revisão humana", "sinalização do analisador");
        }

        return Verdict(PolicyAction.Apply, profile,
            $"silêncio inequívoco de {Sec(duration)} s",
            $"duração {Sec(duration)} s >= limiar {Sec(t.MinSilenceUs)} s (perfil {AutoEditorialProfiles.ToName(profile)})",
            $"nenhuma palavra dentro do intervalo (tolerância de borda {Sec(t.BoundaryJitterUs)} s; transcrição alinhada)",
            $"contexto: '{previous?.Text.Trim() ?? "-"}' / '{next?.Text.Trim() ?? "-"}'",
            $"sem risada a ±{Sec(LaughterWindowUs)} s e sem pontuação expressiva antes");
    }

    private PolicyVerdict EvaluatePause(AutoEditCandidate c, AutoEditorialProfile profile)
    {
        var t = For(profile);
        long duration = c.EndUs - c.StartUs;
        bool expressive = c.TranscriptKnown
            && (HasLaughterNear(c)
                || c.Words.Where(w => w.EndUs <= c.StartUs + BoundaryToleranceUs).OrderBy(w => w.EndUs).LastOrDefault() is { } prev
                   && EndsExpressively(prev.Text));
        if (expressive)
        {
            return Verdict(PolicyAction.Skip, profile, "pausa curta expressiva preservada (ritmo/humor)",
                $"duração {Sec(duration)} s; risada ou pontuação expressiva adjacente");
        }
        return Verdict(PolicyAction.Review, profile, "pausa curta: ritmo dramático ou respiro; decisão humana",
            $"pausa não é silêncio inequívoco (limiar do perfil {Sec(t.MinSilenceUs)} s)");
    }

    private PolicyVerdict EvaluateRepetition(AutoEditCandidate c, AutoEditorialProfile profile)
    {
        var t = For(profile);
        if (!c.TranscriptKnown)
        {
            return Verdict(PolicyAction.Review, profile, "repetição sem transcrição verificável", "transcrição indisponível");
        }

        const long tol = BoundaryToleranceUs;
        var span = c.Words.Where(w => w.StartUs >= c.StartUs - tol && w.EndUs <= c.EndUs + tol).OrderBy(w => w.StartUs).ToList();
        var strong = StrongRepetitionEvidence(c, span, out string weakness);
        if (!strong)
        {
            return Verdict(PolicyAction.Review, profile, $"repetição sem evidência forte: {weakness}", weakness);
        }
        if (!t.RepetitionMayApply)
        {
            return Verdict(PolicyAction.Review, profile,
                $"repetição com evidência forte, mas o perfil {AutoEditorialProfiles.ToName(profile)} só a revisa",
                "autoaplicação de repetição: somente perfil aggressive");
        }
        return Verdict(PolicyAction.Apply, profile, "repetição imediata com evidência forte",
            $"palavras repetidas idênticas: '{string.Join(' ', span.Select(w => w.Text.Trim()))}'",
            "alinhamento certo, intervalo curto entre repetições, não é negação/resposta/ênfase, há continuação");
    }

    private static bool StrongRepetitionEvidence(AutoEditCandidate c, List<PolicyWord> span, out string weakness)
    {
        weakness = string.Empty;
        if (span.Count is not (2 or 4))
        {
            weakness = "intervalo não cobre 1 ou 2 palavras repetidas";
            return false;
        }
        if (!AlignmentIsCertain(span, long.MinValue, long.MaxValue))
        {
            weakness = "alinhamento incerto";
            return false;
        }
        int half = span.Count / 2;
        var tokens = span.Select(w => Normalize(w.Text)).ToArray();
        for (int i = 0; i < half; i++)
        {
            if (tokens[i].Length == 0 || tokens[i] != tokens[i + half])
            {
                weakness = "palavras não são idênticas";
                return false;
            }
        }
        if (tokens.Any(tok => EmphaticTokens.Contains(tok) || Laughter.IsMatch(tok)))
        {
            weakness = "negação/resposta/ênfase ou risada repetida";
            return false;
        }
        if (span.Any(w => w.EndUs - w.StartUs > MaxRepeatedWordUs) || span[half].StartUs - span[half - 1].EndUs > MaxRepetitionGapUs)
        {
            weakness = "pausa ou prolongamento entre as repetições (pode ser ênfase)";
            return false;
        }
        long spanEnd = span[^1].EndUs;
        if (!c.Words.Any(w => w.StartUs >= spanEnd && w.StartUs - spanEnd <= 1_500_000))
        {
            weakness = "sem continuação da frase";
            return false;
        }
        if (HasLaughterNear(c))
        {
            weakness = "risada próxima";
            return false;
        }
        return true;
    }

    private static bool HasLaughterNear(AutoEditCandidate c) =>
        c.Words.Any(w => Laughter.IsMatch(Normalize(w.Text))
                         && w.EndUs >= c.StartUs - LaughterWindowUs && w.StartUs <= c.EndUs + LaughterWindowUs);

    private static bool EndsExpressively(string text)
    {
        var trimmed = text.TrimEnd();
        return trimmed.Length > 0 && (trimmed[^1] is '?' or '!' or '…' || trimmed.EndsWith("...", StringComparison.Ordinal));
    }

    /// <summary>Palavras na janela precisam ter duração positiva e ordem cronológica; senão o alinhamento é incerto.</summary>
    private static bool AlignmentIsCertain(IReadOnlyList<PolicyWord> words, long fromUs, long toUs)
    {
        long? lastEnd = null;
        foreach (var w in words.OrderBy(w => w.StartUs))
        {
            if (w.EndUs < fromUs || w.StartUs > toUs) continue;
            if (w.EndUs <= w.StartUs || (lastEnd is long le && w.StartUs < le - 1_000)) return false;
            lastEnd = lastEnd is long prevEnd ? Math.Max(prevEnd, w.EndUs) : w.EndUs;
        }
        return true;
    }

    private static string Normalize(string text) =>
        NonLetter.Replace((text ?? string.Empty).Trim().Normalize(NormalizationForm.FormC).ToLowerInvariant(), string.Empty);

    private static string Sec(long us) => (us / 1_000_000d).ToString("0.0#", Inv);

    private static PolicyVerdict Verdict(PolicyAction action, AutoEditorialProfile profile, string reason, params string[] evidence) =>
        new(action, reason, evidence, Version, profile);
}
