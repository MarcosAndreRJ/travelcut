using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using TravelCut.Contracts.Dtos;

namespace TravelCut.Application.Planning;

/// <summary>
/// Fingerprint das ENTRADAS de uma revisão do plano (não do conteúdo): conjunto de mídias, estado efetivo de todas as
/// sugestões do projeto (id, status, intervalo efetivo), perfil, base de tempo e fuso. Gravado no índice da revisão na
/// geração; se o valor atual diverge, a revisão está desatualizada (PLAN_OUTDATED).
/// Ordem canônica: sugestões por id; números em invariant culture.
/// </summary>
public static class PlanInputsFingerprint
{
    private const string Schema = "plan-inputs/1";

    public static string Compute(
        string mediaSetFingerprint,
        EditorialProfileDto profile,
        TimelineTimeBaseDto timeBase,
        string timeZoneId,
        IEnumerable<EffectiveSuggestion> suggestions,
        IReadOnlyList<TravelCut.Domain.Planning.PlanManualEdit>? manualEdits = null)
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.Append(Schema).Append('\n');
        sb.Append("media|").Append(mediaSetFingerprint).Append('\n');
        sb.Append("profile|").Append(profile.ProfileId).Append('|')
          .Append(profile.Version.ToString(inv)).Append('|')
          .Append(profile.SilencePolicy).Append('|')
          .Append(profile.Pace).Append('|')
          .Append(profile.JokePreservation).Append('|')
          .Append(profile.ZoomStrength).Append('|')
          .Append(profile.SpeedBroll).Append('|')
          .Append(profile.MusicPolicy).Append('|')
          .Append(profile.ReviewAmbiguous ? "1" : "0").Append('|')
          .Append(profile.AutoApplyTrivialCuts ? "1" : "0").Append('\n');
        sb.Append("timeBase|")
          .Append(timeBase.FrameDurationNum.ToString(inv)).Append('|')
          .Append(timeBase.FrameDurationDen.ToString(inv)).Append('\n');
        sb.Append("tz|").Append(timeZoneId).Append('\n');
        foreach (var s in suggestions.OrderBy(x => x.SuggestionId))
        {
            sb.Append("s|").Append(s.SuggestionId.ToString("D")).Append('|')
              .Append(s.Status.ToString()).Append('|')
              .Append(s.StartMs.ToString(inv)).Append('|')
              .Append(s.EndMs.ToString(inv));
            // F14.8: só quando há parâmetros (fingerprints de projetos antigos permanecem idênticos).
            if (s.ParametersJson is not null) sb.Append("|p:").Append(s.ParametersJson);
            sb.Append('\n');
        }
        // F14.7/04: só quando há overlay manual (fingerprints de projetos sem edição manual permanecem idênticos).
        if (manualEdits is { Count: > 0 })
            foreach (var e in manualEdits.OrderBy(x => x.Seq).ThenBy(x => x.EditId))
                sb.Append("me|").Append(e.Seq.ToString(inv)).Append('|').Append(e.Kind).Append('|').Append(e.MediaId.ToString("D")).Append('|')
                  .Append(e.SourceInUs.ToString(inv)).Append('|').Append(e.SourceOutUs.ToString(inv)).Append('|')
                  .Append(e.AfterMediaId?.ToString("D") ?? "-").Append('|').Append(e.AfterSourceInUs?.ToString(inv) ?? "-").Append('|')
                  .Append(e.Zoom?.ToString("R", inv) ?? "-").Append('|').Append(e.SpeedNum?.ToString(inv) ?? "-").Append('|')
                  .Append(e.SpeedDen?.ToString(inv) ?? "-").Append('|').Append(e.IntroducedInRevision.ToString(inv)).Append('\n');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()))).ToLowerInvariant();
    }
}
