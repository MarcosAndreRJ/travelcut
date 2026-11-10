using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace TravelCut.Domain.Planning;

/// <summary>Tipos de edição manual (F14.7b). Todas ancoradas na FONTE (µs por mídia), nunca em frames de timeline.</summary>
public static class ManualEditKinds
{
    public const string Exclude = "exclude";
    public const string Include = "include";
    /// <summary>Ponto de divisão: SourceInUs == SourceOutUs == ponto.</summary>
    public const string Split = "split";
    public const string Place = "place";
    public const string Duplicate = "duplicate";
    public const string Property = "property";

    public static bool IsKnown(string? k) => k is Exclude or Include or Split or Place or Duplicate or Property;
}

/// <summary>
/// Edição manual do usuário reaplicada sobre cada plano automático regenerado (overlay aditivo; schema editplan/1 inalterado).
/// [SourceInUs, SourceOutUs) em µs da mídia. place/duplicate: posicionados DEPOIS do segmento que contém
/// (AfterMediaId, AfterSourceInUs); âncora nula = início da sequência. property: Zoom e/ou velocidade (SpeedNum/SpeedDen).
/// EditId é determinístico sobre o conteúdo canônico (não depende de Seq nem de IntroducedInRevision).
/// </summary>
public sealed record PlanManualEdit(Guid EditId, int Seq, string Kind, Guid MediaId, long SourceInUs, long SourceOutUs,
    Guid? AfterMediaId = null, long? AfterSourceInUs = null, double? Zoom = null, int? SpeedNum = null, int? SpeedDen = null,
    int IntroducedInRevision = 0)
{
    /// <summary>Cria a edição com EditId calculado a partir do conteúdo.</summary>
    public static PlanManualEdit Create(int seq, string kind, Guid mediaId, long inUs, long outUs, Guid? afterMediaId = null,
        long? afterInUs = null, double? zoom = null, int? speedNum = null, int? speedDen = null, int introducedInRevision = 0)
        => new(ComputeId(kind, mediaId, inUs, outUs, afterMediaId, afterInUs, zoom, speedNum, speedDen), seq, kind, mediaId,
            inUs, outUs, afterMediaId, afterInUs, zoom, speedNum, speedDen, introducedInRevision);

    public PlanManualEdit WithComputedId()
        => this with { EditId = ComputeId(Kind, MediaId, SourceInUs, SourceOutUs, AfterMediaId, AfterSourceInUs, Zoom, SpeedNum, SpeedDen) };

    /// <summary>UUID determinístico (SHA-256 truncado, versão 5-like) sobre o conteúdo canônico.</summary>
    public static Guid ComputeId(string kind, Guid mediaId, long inUs, long outUs, Guid? afterMediaId, long? afterInUs,
        double? zoom, int? speedNum, int? speedDen)
    {
        var inv = CultureInfo.InvariantCulture;
        var s = string.Join('|', "manual-edit", kind, mediaId.ToString("D"), inUs.ToString(inv), outUs.ToString(inv),
            afterMediaId?.ToString("D") ?? "-", afterInUs?.ToString(inv) ?? "-", zoom?.ToString("R", inv) ?? "-",
            speedNum?.ToString(inv) ?? "-", speedDen?.ToString(inv) ?? "-");
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(s));
        var bytes = new byte[16];
        Array.Copy(hash, bytes, 16);
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes);
    }

    /// <summary>Forma válida: intervalo coerente com o tipo, âncora completa ou ausente, property com algo a aplicar.</summary>
    public bool IsWellFormed()
    {
        if (!ManualEditKinds.IsKnown(Kind) || SourceInUs < 0) return false;
        if (Kind == ManualEditKinds.Split ? SourceOutUs != SourceInUs : SourceOutUs <= SourceInUs) return false;
        if ((AfterMediaId is null) != (AfterSourceInUs is null)) return false;
        if (Kind == ManualEditKinds.Property)
        {
            if (Zoom is null && SpeedNum is null && SpeedDen is null) return false;
            if ((SpeedNum is null) != (SpeedDen is null) || SpeedNum <= 0 || SpeedDen <= 0) return false;
        }
        return true;
    }
}

/// <summary>Álgebra pura do overlay: compactação (a edição mais recente vence) e forma canônica para o fingerprint.</summary>
public static class ManualEdits
{
    /// <summary>
    /// Forma canônica e mínima. include/exclude: por mídia, faixas disjuntas e ordenadas (por µs vence a de maior Seq; faixas
    /// adjacentes do mesmo tipo são fundidas) e vêm PRIMEIRO; depois split/place/duplicate/property na ordem de Seq, sem duplicatas
    /// (EditId repetido: fica a mais recente; property superados por outro posterior de faixa ⊇ são descartados).
    /// Seq é renumerado 1..n; IntroducedInRevision de faixas fundidas = máximo das contribuintes. Idempotente.
    /// LIMITAÇÃO CONHECIDA: exclude seguido de re-include do mesmo trecho vira "restore" com a velocidade/propriedades do plano automático
    /// (a velocidade anterior à exclusão não é lembrada); o estado final em µs é o mesmo, só as propriedades por trecho se perdem.
    /// </summary>
    public static IReadOnlyList<PlanManualEdit> Compact(IEnumerable<PlanManualEdit>? edits)
    {
        // A posição de uma inclusão depende da ordem da sequência (vizinho na fonte, adjacência), portanto include/exclude NÃO
        // atravessam edições que mexem na ordem/estrutura (split/place/duplicate/property): cada "época" é compactada à parte.
        var all = (edits ?? Enumerable.Empty<PlanManualEdit>()).Where(e => e.IsWellFormed())
            .Select((e, i) => (E: e, I: i)).OrderBy(x => x.E.Seq).ThenBy(x => x.I).Select(x => x.E).ToList();
        bool IsIe(PlanManualEdit e) => e.Kind is ManualEditKinds.Include or ManualEditKinds.Exclude;
        var keptRest = CompactCore(all.Where(e => !IsIe(e))).ToDictionary(e => e.EditId);
        var lastPos = new Dictionary<Guid, int>();
        for (int i = 0; i < all.Count; i++) if (!IsIe(all[i])) lastPos[all[i].WithComputedId().EditId] = i;
        var output = new List<PlanManualEdit>();
        var epoch = new List<PlanManualEdit>();
        for (int i = 0; i < all.Count; i++)
        {
            if (IsIe(all[i])) { epoch.Add(all[i]); continue; }
            output.AddRange(CompactCore(epoch)); epoch.Clear();
            var id = all[i].WithComputedId().EditId;
            if (lastPos[id] == i && keptRest.TryGetValue(id, out var kept)) output.Add(kept);
        }
        output.AddRange(CompactCore(epoch));
        return output.Select((e, i) => e with { Seq = i + 1 }).ToList();
    }

    private static IReadOnlyList<PlanManualEdit> CompactCore(IEnumerable<PlanManualEdit>? edits)
    {
        var all = (edits ?? Enumerable.Empty<PlanManualEdit>()).Where(e => e.IsWellFormed())
            .Select((e, i) => (E: e, I: i)).OrderBy(x => x.E.Seq).ThenBy(x => x.I).Select(x => x.E).ToList();
        var result = new List<PlanManualEdit>();

        foreach (var g in all.Where(e => e.Kind is ManualEditKinds.Include or ManualEditKinds.Exclude)
                     .GroupBy(e => e.MediaId).OrderBy(g => g.Key))
        {
            var list = g.ToList(); // ordem de Seq: o último que cobre o intervalo elementar vence
            var points = list.SelectMany(e => new[] { e.SourceInUs, e.SourceOutUs }).Distinct().OrderBy(p => p).ToList();
            string? curKind = null; long curStart = 0, curEnd = 0; int curRev = 0;
            void Flush()
            {
                if (curKind is not null)
                    result.Add(PlanManualEdit.Create(0, curKind, g.Key, curStart, curEnd, introducedInRevision: curRev));
                curKind = null;
            }
            for (int i = 0; i + 1 < points.Count; i++)
            {
                long a = points[i], b = points[i + 1];
                PlanManualEdit? winner = null;
                foreach (var e in list) if (e.SourceInUs <= a && e.SourceOutUs >= b) winner = e;
                if (winner is null) { Flush(); continue; }
                if (curKind == winner.Kind && curEnd == a) { curEnd = b; curRev = Math.Max(curRev, winner.IntroducedInRevision); }
                else { Flush(); curKind = winner.Kind; curStart = a; curEnd = b; curRev = winner.IntroducedInRevision; }
            }
            Flush();
        }

        var rest = all.Where(e => e.Kind is not (ManualEditKinds.Include or ManualEditKinds.Exclude)).Select(e => e.WithComputedId()).ToList();
        var kept = new List<PlanManualEdit>();
        for (int i = 0; i < rest.Count; i++)
        {
            var e = rest[i];
            bool superseded = false;
            for (int j = i + 1; j < rest.Count && !superseded; j++)
            {
                var l = rest[j];
                if (l.EditId == e.EditId) { superseded = true; break; }
                if (l.Kind != e.Kind || l.MediaId != e.MediaId || l.SourceInUs > e.SourceInUs || l.SourceOutUs < e.SourceOutUs) continue;
                superseded = e.Kind switch
                {
                    ManualEditKinds.Property => (l.Zoom is not null || e.Zoom is null) && (l.SpeedNum is not null || e.SpeedNum is null),
                    _ => false,
                };
            }
            if (!superseded) kept.Add(e);
        }
        result.AddRange(kept);
        return result.Select((e, i) => e with { Seq = i + 1 }).ToList();
    }
}
