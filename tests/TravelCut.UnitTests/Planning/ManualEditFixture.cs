using System.Globalization;
using TravelCut.Application.Planning;
using TravelCut.Contracts.Dtos;
using TravelCut.Domain.Planning;
using TravelCut.Domain.ValueObjects;

namespace TravelCut.Tests.Planning;

/// <summary>Fixture do motor de overlay (F14.7b): monta planos automáticos EditPlanDto a partir de especificações de segmentos.</summary>
internal static class ManualEditFixture
{
    public const long S = 1_000_000;
    public static Guid G(int n) => new($"00000000-0000-0000-0000-{n:D12}");
    public static readonly Guid A = G(1), B = G(2), C = G(3);
    public static readonly RationalTimeBase Tb = RationalTimeBase.Fps2997;
    public static readonly Dictionary<Guid, long> Durations = new() { [A] = 60 * S, [B] = 40 * S, [C] = 30 * S };

    public sealed record Spec(Guid Media, long In, long Out, int Sn = 1, int Sd = 1, bool V = true, bool Au = true, Guid? Section = null);

    public static List<Spec> DefaultSpecs() => new()
    {
        new(A, 0, 10 * S), new(A, 15 * S, 30 * S), new(A, 30 * S, 45 * S, 3, 2),
        new(B, 0, 20 * S), new(B, 25 * S, 40 * S),
        new(C, 0, 10 * S, V: false), new(C, 12 * S, 30 * S, V: false),
    };

    public static EditPlanDto Default(int dissolveAt = -1) => Build(DefaultSpecs(), Durations, dissolveAt: dissolveAt);

    private static int SectionIndex(Guid media) => media == A ? 1 : media == B ? 2 : media == C ? 3 : 10 + (media.ToByteArray()[15] % 50);

    public static EditPlanDto Build(IReadOnlyList<Spec> specs, IReadOnlyDictionary<Guid, long> durations, IReadOnlyList<PlanManualEdit>? edits = null,
        int dissolveAt = -1, int revision = 1)
    {
        var inv = CultureInfo.InvariantCulture;
        var layout = TimelineMath.LayoutSequential(specs.Select(s => (s.In, s.Out, s.Sn, s.Sd)).ToList(), Tb, 0);
        var clips = new List<PlanClip>();
        var v1 = new Guid?[specs.Count]; var a1 = new Guid?[specs.Count];
        for (int i = 0; i < specs.Count; i++)
        {
            var s = specs[i];
            if (s.V) v1[i] = DeterministicIds.Create("clip", "V1", s.Media.ToString("D"), s.In.ToString(inv), s.Out.ToString(inv));
            if (s.Au) a1[i] = DeterministicIds.Create("clip", "A1", s.Media.ToString("D"), s.In.ToString(inv), s.Out.ToString(inv));
            var sec = s.Section ?? G(100 + SectionIndex(s.Media));
            if (v1[i] is Guid v) clips.Add(new PlanClip(v, "V1", s.Media, "m.mp4", sec, s.In, s.Out, layout[i].StartFrame, layout[i].DurationFrames, s.Sn, s.Sd, a1[i], null, "r", new List<string>(), new List<Guid>(), "high", false));
            if (a1[i] is Guid a) clips.Add(new PlanClip(a, "A1", s.Media, "m.mp4", sec, s.In, s.Out, layout[i].StartFrame, layout[i].DurationFrames, s.Sn, s.Sd, v1[i], null, "r", new List<string>(), new List<Guid>(), "high", false));
        }
        var transitions = new List<PlanTransition>();
        for (int i = 0; i + 1 < specs.Count; i++)
        {
            if (v1[i] is Guid f && v1[i + 1] is Guid t)
                transitions.Add(i == dissolveAt ? new PlanTransition(f, t, "dissolve", 12, true, "auto", G(900)) : new PlanTransition(f, t, "cut", 0, false));
            if (a1[i] is Guid fa && a1[i + 1] is Guid ta) transitions.Add(new PlanTransition(fa, ta, "cut", 0, false));
        }
        var cuts = new List<PlanAppliedCut>();
        long removed = 0;
        foreach (var g in specs.GroupBy(s => s.Media))
        {
            var kept = UnionOf(g.Select(s => (s.In, s.Out)));
            long cur = 0;
            foreach (var (a, b) in kept)
            {
                if (a > cur) { cuts.Add(new PlanAppliedCut(DeterministicIds.Create("cut", g.Key.ToString("D"), cur.ToString(inv)), g.Key, cur, a, "cut", "auto_trivial")); removed += a - cur; }
                cur = b;
            }
            long dur = durations[g.Key];
            if (cur < dur) { cuts.Add(new PlanAppliedCut(DeterministicIds.Create("cut", g.Key.ToString("D"), cur.ToString(inv)), g.Key, cur, dur, "cut", "auto_trivial")); removed += dur - cur; }
        }
        var sections = new List<PlanSection>();
        foreach (var g in clips.GroupBy(c => c.SectionId).OrderBy(g => g.Min(c => c.TimelineStartFrame)))
            sections.Add(new PlanSection(g.Key!.Value, sections.Count + 1, "desenvolvimento", "s", "r", true, g.Min(c => c.TimelineStartFrame), g.Max(c => c.TimelineStartFrame + c.DurationFrames)));
        var profile = new PlanProfileSnapshot("viagens", 1, "conservative", "normal", "strict", "subtle", "auto", "suggest_only", true, true);
        var domain = new PlanRevision(G(500), G(501), revision, null, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), "planner-rules/1", profile, Tb, "UTC", "mfp",
            new[] { new PlanTrack("V1", "video", "V1"), new PlanTrack("A1", "dialogue", "A1") }, sections, clips, transitions, cuts, new List<PlanPendingItem>(), null, edits);
        long end = clips.Count == 0 ? 0 : clips.Max(c => c.TimelineStartFrame + c.DurationFrames);
        var summary = new PlanSummaryDto(durations.Where(d => specs.Any(s => s.Media == d.Key)).Sum(d => d.Value), end, removed, 0, cuts.Count, 0, 0);
        return PlanMapping.ToDto(domain, summary, new PlanValidationDto(true, new(), domain.CreatedAtUtc), PlanFingerprint.ComputePlan(domain));
    }

    private static List<(long A, long B)> UnionOf(IEnumerable<(long A, long B)> r)
    {
        var res = new List<(long A, long B)>();
        foreach (var x in r.OrderBy(x => x.A)) { if (res.Count > 0 && x.A <= res[^1].B) res[^1] = (res[^1].A, Math.Max(res[^1].B, x.B)); else res.Add(x); }
        return res;
    }

    /// <summary>Forma canônica de equivalência: segmentos em ordem, fundindo vizinhos contíguos na fonte (mesma mídia/velocidade/trilhas).</summary>
    public static string Canon(EditPlanDto plan)
    {
        var segs = new List<(Guid M, long In, long Out, int Sn, int Sd, bool V, bool A)>();
        var byId = plan.Clips.ToDictionary(c => c.ClipId);
        var used = new HashSet<Guid>();
        foreach (var c in plan.Clips.OrderBy(c => c.TimelineStartFrame).ThenBy(c => c.TrackId == "V1" ? 0 : 1))
        {
            if (used.Contains(c.ClipId)) continue;
            used.Add(c.ClipId);
            bool pair = c.LinkedClipId is Guid l && byId.TryGetValue(l, out var o) && used.Add(o.ClipId);
            var s = (M: c.MediaId, In: c.SourceInUs, Out: c.SourceOutUs, Sn: c.SpeedNum, Sd: c.SpeedDen, V: c.TrackId == "V1" || pair, A: c.TrackId == "A1" || pair);
            if (segs.Count > 0 && segs[^1].M == s.M && segs[^1].Out == s.In && segs[^1].Sn == s.Sn && segs[^1].Sd == s.Sd && segs[^1].V == s.V && segs[^1].A == s.A)
                segs[^1] = (s.M, segs[^1].In, s.Out, s.Sn, s.Sd, s.V, s.A);
            else segs.Add(s);
        }
        return string.Join(";", segs.Select(s => $"{s.M:N}:{s.In}-{s.Out}@{s.Sn}/{s.Sd}{(s.V ? "v" : "")}{(s.A ? "a" : "")}"));
    }
}
