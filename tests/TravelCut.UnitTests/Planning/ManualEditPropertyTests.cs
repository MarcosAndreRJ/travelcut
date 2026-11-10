using System.Text.Json;
using TravelCut.Application.Planning;
using TravelCut.Contracts.Dtos;
using TravelCut.Domain.Planning;
using Xunit;
using static TravelCut.Tests.Planning.ManualEditFixture;

namespace TravelCut.Tests.Planning;

/// <summary>Testes de propriedades (seed fixa) das invariantes I1-I10 do motor de overlay (F14.7b).</summary>
public class ManualEditPropertyTests
{
    private const long Grid = 500_000;

    private static (List<Spec> Specs, Dictionary<Guid, long> Durs) RandomAuto(Random rng)
    {
        var specs = new List<Spec>();
        var durs = new Dictionary<Guid, long>();
        int n = 2 + rng.Next(3);
        for (int i = 1; i <= n; i++)
        {
            var m = G(i);
            long dur = (20 + rng.Next(40)) * S;
            durs[m] = dur;
            int kind = rng.Next(10);
            bool v = kind != 0 && kind != 1, a = kind != 2;           // só-áudio (0,1), só-vídeo (2), vídeo+áudio
            int pieces = 1 + rng.Next(4);
            var pts = new SortedSet<long>();
            while (pts.Count < pieces * 2) pts.Add(rng.NextInt64(0, dur / Grid + 1) * Grid);
            var p = pts.ToList();
            for (int k = 0; k < pieces; k++)
            {
                int sp = rng.Next(6);
                var (sn, sd) = sp == 0 ? (3, 2) : sp == 1 ? (1, 2) : (1, 1);
                specs.Add(new Spec(m, p[2 * k], p[2 * k + 1], sn, sd, v, a));
            }
        }
        return (specs, durs);
    }

    private static List<PlanManualEdit> RandomEdits(Random rng, List<Spec> specs, Dictionary<Guid, long> durs, int count, bool allKinds, int startSeq)
    {
        var res = new List<PlanManualEdit>();
        var medias = durs.Keys.ToList();
        for (int k = 0; k < count; k++)
        {
            var m = medias[rng.Next(medias.Count)];
            long dur = durs[m];
            long a = rng.NextInt64(0, dur / Grid - 1) * Grid;
            long b = Math.Min(dur, a + Grid * (1 + rng.Next(20)));
            Guid? am = null; long? ai = null;
            if (rng.Next(10) < 7) { var s = specs[rng.Next(specs.Count)]; am = s.Media; ai = s.In; }
            int kind = rng.Next(allKinds ? 7 : 4);
            int seq = startSeq + k;
            res.Add(kind switch
            {
                0 => PlanManualEdit.Create(seq, ManualEditKinds.Exclude, m, a, b, introducedInRevision: 2),
                1 => PlanManualEdit.Create(seq, ManualEditKinds.Include, m, a, b, introducedInRevision: 2),
                2 => PlanManualEdit.Create(seq, ManualEditKinds.Include, m, a, b, introducedInRevision: 2),
                3 => PlanManualEdit.Create(seq, ManualEditKinds.Place, m, a, b, am, ai, introducedInRevision: 2),
                4 => PlanManualEdit.Create(seq, ManualEditKinds.Duplicate, m, a, b, am, ai, introducedInRevision: 2),
                5 => PlanManualEdit.Create(seq, ManualEditKinds.Property, m, a, b, speedNum: 3, speedDen: 2, introducedInRevision: 2),
                _ => PlanManualEdit.Create(seq, ManualEditKinds.Split, m, a, a, introducedInRevision: 2),
            });
        }
        return res;
    }

    private static bool Covers(IEnumerable<(long A, long B)> ranges, long a, long b)
    {
        long cur = a;
        foreach (var r in ranges.OrderBy(r => r.A)) { if (r.A <= cur && r.B > cur) cur = r.B; }
        return cur >= b;
    }

    private static void Check(EditPlanDto auto, IReadOnlyList<PlanManualEdit> edits, PlanApplyResult res, IReadOnlyDictionary<Guid, long> durs, string tag)
    {
        var plan = res.Plan!;
        var clips = plan.Clips;
        // I1: sequência contígua em [0, duração).
        long pos = 0;
        foreach (var (s, e) in clips.Select(c => (c.TimelineStartFrame, End: c.TimelineStartFrame + c.DurationFrames)).Distinct().OrderBy(x => x.TimelineStartFrame))
        { Assert.True(s == pos, $"{tag} I1 lacuna/sobreposição em {s} (esperado {pos})"); pos = e; }
        Assert.Equal(plan.Summary.TimelineDurationFrames, pos);
        // I2: sem sobreposição por faixa.
        foreach (var g in clips.GroupBy(c => c.TrackId))
        {
            long end = 0;
            foreach (var c in g.OrderBy(c => c.TimelineStartFrame)) { Assert.True(c.TimelineStartFrame >= end, $"{tag} I2 overlap"); end = c.TimelineStartFrame + c.DurationFrames; }
        }
        var byId = clips.ToDictionary(c => c.ClipId);
        foreach (var c in clips)
        {
            // I3: pares simétricos.
            if (c.LinkedClipId is Guid l)
            {
                var o = byId[l];
                Assert.True(o.LinkedClipId == c.ClipId && o.MediaId == c.MediaId && o.SourceInUs == c.SourceInUs && o.SourceOutUs == c.SourceOutUs
                    && o.TimelineStartFrame == c.TimelineStartFrame && o.DurationFrames == c.DurationFrames
                    && (long)o.SpeedNum * c.SpeedDen == (long)c.SpeedNum * o.SpeedDen, $"{tag} I3 par assimétrico");
            }
            // I4.
            Assert.True(c.DurationFrames > 0 && 0 <= c.SourceInUs && c.SourceInUs < c.SourceOutUs && c.SourceOutUs <= durs[c.MediaId], $"{tag} I4");
        }

        // Edições efetivas = aplicadas de fato.
        var appliedIds = res.Outcomes.Where(o => o.Status == "applied" && o.Code is null).Select(o => o.EditId).ToHashSet();
        var eff = ManualEdits.Compact(edits.Select(e => e.WithComputedId()).Where(e => appliedIds.Contains(e.EditId) && e.Kind is ManualEditKinds.Include or ManualEditKinds.Exclude));
        var clipRanges = clips.GroupBy(c => c.MediaId).ToDictionary(g => g.Key, g => g.Select(c => (c.SourceInUs, c.SourceOutUs)).ToList());
        var autoRanges = auto.Clips.GroupBy(c => c.MediaId).ToDictionary(g => g.Key, g => g.Select(c => (c.SourceInUs, c.SourceOutUs)).ToList());
        foreach (var e in eff.Where(e => e.Kind == ManualEditKinds.Exclude))   // I5
            Assert.DoesNotContain(clipRanges.GetValueOrDefault(e.MediaId) ?? new(), r => r.SourceInUs < e.SourceOutUs && r.SourceOutUs > e.SourceInUs);
        foreach (var e in eff.Where(e => e.Kind == ManualEditKinds.Include))   // I6
            Assert.True(Covers(clipRanges.GetValueOrDefault(e.MediaId) ?? new(), e.SourceInUs, e.SourceOutUs), $"{tag} I6 include nao contido");
        var allIncludes = edits.Where(e => e.Kind == ManualEditKinds.Include && appliedIds.Contains(e.WithComputedId().EditId)).ToList();
        foreach (var (m, rs) in clipRanges)                                    // I7
            foreach (var r in rs)
            {
                var allowed = (autoRanges.GetValueOrDefault(m) ?? new()).Concat(allIncludes.Where(i => i.MediaId == m).Select(i => (i.SourceInUs, i.SourceOutUs))).ToList();
                Assert.True(Covers(allowed, r.SourceInUs, r.SourceOutUs), $"{tag} I7 µs fora de (automatico ∪ include)");
            }

        // I10: aplicadas + conflitos = total; ClipIds únicos; seções contíguas.
        Assert.Equal(edits.Count, res.Outcomes.Count);
        Assert.Equal(clips.Count, byId.Count);
        var seq = clips.OrderBy(c => c.TimelineStartFrame).ThenBy(c => c.TrackId == "V1" ? 0 : 1).Select(c => c.SectionId).ToList();
        var closed = new HashSet<Guid?>();
        for (int i = 0; i < seq.Count; i++)
        {
            if (i > 0 && seq[i] != seq[i - 1]) closed.Add(seq[i - 1]);
            Assert.False(closed.Contains(seq[i]), $"{tag} I10 seção não contígua");
        }
        foreach (var sec in plan.Sections)
        {
            var cs = clips.Where(c => c.SectionId == sec.SectionId).ToList();
            Assert.True(cs.Count > 0 && cs.Min(c => c.TimelineStartFrame) == sec.StartFrame && cs.Max(c => c.TimelineStartFrame + c.DurationFrames) == sec.EndFrame, $"{tag} I10 limites da seção");
        }
    }

    [Fact]
    public void Invariants_I1toI10_HoldOverSeededRandomSequences()
    {
        var rng = new Random(20260607);
        int sequences = 0, roundtrips = 0, withSpeed = 0, audioOnly = 0;
        for (int iter = 0; iter < 700; iter++)
        {
            var (specs, durs) = RandomAuto(rng);
            var auto = Build(specs, durs);
            if (specs.Any(s => s.Sn != s.Sd)) withSpeed++;
            if (specs.Any(s => !s.V)) audioOnly++;
            var ctx = new PlanApplyContext(durs);
            var edits = RandomEdits(rng, specs, durs, 1 + rng.Next(8), allKinds: true, startSeq: 1);
            var res = PlanReconciler.Apply(auto, edits, Tb, ctx);
            Assert.False(res.Refused, $"iter {iter}: recusado: {string.Join(",", res.Refusals.Select(r => r.Code + ":" + r.Message))}");
            Check(auto, edits, res, durs, $"iter {iter}");
            sequences++;

            // I8: determinismo.
            Assert.Equal(JsonSerializer.Serialize(res.Plan), JsonSerializer.Serialize(PlanReconciler.Apply(auto, edits, Tb, ctx).Plan));

            // I9: Derive(r, r) = ∅ e o resultado equivale ao plano editado.
            var dom = PlanMapping.ToDomain(res.Plan!);
            Assert.Empty(ManualEditDeriver.DeriveDelta(dom, dom.Clips));
            var e1 = RandomEdits(rng, specs, durs, 1 + rng.Next(3), allKinds: false, startSeq: 1);
            var e2 = RandomEdits(rng, specs, durs, 1 + rng.Next(3), allKinds: false, startSeq: 10);
            var baseRes = PlanReconciler.Apply(auto, e1, Tb, ctx);
            var targetRes = PlanReconciler.Apply(auto, e1.Concat(e2).ToList(), Tb, ctx);
            if (baseRes.Refused || targetRes.Refused || baseRes.Outcomes.Any(o => o.Status != "applied") || targetRes.Outcomes.Any(o => o.Status != "applied")) continue;
            var all12 = e1.Concat(e2).ToList();
            // Limitação conhecida: re-incluir µs excluídos antes no mesmo overlay recolhe para "restaurar o automático" (perde a velocidade padrão do trecho re-incluído).
            if (all12.Any(x => x.Kind == ManualEditKinds.Exclude && all12.Any(y => y.Seq > x.Seq && y.Kind == ManualEditKinds.Include && y.MediaId == x.MediaId && y.SourceInUs < x.SourceOutUs && y.SourceOutUs > x.SourceInUs))) continue;
            var derived = ManualEditDeriver.Derive(PlanMapping.ToDomain(baseRes.Plan!), PlanMapping.ToDomain(targetRes.Plan!).Clips);
            var again = PlanReconciler.Apply(auto, derived, Tb, ctx);
            Assert.False(again.Refused, $"iter {iter}: derivado recusado");
            Assert.True(Canon(targetRes.Plan!) == Canon(again.Plan!), $"iter {iter} I9 roundtrip divergiu\nalvo:    {Canon(targetRes.Plan!)}\nderivado:{Canon(again.Plan!)}\nE1={Describe(e1)}\nE2={Describe(e2)}\nD={Describe(derived)}");
            roundtrips++;
        }
        Assert.True(sequences >= 500 && withSpeed > 100 && audioOnly > 100 && roundtrips > 100, $"cobertura: {sequences}/{withSpeed}/{audioOnly}/{roundtrips}");
    }

    private static string Describe(IEnumerable<PlanManualEdit> e)
        => string.Join(" ", e.Select(x => $"[{x.Kind} {x.MediaId.ToString("N")[^2..]} {x.SourceInUs / 1000}-{x.SourceOutUs / 1000} after={(x.AfterMediaId?.ToString("N")[^2..])}@{x.AfterSourceInUs / 1000}]"));
}
