namespace TravelCut.Domain.Planning;

/// <summary>
/// Validação pura do plano editorial. Erros bloqueiam (aplicação/exportação); avisos informam.
/// DURATION_MISMATCH: por faixa, clipes ordenados por início e CONTÍGUOS (início == fim do anterior) formam uma "corrida";
/// cada corrida é re-posicionada com <see cref="TimelineMath.LayoutSequential"/> a partir do início do seu primeiro clipe
/// e as durações recalculadas são comparadas. Se houver lacuna no meio da sequência original, o re-layout pode divergir
/// 1 frame por refazer o arredondamento a partir de outra origem (reportado só como warning).
/// </summary>
public static class PlanValidator
{
    public static IReadOnlyList<PlanIssue> Validate(
        PlanRevision plan,
        IReadOnlyDictionary<Guid, MediaFacts> media,
        IReadOnlyDictionary<Guid, IReadOnlyList<(long StartUs, long EndUs)>> speechByMedia,
        string currentMediaSetFingerprint)
    {
        var issues = new List<PlanIssue>();
        void Err(string code, string msg, Guid? clip = null) => issues.Add(new(PlanIssueSeverity.Error, code, msg, clip));
        void Warn(string code, string msg, Guid? clip = null) => issues.Add(new(PlanIssueSeverity.Warning, code, msg, clip));

        var byId = new Dictionary<Guid, PlanClip>();
        foreach (var c in plan.Clips) byId.TryAdd(c.ClipId, c);
        var trackIds = plan.Tracks.Select(t => t.TrackId).ToHashSet(StringComparer.Ordinal);

        foreach (var c in plan.Clips)
        {
            bool speedOk = c.SpeedNum > 0 && c.SpeedDen > 0;
            if (!speedOk) Err("INVALID_SPEED", $"Velocidade inválida {c.SpeedNum}/{c.SpeedDen}.", c.ClipId);
            if (c.SourceInUs >= c.SourceOutUs) Err("EMPTY_INTERVAL", "Intervalo de fonte vazio ou invertido.", c.ClipId);
            if (c.DurationFrames <= 0) Err("EMPTY_INTERVAL", "Duração na timeline <= 0 frames.", c.ClipId);
            if (c.TimelineStartFrame < 0) Err("NEGATIVE_POSITION", "Posição na timeline negativa.", c.ClipId);
            if (!trackIds.Contains(c.TrackId))
                Err("INVALID_TRACK", $"Clipe na faixa '{c.TrackId}', que não existe no plano.", c.ClipId);

            if (!media.TryGetValue(c.MediaId, out var mf))
                Err("MEDIA_MISSING", $"Mídia {c.MediaId} não encontrada.", c.ClipId);
            else if (c.SourceInUs < 0 || c.SourceOutUs > mf.DurationUs)
                Err("CLIP_OUTSIDE_MEDIA", "Intervalo fora da duração da mídia.", c.ClipId);

            // Velocidade ≠ 1 só é admissível com transcrição conhecida e trecho sem fala.
            // Sem entrada em speechByMedia a fala é desconhecida: não pode haver aceleração.
            if (speedOk && c.SpeedNum != c.SpeedDen && c.SourceInUs < c.SourceOutUs)
            {
                if (!speechByMedia.TryGetValue(c.MediaId, out var speech))
                    Err("SPEED_OVER_SPEECH", "Velocidade diferente de 1x em mídia com fala desconhecida (sem transcrição).", c.ClipId);
                else if (speech.Any(s => s.StartUs < c.SourceOutUs && s.EndUs > c.SourceInUs))
                    Err("SPEED_OVER_SPEECH", "Velocidade diferente de 1x sobre trecho com fala.", c.ClipId);
            }

            if (c.LinkedClipId is Guid lid)
            {
                if (!byId.TryGetValue(lid, out var o))
                    Err("LINK_MISMATCH", "Clipe vinculado inexistente.", c.ClipId);
                else
                {
                    if (o.LinkedClipId != c.ClipId) Err("LINK_MISMATCH", "Vínculo não recíproco.", c.ClipId);
                    if (o.MediaId != c.MediaId || o.SourceInUs != c.SourceInUs || o.SourceOutUs != c.SourceOutUs
                        || o.TimelineStartFrame != c.TimelineStartFrame || o.DurationFrames != c.DurationFrames
                        || (long)o.SpeedNum * c.SpeedDen != (long)c.SpeedNum * o.SpeedDen)
                        Err("LINK_MISMATCH", "Clipe vinculado dessincronizado (mídia/in/out/posição/duração/velocidade).", c.ClipId);
                }
            }
        }

        // Sobreposição e duração por faixa.
        foreach (var g in plan.Clips.GroupBy(c => c.TrackId, StringComparer.Ordinal))
        {
            var ordered = g.OrderBy(c => c.TimelineStartFrame).ThenBy(c => c.ClipId).ToList();
            var positive = ordered.Where(c => c.DurationFrames > 0).ToList();
            for (int i = 1; i < positive.Count; i++)
            {
                var prev = positive[i - 1]; var cur = positive[i];
                if (prev.TimelineStartFrame + prev.DurationFrames > cur.TimelineStartFrame)
                    Err("TRACK_OVERLAP", $"Clipe sobrepõe o anterior na faixa {g.Key}.", cur.ClipId);
            }
            CheckDurations(ordered, plan, Err, Warn);
        }

        // Transições.
        foreach (var t in plan.Transitions)
        {
            if (!byId.TryGetValue(t.FromClipId, out var a) || !byId.TryGetValue(t.ToClipId, out var b))
            { Err("TRANSITION_OUT_OF_BOUNDS", "Transição referencia clipe inexistente."); continue; }
            if (t.DurationFrames < 0 || t.DurationFrames * 2 > a.DurationFrames || t.DurationFrames * 2 > b.DurationFrames)
                Err("TRANSITION_OUT_OF_BOUNDS", "Transição maior que metade de um dos clipes.", a.ClipId);
            if (!AreAdjacent(plan, a, b))
                Err("TRANSITION_OUT_OF_BOUNDS", "Transição entre clipes não adjacentes na mesma faixa.", a.ClipId);
        }

        if (!string.Equals(plan.MediaSetFingerprint, currentMediaSetFingerprint, StringComparison.OrdinalIgnoreCase))
            Warn("MEDIA_CHANGED", "As mídias mudaram desde a geração do plano.");

        // Seções.
        long end = plan.Clips.Count == 0 ? 0 : plan.Clips.Max(c => c.TimelineStartFrame + Math.Max(0, c.DurationFrames));
        var secs = plan.Sections.OrderBy(s => s.StartFrame).ThenBy(s => s.Order).ToList();
        foreach (var s in secs)
            if (s.StartFrame < 0 || s.EndFrame > end || s.EndFrame <= s.StartFrame)
                Warn("SECTION_BOUNDS", $"Seção '{s.Title}' fora dos limites da timeline [0,{end}].");
        for (int i = 1; i < secs.Count; i++)
            if (secs[i - 1].EndFrame > secs[i].StartFrame)
                Warn("SECTION_BOUNDS", $"Seção '{secs[i].Title}' sobrepõe a anterior.");

        return issues;
    }

    private static bool AreAdjacent(PlanRevision plan, PlanClip a, PlanClip b)
    {
        if (!string.Equals(a.TrackId, b.TrackId, StringComparison.Ordinal)) return false;
        var ordered = plan.Clips.Where(c => c.TrackId == a.TrackId).OrderBy(c => c.TimelineStartFrame).ThenBy(c => c.ClipId).ToList();
        int ia = ordered.FindIndex(c => c.ClipId == a.ClipId);
        return ia >= 0 && ia + 1 < ordered.Count && ordered[ia + 1].ClipId == b.ClipId;
    }

    private static void CheckDurations(List<PlanClip> ordered, PlanRevision plan, Action<string, string, Guid?> err, Action<string, string, Guid?> warn)
    {
        var run = new List<PlanClip>();
        void Flush()
        {
            if (run.Count == 0) return;
            var layout = TimelineMath.LayoutSequential(
                run.Select(c => (c.SourceInUs, c.SourceOutUs, c.SpeedNum, c.SpeedDen)).ToList(), plan.TimeBase, run[0].TimelineStartFrame);
            for (int i = 0; i < run.Count; i++)
            {
                long diff = Math.Abs(run[i].DurationFrames - layout[i].DurationFrames);
                if (diff == 0) continue;
                var msg = $"durationFrames={run[i].DurationFrames} difere do recalculado ({layout[i].DurationFrames}).";
                if (diff <= 1) warn("DURATION_MISMATCH", msg, run[i].ClipId); else err("DURATION_MISMATCH", msg, run[i].ClipId);
            }
            run.Clear();
        }
        foreach (var c in ordered)
        {
            bool checkable = c.SpeedNum > 0 && c.SpeedDen > 0 && c.SourceOutUs >= c.SourceInUs;
            if (!checkable) { Flush(); continue; }
            if (run.Count > 0 && run[^1].TimelineStartFrame + run[^1].DurationFrames != c.TimelineStartFrame) Flush();
            run.Add(c);
        }
        Flush();
    }
}
