using System.Globalization;
using TravelCut.Contracts.Dtos;
using TravelCut.Domain.Planning;
using TravelCut.Domain.ValueObjects;

namespace TravelCut.Application.Planning;

/// <summary>Contexto opcional de <see cref="PlanReconciler.Apply"/>: duração das mídias (µs) e fala por mídia. Ausente: usa o máximo dos clipes do plano e não checa fala.</summary>
public sealed record PlanApplyContext(
    IReadOnlyDictionary<Guid, long>? MediaDurationUs = null,
    IReadOnlyDictionary<Guid, IReadOnlyList<(long StartUs, long EndUs)>>? SpeechByMedia = null);

/// <summary>Resultado por edição: Status "applied" (inclui no-op) ou "conflict" (não aplicada; ver Code).</summary>
public sealed record ManualEditOutcome(Guid EditId, int Seq, string Status, string? Code);

/// <summary>Plan nulo = recusado (Refusals lista os erros estruturais). Conflicts reúne bloqueios (edição não aplicada) e avisos.</summary>
public sealed record PlanApplyResult(
    EditPlanDto? Plan,
    IReadOnlyList<PlanConflictDto> Conflicts,
    IReadOnlyList<ManualEditOutcome> Outcomes,
    IReadOnlyList<PlanIssue> Refusals)
{
    public bool Refused => Plan is null;
}

public static partial class PlanReconciler
{
    public const string ConflictCutReopened = "CUT_REOPENED";
    public const string ConflictIncludeUnanchored = "INCLUDE_UNANCHORED";
    public const string ConflictOrderAnchorLost = "ORDER_ANCHOR_LOST";
    public const string ConflictTransitionDropped = "TRANSITION_DROPPED";
    public const string ConflictMediaRemoved = "MEDIA_REMOVED";
    public const string OriginManualEdit = "manual_edit";
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>
    /// Reaplica o overlay E sobre o plano automático (função pura, determinística). Apply(A, ∅) = A. Edições em ordem de Seq; a mais
    /// recente vence. Segmenta V1/A1 em pares (e só-A1/só-V1), aplica exclude/include/split/place/duplicate/property, re-posiciona por
    /// <see cref="TimelineMath.LayoutSequential"/> a partir do frame 0 (V1 e A1 no mesmo início/duração; duração 0 → residual_sub_frame) e
    /// recalcula ClipIds, seções, transições, cortes aplicados, efeitos, resumo e fingerprint. Recusa (Plan nulo) se a validação estrutural falhar.
    /// </summary>
    public static PlanApplyResult Apply(EditPlanDto autoPlan, IReadOnlyList<PlanManualEdit>? edits, RationalTimeBase timeBase, PlanApplyContext? ctx = null)
    {
        if (edits is null || edits.Count == 0) return new PlanApplyResult(autoPlan, Array.Empty<PlanConflictDto>(), Array.Empty<ManualEditOutcome>(), Array.Empty<PlanIssue>());

        var auto = PlanMapping.ToDomain(autoPlan);
        var cur = SegOps.FromClips(auto.Clips);
        var segsA = SegOps.Clone(cur);
        long DurOf(Guid m) => ctx?.MediaDurationUs is { } d && d.TryGetValue(m, out var v) ? v
            : auto.Clips.Where(c => c.MediaId == m).Select(c => c.SourceOutUs).DefaultIfEmpty(0).Max();

        var conflicts = new List<PlanConflictDto>();
        var outcomes = new List<ManualEditOutcome>();
        PlanConflictDto Conf(PlanManualEdit e, string type, string msg, string sev, string? res = null)
            => new(DeterministicIds.Create("conflict", e.EditId.ToString("D"), type), null, null, type, msg, sev, res);

        var ordered = edits.Select((e, i) => (E: e.WithComputedId(), I: i)).OrderBy(x => x.E.Seq).ThenBy(x => x.I).Select(x => x.E).ToList();
        var lastIndex = new Dictionary<Guid, int>();
        for (int i = 0; i < ordered.Count; i++) lastIndex[ordered[i].EditId] = i;
        var appliedExcludes = new List<PlanManualEdit>();

        for (int i = 0; i < ordered.Count; i++)
        {
            var e = ordered[i];
            if (!e.IsWellFormed()) { outcomes.Add(new(e.EditId, e.Seq, "conflict", "EDIT_MALFORMED")); conflicts.Add(Conf(e, "EDIT_MALFORMED", "Edição manual malformada; ignorada.", "warning")); continue; }
            if (lastIndex[e.EditId] != i) { outcomes.Add(new(e.EditId, e.Seq, "applied", "superseded_duplicate")); continue; }

            var trial = SegOps.Clone(cur);
            string? blocked = null;
            switch (e.Kind)
            {
                case ManualEditKinds.Exclude:
                    SegOps.Exclude(trial, e.MediaId, e.SourceInUs, e.SourceOutUs);
                    appliedExcludes.Add(e);
                    break;
                case ManualEditKinds.Include:
                    if (e.SourceOutUs > DurOf(e.MediaId)) { blocked = "INCLUDE_OUT_OF_MEDIA"; conflicts.Add(Conf(e, blocked, "O trecho a incluir passa do fim da mídia.", "warning")); break; }
                    var uncovered = SegOps.Subtract(new[] { (e.SourceInUs, e.SourceOutUs) }, SegOps.RangesOf(cur, e.MediaId));
                    if (!SegOps.Include(trial, e.MediaId, e.SourceInUs, e.SourceOutUs))
                    {
                        blocked = ConflictIncludeUnanchored;
                        conflicts.Add(Conf(e, blocked, "Trecho incluído sem segmento vizinho na mídia; posição não inventada.", "warning", "Reposicionar o trecho manualmente."));
                    }
                    else if (uncovered.Any(u => auto.AppliedCuts.Any(c => c.MediaId == e.MediaId && c.SourceInUs < u.B && c.SourceOutUs > u.A)))
                        conflicts.Add(Conf(e, ConflictCutReopened, "A inclusão manual venceu um corte aplicado pelo plano automático.", "warning", "Revisar o corte reaberto."));
                    break;
                case ManualEditKinds.Split:
                    SegOps.SplitAt(trial, e.MediaId, e.SourceInUs);
                    break;
                case ManualEditKinds.Place:
                case ManualEditKinds.Duplicate:
                    switch (SegOps.MoveOrCopy(trial, e.MediaId, e.SourceInUs, e.SourceOutUs, e.AfterMediaId, e.AfterSourceInUs, e.Kind == ManualEditKinds.Duplicate, e.EditId))
                    {
                        case SegOps.MoveStatus.NothingToMove:
                            blocked = cur.Any(x => x.MediaId == e.MediaId) ? ConflictCutReopened : ConflictMediaRemoved;
                            conflicts.Add(Conf(e, blocked, blocked == ConflictCutReopened
                                ? "Nada sobrou do trecho posicionado: o plano automático o removeu (corte reaberto)."
                                : "A mídia do trecho manual não está mais no plano automático.", "warning",
                                blocked == ConflictCutReopened ? "restaurar trecho" : "Manter para inspeção do editor."));
                            break;
                        case SegOps.MoveStatus.AnchorLost:
                            blocked = ConflictOrderAnchorLost;
                            conflicts.Add(Conf(e, blocked, "O segmento âncora da posição manual não existe mais; ordem automática mantida.", "warning"));
                            break;
                        case SegOps.MoveStatus.Partial:
                            conflicts.Add(Conf(e, ConflictCutReopened, "Parte do trecho posicionado foi cortada pelo plano automático; a posição manual encolheu.", "warning"));
                            break;
                    }
                    break;
                case ManualEditKinds.Property:
                    if (e.SpeedNum is int sn && e.SpeedDen is int sd && sn != sd
                        && ctx?.SpeechByMedia is { } sp && sp.TryGetValue(e.MediaId, out var speech)
                        && speech.Any(s => s.StartUs < e.SourceOutUs && s.EndUs > e.SourceInUs))
                    {
                        blocked = "SPEED_OVER_SPEECH";
                        conflicts.Add(Conf(e, blocked, "Velocidade diferente de 1x sobre trecho com fala.", "warning"));
                        break;
                    }
                    SegOps.SetProperty(trial, e.MediaId, e.SourceInUs, e.SourceOutUs, e.Zoom, e.SpeedNum, e.SpeedDen);
                    break;
            }
            if (blocked is null) { cur = trial; outcomes.Add(new(e.EditId, e.Seq, "applied", null)); }
            else outcomes.Add(new(e.EditId, e.Seq, "conflict", blocked));
        }

        // Ripple: V1 e A1 compartilham (início, duração); duração 0 → trecho removido e registrado (residual_sub_frame).
        var subFrame = new List<PlanAppliedCut>();
        IReadOnlyList<(long StartFrame, long DurationFrames)> layout;
        while (true)
        {
            layout = TimelineMath.LayoutSequential(cur.Select(x => (x.In, x.Out, x.SpeedNum, x.SpeedDen)).ToList(), timeBase, 0);
            int z = -1;
            for (int k = 0; k < layout.Count; k++) if (layout[k].DurationFrames <= 0) { z = k; break; }
            if (z < 0) break;
            subFrame.Add(new PlanAppliedCut(Guid.Empty, cur[z].MediaId, cur[z].In, cur[z].Out, "cut", EditPlanner.OriginResidualSubFrame));
            cur.RemoveAt(z);
        }

        // Seções contíguas: um clipe cuja seção reaparece depois de outra herda a do predecessor.
        var seen = new HashSet<Guid>();
        for (int k = 0; k < cur.Count; k++)
        {
            if (k > 0 && cur[k].SectionId is Guid sid && cur[k - 1].SectionId != sid && seen.Contains(sid)) cur[k].SectionId = cur[k - 1].SectionId;
            if (cur[k].SectionId is Guid s2) seen.Add(s2);
        }

        // ClipIds pela fórmula do planner (duplicatas sufixadas pelo EditId; colisões desambiguadas).
        var usedIds = new HashSet<Guid>();
        Guid MakeId(string track, Seg s)
        {
            var parts = new List<string> { "clip", track, s.MediaId.ToString("D"), s.In.ToString(Inv), s.Out.ToString(Inv) };
            if (s.DupEditId is Guid d) parts.Add(d.ToString("D"));
            var id = DeterministicIds.Create(parts.ToArray());
            for (int n = 1; !usedIds.Add(id); n++) id = DeterministicIds.Create(parts.Concat(new[] { "n" + n }).ToArray());
            return id;
        }
        var clips = new List<PlanClip>();
        var v1Ids = new Guid?[cur.Count];
        var a1Ids = new Guid?[cur.Count];
        for (int k = 0; k < cur.Count; k++)
        {
            var s = cur[k];
            if (s.HasV1) v1Ids[k] = MakeId(EditPlanner.TrackV1, s);
            if (s.HasA1) a1Ids[k] = MakeId(EditPlanner.TrackA1, s);
            var (start, dur) = layout[k];
            if (v1Ids[k] is Guid v)
                clips.Add(new PlanClip(v, EditPlanner.TrackV1, s.MediaId, s.RelativePath, s.SectionId, s.In, s.Out, start, dur, s.SpeedNum, s.SpeedDen,
                    a1Ids[k], s.Zoom, s.Rationale, s.Evidence, s.SuggestionIds, s.Band, s.Review, s.ZFx, s.ZFy, s.ZDur, s.ZCurve));
            if (a1Ids[k] is Guid a)
                clips.Add(new PlanClip(a, EditPlanner.TrackA1, s.MediaId, s.RelativePath, s.SectionId, s.In, s.Out, start, dur, s.SpeedNum, s.SpeedDen,
                    v1Ids[k], null, s.Rationale, s.Evidence, s.SuggestionIds, s.Band, s.Review));
        }

        var sections = new List<PlanSection>();
        foreach (var sec in auto.Sections.OrderBy(x => x.Order).ThenBy(x => x.SectionId))
        {
            var idx = Enumerable.Range(0, cur.Count).Where(k => cur[k].SectionId == sec.SectionId).ToList();
            if (idx.Count == 0) continue;
            sections.Add(sec with { Order = sections.Count + 1, StartFrame = layout[idx[0]].StartFrame, EndFrame = layout[idx[^1]].StartFrame + layout[idx[^1]].DurationFrames });
        }

        // Transições entre segmentos consecutivos (como o planner). Não-cut só sobrevive com a fronteira intacta e dentro dos limites.
        var transitions = new List<PlanTransition>();
        var keptNonCut = new HashSet<(Guid, Guid)>();
        for (int k = 0; k + 1 < cur.Count; k++)
        {
            foreach (var track in new[] { EditPlanner.TrackV1, EditPlanner.TrackA1 })
            {
                var from = track == EditPlanner.TrackV1 ? v1Ids[k] : a1Ids[k];
                var to = track == EditPlanner.TrackV1 ? v1Ids[k + 1] : a1Ids[k + 1];
                if (from is not Guid f || to is not Guid t) continue;
                var oa = track == EditPlanner.TrackV1 ? cur[k].OrigV1 : cur[k].OrigA1;
                var ob = track == EditPlanner.TrackV1 ? cur[k + 1].OrigV1 : cur[k + 1].OrigA1;
                var old = oa is Guid x && ob is Guid y && cur[k].DupEditId is null && cur[k + 1].DupEditId is null
                    && cur[k].Out == cur[k].OrigOut && cur[k + 1].In == cur[k + 1].OrigIn
                    ? auto.Transitions.FirstOrDefault(tr => tr.FromClipId == x && tr.ToClipId == y) : null;
                if (old is not null && old.Kind != "cut" && old.DurationFrames * 2 <= Math.Min(layout[k].DurationFrames, layout[k + 1].DurationFrames))
                {
                    transitions.Add(old with { FromClipId = f, ToClipId = t });
                    keptNonCut.Add((old.FromClipId, old.ToClipId));
                }
                else transitions.Add(new PlanTransition(f, t, "cut", 0, false));
            }
        }
        foreach (var tr in auto.Transitions.Where(tr => tr.Kind != "cut" && !keptNonCut.Contains((tr.FromClipId, tr.ToClipId))))
            conflicts.Add(new PlanConflictDto(DeterministicIds.Create("conflict", ConflictTransitionDropped, tr.FromClipId.ToString("D"), tr.ToClipId.ToString("D")),
                null, null, ConflictTransitionDropped, $"Transição {tr.Kind} removida: clipes não adjacentes ou fora dos limites após a edição manual.", "warning"));

        // Cortes aplicados: os do plano automático menos o que ficou mantido; mais exclusões manuais e remoções sub-frame.
        var medias = auto.Clips.Select(c => c.MediaId).Concat(cur.Select(c => c.MediaId)).Distinct().OrderBy(g => g).ToList();
        var keptA = medias.ToDictionary(m => m, m => SegOps.RangesOf(segsA, m));
        var keptR = medias.ToDictionary(m => m, m => SegOps.RangesOf(cur, m));
        var cuts = new List<PlanAppliedCut>();
        foreach (var c in auto.AppliedCuts)
        {
            var rest = SegOps.Subtract(new[] { (c.SourceInUs, c.SourceOutUs) }, keptR.TryGetValue(c.MediaId, out var kr) ? kr : new());
            cuts.AddRange(rest.Select(r => c with { SourceInUs = r.A, SourceOutUs = r.B }));
        }
        foreach (var e in appliedExcludes)
        {
            var gone = SegOps.Subtract(SegOps.Subtract(new[] { (e.SourceInUs, e.SourceOutUs) }, keptR.GetValueOrDefault(e.MediaId) ?? new()), SegOps.Subtract(new[] { (0L, long.MaxValue) }, keptA.GetValueOrDefault(e.MediaId) ?? new()));
            cuts.AddRange(gone.Select(r => new PlanAppliedCut(e.EditId, e.MediaId, r.A, r.B, "cut", OriginManualEdit)));
        }
        cuts.AddRange(subFrame);

        var ids = clips.Select(c => c.ClipId).ToHashSet();
        var map = new Dictionary<Guid, Guid>();
        for (int k = 0; k < cur.Count; k++)
        {
            if (cur[k].OrigV1 is Guid ov && v1Ids[k] is Guid nv) map.TryAdd(ov, nv);
            if (cur[k].OrigA1 is Guid oa2 && a1Ids[k] is Guid na) map.TryAdd(oa2, na);
        }
        var pending = auto.Pending.Where(p => p.ClipId is null || map.ContainsKey(p.ClipId.Value))
            .Select(p => p.ClipId is Guid cid ? p with { ClipId = map[cid] } : p).ToList();
        var known = cuts.Select(c => c.SuggestionId).Concat(transitions.Where(t => t.SuggestionId is not null).Select(t => t.SuggestionId!.Value))
            .Concat(clips.SelectMany(c => c.SuggestionIds)).ToHashSet();
        var effects = auto.AppliedEffects?.Where(x => known.Contains(x.SuggestionId)).ToList();

        long Sum(IEnumerable<(long A, long B)> r) => r.Sum(x => x.B - x.A);
        long removed = Math.Max(0, autoPlan.Summary.RemovedSourceUs + medias.Sum(m => Sum(keptA[m]) - Sum(keptR[m])));
        var summary = autoPlan.Summary with
        {
            TimelineDurationFrames = clips.Count == 0 ? 0 : clips.Max(c => c.TimelineStartFrame + c.DurationFrames),
            RemovedSourceUs = removed,
            PendingSuggestions = pending.Count(p => p.Kind == EditPlanner.SuggestionPendingKind),
        };

        var domain = auto with
        {
            TimeBase = timeBase, Sections = sections, Clips = clips, Transitions = transitions, AppliedCuts = cuts, Pending = pending,
            AppliedEffects = effects, ManualEdits = ordered,
        };
        var fingerprint = PlanFingerprint.ComputePlan(domain);

        var facts = medias.ToDictionary(m => m, m => new MediaFacts(m, DurOf(m), "", auto.Clips.FirstOrDefault(c => c.MediaId == m)?.RelativePath ?? ""));
        var speechMap = medias.ToDictionary(m => m,
            m => ctx?.SpeechByMedia is { } sm && sm.TryGetValue(m, out var sl) ? sl : (IReadOnlyList<(long StartUs, long EndUs)>)Array.Empty<(long, long)>());
        var issues = PlanValidator.Validate(domain, facts, speechMap, domain.MediaSetFingerprint);
        var errors = issues.Where(i => i.Severity == PlanIssueSeverity.Error).ToList();
        if (errors.Count > 0) return new PlanApplyResult(null, conflicts, outcomes, errors);

        var dto = PlanMapping.ToDto(domain, summary, PlanMapping.ToValidationDto(issues, autoPlan.Validation.ValidatedAtUtc), fingerprint);
        return new PlanApplyResult(dto, conflicts, outcomes, Array.Empty<PlanIssue>());
    }
}
