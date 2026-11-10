using TravelCut.Domain.Planning;

namespace TravelCut.Application.Planning;

/// <summary>
/// Deriva o overlay de edições manuais por DIFERENÇA DE ESTADO (não pelo log de operações): compara a revisão base com os clipes
/// editados, em µs por mídia. include = K_e \ K_b; exclude = K_b \ K_e; a ordem usa a subsequência crescente mais longa dos átomos
/// (o que fica de fora vira o mínimo de place); átomos repetidos viram duplicate; zoom/velocidade alterados viram property.
/// </summary>
public static class ManualEditDeriver
{
    /// <summary>Overlay completo = Compact(E_base ⊕ delta). Sem mudança de estado: devolve Compact(E_base) (∅ quando a base não tem overlay).</summary>
    public static IReadOnlyList<PlanManualEdit> Derive(PlanRevision basePlan, IReadOnlyList<PlanClip> editedClips, int? introducedInRevision = null)
        => ManualEdits.Compact((basePlan.ManualEdits ?? Array.Empty<PlanManualEdit>()).Concat(DeriveDelta(basePlan, editedClips, introducedInRevision)));

    /// <summary>Somente as edições novas implicadas por (base → editado). Derive(r, clipes de r) = ∅.</summary>
    public static IReadOnlyList<PlanManualEdit> DeriveDelta(PlanRevision basePlan, IReadOnlyList<PlanClip> editedClips, int? introducedInRevision = null)
    {
        var b = SegOps.FromClips(basePlan.Clips);
        var t = SegOps.FromClips(editedClips);
        int seq = basePlan.ManualEdits is { Count: > 0 } be ? be.Max(e => e.Seq) : 0;
        int rev = introducedInRevision ?? basePlan.Revision + 1;   // revisão REALMENTE atribuída ao gravar (pode ser > base+1 com revisão órfã em disco)
        var delta = new List<PlanManualEdit>();
        PlanManualEdit Add(string kind, Guid m, long a, long z, Guid? am = null, long? ai = null, double? zoom = null, int? sn = null, int? sd = null)
        {
            var e = PlanManualEdit.Create(++seq, kind, m, a, z, am, ai, zoom, sn, sd, rev);
            delta.Add(e);
            return e;
        }

        var medias = b.Concat(t).Select(x => x.MediaId).Distinct().OrderBy(g => g).ToList();
        var n = SegOps.Clone(b);
        foreach (var m in medias)
        {
            var kb = SegOps.RangesOf(b, m);
            var ke = SegOps.RangesOf(t, m);
            foreach (var (a, z) in SegOps.Subtract(kb, ke)) { Add(ManualEditKinds.Exclude, m, a, z); SegOps.Exclude(n, m, a, z); }
            foreach (var (a, z) in SegOps.Subtract(ke, kb)) { Add(ManualEditKinds.Include, m, a, z); SegOps.Include(n, m, a, z); }
        }

        // Átomos: ambas as sequências refinadas por todas as fronteiras de cada mídia.
        var bounds = medias.ToDictionary(m => m, m => n.Concat(t).Where(x => x.MediaId == m)
            .SelectMany(x => new[] { x.In, x.Out }).Distinct().OrderBy(p => p).ToList());
        List<Seg> Atomize(List<Seg> src)
        {
            var c = SegOps.Clone(src);
            foreach (var (m, pts) in bounds) foreach (var p in pts) SegOps.SplitAt(c, m, p);
            return c;
        }
        var na = Atomize(n);
        var ta = Atomize(t);

        var queues = new Dictionary<(Guid, long, long), Queue<int>>();
        var keysInN = new HashSet<(Guid, long, long)>();
        for (int i = 0; i < na.Count; i++)
        {
            var k = (na[i].MediaId, na[i].In, na[i].Out);
            keysInN.Add(k);
            if (!queues.TryGetValue(k, out var q)) queues[k] = q = new Queue<int>();
            q.Enqueue(i);
        }
        var nIdx = new int[ta.Count];
        var excess = new bool[ta.Count];
        for (int j = 0; j < ta.Count; j++)
        {
            var k = (ta[j].MediaId, ta[j].In, ta[j].Out);
            if (queues.TryGetValue(k, out var q) && q.Count > 0) nIdx[j] = q.Dequeue();
            else { nIdx[j] = -1; excess[j] = keysInN.Contains(k); }
        }

        // LIS estrita sobre os índices de N dos átomos casados, em ordem de T.
        var matched = Enumerable.Range(0, ta.Count).Where(j => nIdx[j] >= 0).ToList();
        var len = new int[matched.Count];
        var prev = new int[matched.Count];
        int best = -1;
        for (int i = 0; i < matched.Count; i++)
        {
            len[i] = 1; prev[i] = -1;
            for (int k = 0; k < i; k++)
                if (nIdx[matched[k]] < nIdx[matched[i]] && len[k] + 1 > len[i]) { len[i] = len[k] + 1; prev[i] = k; }
            if (best < 0 || len[i] > len[best]) best = i;
        }
        var staying = new HashSet<int>();
        for (int i = best; i >= 0; i = prev[i]) staying.Add(matched[i]);

        // Movidos (place) e repetidos (duplicate): grupos contíguos em T e na fonte.
        var sim = SegOps.Clone(n);
        int pos = 0;
        while (pos < ta.Count)
        {
            bool moved = nIdx[pos] >= 0 && !staying.Contains(pos);
            bool dup = excess[pos];
            if (!moved && !dup) { pos++; continue; }
            int end = pos;
            while (end + 1 < ta.Count && ((moved && nIdx[end + 1] >= 0 && !staying.Contains(end + 1)) || (dup && excess[end + 1]))
                   && ta[end + 1].MediaId == ta[end].MediaId && ta[end + 1].In == ta[end].Out) end++;
            var first = ta[pos]; var last = ta[end];
            Guid? am = null; long? ai = null;
            if (pos > 0)
            {
                var p = ta[pos - 1];
                am = p.MediaId; ai = p.In;
                if (SegOps.SplitAt(sim, p.MediaId, p.Out)) Add(ManualEditKinds.Split, p.MediaId, p.Out, p.Out);
            }
            var e = Add(moved ? ManualEditKinds.Place : ManualEditKinds.Duplicate, first.MediaId, first.In, last.Out, am, ai);
            SegOps.MoveOrCopy(sim, first.MediaId, first.In, last.Out, am, ai, copy: dup, e.EditId);
            pos = end + 1;
        }

        // Propriedades (zoom / velocidade) por átomo, agrupadas quando contíguas e iguais.
        (double? Zoom, int? Sn, int? Sd)? Prop(int j)
        {
            var src = nIdx[j] >= 0 ? na[nIdx[j]] : (excess[j] ? na[queuesFirst(j)] : null);
            if (src is null) return null;
            var tg = ta[j];
            double? z = tg.HasV1 && src.HasV1 && (tg.Zoom ?? 1.0) != (src.Zoom ?? 1.0) ? tg.Zoom ?? 1.0 : null;
            bool sp = !(tg.SpeedNum == src.SpeedNum && tg.SpeedDen == src.SpeedDen);
            return z is null && !sp ? null : (z, sp ? tg.SpeedNum : null, sp ? tg.SpeedDen : null);
        }
        int queuesFirst(int j)
        {
            for (int i = 0; i < na.Count; i++)
                if (na[i].MediaId == ta[j].MediaId && na[i].In == ta[j].In && na[i].Out == ta[j].Out) return i;
            return 0;
        }
        int q2 = 0;
        while (q2 < ta.Count)
        {
            var pr = Prop(q2);
            if (pr is null) { q2++; continue; }
            int end = q2;
            while (end + 1 < ta.Count && ta[end + 1].MediaId == ta[end].MediaId && ta[end + 1].In == ta[end].Out && Prop(end + 1) is { } nx && nx == pr.Value) end++;
            Add(ManualEditKinds.Property, ta[q2].MediaId, ta[q2].In, ta[end].Out, null, null, pr.Value.Zoom, pr.Value.Sn, pr.Value.Sd);
            q2 = end + 1;
        }
        return delta;
    }
}
