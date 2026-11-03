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
        IEnumerable<EffectiveSuggestion> suggestions)
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
              .Append(s.EndMs.ToString(inv)).Append('\n');
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()))).ToLowerInvariant();
    }
}
