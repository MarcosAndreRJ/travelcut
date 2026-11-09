using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace TravelCut.Domain.Planning;

/// <summary>
/// Fingerprints determinísticos. A serialização canônica é JSON compacto escrito manualmente com Utf8JsonWriter
/// (ordem de propriedades fixa, listas ordenadas por chaves estáveis, Guid "D" minúsculo, números invariantes).
/// Excluídos do plano: PlanId, Revision, BaseRevision, CreatedAtUtc, Author. Incluídos: perfil inteiro e timeZoneId.
/// </summary>
public static class PlanFingerprint
{
    /// <summary>SHA-256 hex minúsculo de "id|fingerprint|durationUs" por mídia, ordenado por MediaId (caminho não entra).</summary>
    public static string ComputeMediaSet(IEnumerable<MediaFacts> media)
    {
        var sb = new StringBuilder();
        foreach (var m in media.OrderBy(m => m.MediaId))
            sb.Append(m.MediaId.ToString("D")).Append('|').Append(m.Fingerprint).Append('|')
              .Append(m.DurationUs.ToString(CultureInfo.InvariantCulture)).Append('\n');
        return Sha256Hex(Encoding.UTF8.GetBytes(sb.ToString()));
    }

    public static string ComputePlan(PlanRevision p) => Sha256Hex(CanonicalJson(p));

    internal static byte[] CanonicalJson(PlanRevision p)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = false, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            w.WriteStartObject();
            w.WriteString("schema", "editplan-canonical/2");
            w.WriteString("projectId", p.ProjectId.ToString("D"));
            // Perfil inteiro (todos os campos), não só id/versão: alterar qualquer política muda o fingerprint.
            w.WriteStartObject("profile");
            w.WriteString("id", p.Profile.ProfileId);
            w.WriteNumber("version", p.Profile.Version);
            w.WriteString("silence", p.Profile.SilencePolicy);
            w.WriteString("pace", p.Profile.Pace);
            w.WriteString("joke", p.Profile.JokePreservation);
            w.WriteString("zoom", p.Profile.ZoomStrength);
            w.WriteString("speedBroll", p.Profile.SpeedBroll);
            w.WriteString("music", p.Profile.MusicPolicy);
            w.WriteBoolean("reviewAmbiguous", p.Profile.ReviewAmbiguous);
            w.WriteBoolean("autoTrivial", p.Profile.AutoApplyTrivialCuts);
            w.WriteEndObject();
            w.WriteNumber("tbNum", p.TimeBase.Numerator);
            w.WriteNumber("tbDen", p.TimeBase.Denominator);
            w.WriteString("timeZoneId", p.TimeZoneId);
            w.WriteString("mediaSet", p.MediaSetFingerprint);

            w.WriteStartArray("tracks");
            foreach (var t in p.Tracks.OrderBy(t => t.TrackId, StringComparer.Ordinal))
            { w.WriteStartObject(); w.WriteString("id", t.TrackId); w.WriteString("kind", t.Kind); w.WriteString("label", t.Label); w.WriteEndObject(); }
            w.WriteEndArray();

            w.WriteStartArray("sections");
            foreach (var s in p.Sections.OrderBy(s => s.Order).ThenBy(s => s.SectionId))
            {
                w.WriteStartObject(); w.WriteString("id", s.SectionId.ToString("D")); w.WriteNumber("order", s.Order);
                w.WriteString("role", s.Role); w.WriteString("title", s.Title); w.WriteString("rationale", s.Rationale);
                w.WriteBoolean("hyp", s.IsHypothesis); w.WriteNumber("start", s.StartFrame); w.WriteNumber("end", s.EndFrame);
                w.WriteEndObject();
            }
            w.WriteEndArray();

            w.WriteStartArray("clips");
            foreach (var c in p.Clips.OrderBy(c => c.TrackId, StringComparer.Ordinal).ThenBy(c => c.TimelineStartFrame).ThenBy(c => c.ClipId))
            {
                w.WriteStartObject();
                w.WriteString("id", c.ClipId.ToString("D")); w.WriteString("track", c.TrackId);
                w.WriteString("media", c.MediaId.ToString("D")); w.WriteString("path", c.RelativePath);
                if (c.SectionId is Guid sid) w.WriteString("section", sid.ToString("D")); else w.WriteNull("section");
                w.WriteNumber("inUs", c.SourceInUs); w.WriteNumber("outUs", c.SourceOutUs);
                w.WriteNumber("start", c.TimelineStartFrame); w.WriteNumber("dur", c.DurationFrames);
                w.WriteNumber("spN", c.SpeedNum); w.WriteNumber("spD", c.SpeedDen);
                if (c.LinkedClipId is Guid lid) w.WriteString("link", lid.ToString("D")); else w.WriteNull("link");
                if (c.Zoom is double z) w.WriteNumber("zoom", z); else w.WriteNull("zoom"); // double: menor round-trip, invariante
                // Foco/duração/curva do zoom: só quando existem (planos antigos mantêm o mesmo fingerprint).
                if (c.ZoomFocalPointX is double zfx) w.WriteNumber("zfx", zfx);
                if (c.ZoomFocalPointY is double zfy) w.WriteNumber("zfy", zfy);
                if (c.ZoomDurationSeconds is double zdur) w.WriteNumber("zdur", zdur);
                if (c.ZoomCurve is string zcur) w.WriteString("zcur", zcur);
                w.WriteString("rationale", c.Rationale);
                w.WriteStartArray("evidence"); foreach (var e in c.Evidence) w.WriteStringValue(e); w.WriteEndArray();
                w.WriteStartArray("suggestions"); foreach (var g in c.SuggestionIds.OrderBy(g => g)) w.WriteStringValue(g.ToString("D")); w.WriteEndArray();
                w.WriteString("band", c.ConfidenceBand); w.WriteBoolean("review", c.NeedsHumanReview);
                w.WriteEndObject();
            }
            w.WriteEndArray();

            w.WriteStartArray("transitions");
            foreach (var t in p.Transitions.OrderBy(t => t.FromClipId).ThenBy(t => t.ToClipId))
            {
                w.WriteStartObject(); w.WriteString("from", t.FromClipId.ToString("D")); w.WriteString("to", t.ToClipId.ToString("D"));
                w.WriteString("kind", t.Kind); w.WriteNumber("dur", t.DurationFrames); w.WriteBoolean("review", t.NeedsHumanReview);
                // Só quando há origem (transição materializada por sugestão): fingerprints de planos antigos permanecem idênticos.
                if (t.Origin is not null)
                {
                    w.WriteString("origin", t.Origin);
                    if (t.SuggestionId is Guid tsid) w.WriteString("sug", tsid.ToString("D"));
                }
                w.WriteEndObject();
            }
            w.WriteEndArray();

            w.WriteStartArray("cuts");
            foreach (var c in p.AppliedCuts.OrderBy(c => c.SuggestionId).ThenBy(c => c.MediaId).ThenBy(c => c.SourceInUs).ThenBy(c => c.SourceOutUs))
            {
                w.WriteStartObject(); w.WriteString("sug", c.SuggestionId.ToString("D")); w.WriteString("media", c.MediaId.ToString("D"));
                w.WriteNumber("inUs", c.SourceInUs); w.WriteNumber("outUs", c.SourceOutUs); w.WriteString("kind", c.Kind); w.WriteString("origin", c.Origin);
                w.WriteEndObject();
            }
            w.WriteEndArray();

            w.WriteStartArray("pending");
            foreach (var i in p.Pending.OrderBy(i => i.Kind, StringComparer.Ordinal).ThenBy(i => i.SuggestionId).ThenBy(i => i.MediaId).ThenBy(i => i.ClipId).ThenBy(i => i.Message, StringComparer.Ordinal))
            {
                w.WriteStartObject();
                if (i.SuggestionId is Guid a) w.WriteString("sug", a.ToString("D")); else w.WriteNull("sug");
                if (i.MediaId is Guid b) w.WriteString("media", b.ToString("D")); else w.WriteNull("media");
                if (i.ClipId is Guid cl) w.WriteString("clip", cl.ToString("D")); else w.WriteNull("clip");
                w.WriteString("kind", i.Kind); w.WriteString("msg", i.Message);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return ms.ToArray();
    }

    private static string Sha256Hex(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
}
