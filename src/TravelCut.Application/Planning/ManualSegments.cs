using TravelCut.Domain.Planning;

namespace TravelCut.Application.Planning;

/// <summary>
/// Segmento editorial (F14.7b): um par V1/A1 vinculado, um segmento só-A1 (mídia só-áudio) ou só-V1 (vídeo sem áudio).
/// Unidade de trabalho do overlay de edições manuais; fonte em µs, [In, Out).
/// </summary>
internal sealed class Seg
{
    public Guid MediaId;
    public string RelativePath = "";
    public long In, Out;
    public int SpeedNum = 1, SpeedDen = 1;
    public Guid? SectionId;
    public bool HasV1, HasA1;
    public double? Zoom, ZFx, ZFy, ZDur;
    public string? ZCurve;
    public string Rationale = "";
    public List<string> Evidence = new();
    public List<Guid> SuggestionIds = new();
    public string Band = "high";
    public bool Review;
    /// <summary>Identidade no plano automático de origem (transições, pendências); OrigIn/OrigOut = recorte original (-1 se novo).</summary>
    public Guid? OrigV1, OrigA1;
    public long OrigIn = -1, OrigOut = -1;
    /// <summary>Cópia criada por uma edição "duplicate": o ClipId é sufixado pelo EditId.</summary>
    public Guid? DupEditId;

    public Seg Clone()
    {
        var c = (Seg)MemberwiseClone();
        c.Evidence = new List<string>(Evidence);
        c.SuggestionIds = new List<Guid>(SuggestionIds);
        return c;
    }

    public bool SameSpeed(Seg o) => (long)SpeedNum * o.SpeedDen == (long)o.SpeedNum * SpeedDen;
    public bool IsUnitSpeed => SpeedNum == SpeedDen;
}

internal static class SegOps
{
    public static List<Seg> Clone(List<Seg> s) => s.Select(x => x.Clone()).ToList();

    public static List<Seg> FromClips(IEnumerable<PlanClip> clips)
    {
        var list = clips.ToList();
        var byId = new Dictionary<Guid, PlanClip>();
        foreach (var c in list) byId.TryAdd(c.ClipId, c);
        var used = new HashSet<Guid>();
        var segs = new List<(long Start, Guid Id, Seg S)>();
        foreach (var v in list.Where(c => c.TrackId == EditPlanner.TrackV1).OrderBy(c => c.TimelineStartFrame).ThenBy(c => c.ClipId))
        {
            PlanClip? a = v.LinkedClipId is Guid l && byId.TryGetValue(l, out var ac) && ac.TrackId == EditPlanner.TrackA1
                && !used.Contains(ac.ClipId) ? ac : null;
            used.Add(v.ClipId);
            if (a is not null) used.Add(a.ClipId);
            segs.Add((v.TimelineStartFrame, v.ClipId, Make(v, a)));
        }
        foreach (var a in list.Where(c => c.TrackId == EditPlanner.TrackA1 && !used.Contains(c.ClipId)))
            segs.Add((a.TimelineStartFrame, a.ClipId, Make(null, a)));
        return segs.OrderBy(x => x.Start).ThenBy(x => x.Id).Select(x => x.S).ToList();
    }

    private static Seg Make(PlanClip? v, PlanClip? a)
    {
        var p = (v ?? a)!;
        return new Seg
        {
            MediaId = p.MediaId, RelativePath = p.RelativePath, In = p.SourceInUs, Out = p.SourceOutUs,
            SpeedNum = p.SpeedNum, SpeedDen = p.SpeedDen, SectionId = p.SectionId, HasV1 = v is not null, HasA1 = a is not null,
            Zoom = v?.Zoom, ZFx = v?.ZoomFocalPointX, ZFy = v?.ZoomFocalPointY, ZDur = v?.ZoomDurationSeconds, ZCurve = v?.ZoomCurve,
            Rationale = p.Rationale, Evidence = p.Evidence.ToList(), SuggestionIds = p.SuggestionIds.ToList(), Band = p.ConfidenceBand,
            Review = p.NeedsHumanReview, OrigV1 = v?.ClipId, OrigA1 = a?.ClipId, OrigIn = p.SourceInUs, OrigOut = p.SourceOutUs,
        };
    }

    // ---- Intervalos [a,b) ----

    public static List<(long A, long B)> Union(IEnumerable<(long A, long B)> ranges)
    {
        var res = new List<(long A, long B)>();
        foreach (var r in ranges.Where(r => r.B > r.A).OrderBy(r => r.A).ThenBy(r => r.B))
        {
            if (res.Count > 0 && r.A <= res[^1].B) res[^1] = (res[^1].A, Math.Max(res[^1].B, r.B));
            else res.Add(r);
        }
        return res;
    }

    /// <summary>from \ minus (ambos normalizados por Union).</summary>
    public static List<(long A, long B)> Subtract(IEnumerable<(long A, long B)> from, IEnumerable<(long A, long B)> minus)
    {
        var m = Union(minus);
        var res = new List<(long A, long B)>();
        foreach (var f in Union(from))
        {
            long cur = f.A;
            foreach (var x in m)
            {
                if (x.B <= cur) continue;
                if (x.A >= f.B) break;
                if (x.A > cur) res.Add((cur, x.A));
                cur = Math.Max(cur, x.B);
            }
            if (cur < f.B) res.Add((cur, f.B));
        }
        return res;
    }

    public static List<(long A, long B)> RangesOf(List<Seg> s, Guid media)
        => Union(s.Where(x => x.MediaId == media).Select(x => (x.In, x.Out)));

    // ---- Operações (mutam a lista recebida; o chamador trabalha numa cópia) ----

    public static bool Exclude(List<Seg> s, Guid m, long a, long b)
    {
        bool any = false;
        for (int i = 0; i < s.Count; i++)
        {
            var seg = s[i];
            if (seg.MediaId != m || seg.Out <= a || seg.In >= b) continue;
            any = true;
            var pieces = new List<Seg>();
            if (seg.In < a) { var l = seg.Clone(); l.Out = a; pieces.Add(l); }
            if (seg.Out > b) { var r = seg.Clone(); r.In = b; pieces.Add(r); }
            s.RemoveAt(i);
            s.InsertRange(i, pieces);
            i += pieces.Count - 1;
        }
        return any;
    }

    /// <summary>Insere a parte não coberta de [a,b) colada ao vizinho na fonte. False = mídia sem nenhum segmento (INCLUDE_UNANCHORED).</summary>
    public static bool Include(List<Seg> s, Guid m, long a, long b)
    {
        if (!s.Any(x => x.MediaId == m)) return false;
        foreach (var (u1, u2) in Subtract(new[] { (a, b) }, RangesOf(s, m)))
        {
            int li = -1;
            for (int i = 0; i < s.Count; i++)
                if (s[i].MediaId == m && s[i].Out <= u1 && (li < 0 || s[i].Out > s[li].Out
                    || (s[i].Out == s[li].Out && s[li].DupEditId is not null && s[i].DupEditId is null))) li = i;
            int ri = -1;
            for (int i = 0; i < s.Count; i++)
                if (s[i].MediaId == m && s[i].In >= u2 && (ri < 0 || s[i].In < s[ri].In
                    || (s[i].In == s[ri].In && s[ri].DupEditId is not null && s[i].DupEditId is null))) ri = i;

            if (li >= 0 && s[li].Out == u1 && s[li].IsUnitSpeed)
            {
                s[li].Out = u2;
                if (li + 1 < s.Count && s[li + 1].MediaId == m && s[li + 1].In == u2 && s[li + 1].IsUnitSpeed
                    && s[li + 1].HasV1 == s[li].HasV1 && s[li + 1].HasA1 == s[li].HasA1)
                {
                    s[li].Out = s[li + 1].Out;
                    s[li].Review |= s[li + 1].Review;
                    s[li].SuggestionIds = s[li].SuggestionIds.Concat(s[li + 1].SuggestionIds).Distinct().OrderBy(g => g).ToList();
                    s.RemoveAt(li + 1);
                }
            }
            else if (ri >= 0 && s[ri].In == u2 && s[ri].IsUnitSpeed && (li < 0 || s[li].Out != u1 || !s[li].IsUnitSpeed))
            {
                s[ri].In = u1;
            }
            else
            {
                var neighbor = li >= 0 ? s[li] : s[ri];
                var n = neighbor.Clone();
                n.In = u1; n.Out = u2; n.SpeedNum = 1; n.SpeedDen = 1;
                n.Zoom = n.ZFx = n.ZFy = n.ZDur = null; n.ZCurve = null;
                n.Rationale = "Trecho restaurado manualmente."; n.Evidence = new(); n.SuggestionIds = new(); n.Review = false;
                n.OrigV1 = n.OrigA1 = null; n.OrigIn = n.OrigOut = -1; n.DupEditId = null;
                s.Insert(li >= 0 ? li + 1 : ri, n);
            }
        }
        return true;
    }

    /// <summary>Divide em p o segmento que o contém estritamente. True quando dividiu.</summary>
    public static bool SplitAt(List<Seg> s, Guid m, long p)
    {
        bool any = false;
        for (int i = 0; i < s.Count; i++)
        {
            var seg = s[i];
            if (seg.MediaId != m || !(seg.In < p && p < seg.Out)) continue;
            var l = seg.Clone(); l.Out = p;
            var r = seg.Clone(); r.In = p;
            s[i] = l; s.Insert(i + 1, r);
            i++; any = true;
        }
        return any;
    }

    public enum MoveStatus { Ok, NothingToMove, AnchorLost, Partial }

    /// <summary>place (copy=false) ou duplicate (copy=true) de [a,b) depois do segmento que contém a âncora (nula = início).</summary>
    public static MoveStatus MoveOrCopy(List<Seg> s, Guid m, long a, long b, Guid? afterMedia, long? afterIn, bool copy, Guid editId)
    {
        SplitAt(s, m, a); SplitAt(s, m, b);
        var moving = s.Where(x => x.MediaId == m && x.In >= a && x.Out <= b).ToList();
        if (moving.Count == 0) return MoveStatus.NothingToMove;
        bool partial = Subtract(new[] { (a, b) }, moving.Select(x => (x.In, x.Out))).Count > 0;

        Seg? anchor = null;
        if (afterMedia is Guid am && afterIn is long ai)
        {
            anchor = s.FirstOrDefault(x => x.MediaId == am && x.In <= ai && ai < x.Out && (copy || !moving.Contains(x)));
            if (anchor is null) return MoveStatus.AnchorLost;
        }

        List<Seg> block;
        if (copy)
        {
            block = moving.Select(x => { var c = x.Clone(); c.DupEditId = editId; c.OrigV1 = c.OrigA1 = null; c.OrigIn = c.OrigOut = -1; return c; }).ToList();
        }
        else
        {
            block = moving;
            foreach (var x in moving) s.Remove(x);
        }
        int at = anchor is null ? 0 : s.IndexOf(anchor) + 1;
        var section = anchor is not null ? anchor.SectionId : (s.Count > 0 ? s[0].SectionId : block[0].SectionId);
        foreach (var x in block) x.SectionId = section; // clipe movido/duplicado herda a seção do predecessor
        s.InsertRange(at, block);
        return partial ? MoveStatus.Partial : MoveStatus.Ok;
    }

    /// <summary>property: zoom (1.0 = remover zoom) e/ou velocidade sobre [a,b). Devolve quantos segmentos mudaram.</summary>
    public static int SetProperty(List<Seg> s, Guid m, long a, long b, double? zoom, int? speedNum, int? speedDen)
    {
        SplitAt(s, m, a); SplitAt(s, m, b);
        int n = 0;
        foreach (var x in s.Where(x => x.MediaId == m && x.In >= a && x.Out <= b))
        {
            bool changed = false;
            if (zoom is double z && x.HasV1)
            {
                double? nz = Math.Abs(z - 1.0) < 1e-9 ? null : z;
                if (x.Zoom != nz) { x.Zoom = nz; x.ZFx = x.ZFy = x.ZDur = null; x.ZCurve = null; changed = true; }
            }
            if (speedNum is int sn && speedDen is int sd && !(x.SpeedNum == sn && x.SpeedDen == sd)) { x.SpeedNum = sn; x.SpeedDen = sd; changed = true; }
            if (changed) n++;
        }
        return n;
    }
}
