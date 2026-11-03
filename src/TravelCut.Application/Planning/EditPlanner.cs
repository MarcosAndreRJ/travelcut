using System.Globalization;
using TravelCut.Application.Services;
using TravelCut.Application.UseCases;
using TravelCut.Contracts.Dtos;
using TravelCut.Domain.Entities;
using TravelCut.Domain.Enums;
using TravelCut.Domain.Planning;
using TravelCut.Domain.ValueObjects;

namespace TravelCut.Application.Planning;

/// <summary>Snapshot completo de entrada do planejador. Carregado por um use case; o planejador não faz IO.</summary>
public sealed record PlanningInput(
    Guid ProjectId,
    EditorialProfileDto Profile,
    TimelineTimeBaseDto? RequestedTimeBase,
    IReadOnlyList<MediaAsset> Media,
    ChronologyReport Chronology,
    IReadOnlyDictionary<Guid, MediaAnalysisDto> Analyses,
    IReadOnlyList<EditSuggestion> Suggestions,
    IReadOnlyList<EditDecision> Decisions,
    TimeZoneInfo DisplayZone,
    string TimeZoneId);

/// <summary>Identidade e metadados da revisão a construir (o conteúdo do plano não depende de CreatedAtUtc).</summary>
public sealed record PlanBuildContext(Guid PlanId, int Revision, int? BaseRevision, DateTime CreatedAtUtc, string Author);

public sealed record PlanBuildResult(EditPlanDto? Plan, string? ErrorCode, string? ErrorMessage);

/// <summary>
/// Planejador determinístico do plano editorial (perfil "viagens" v1). Puro: mesmas entradas → mesmo conteúdo.
/// Regras: cronologia real; cortes aprovados + triviais de alta evidência; velocidade só com velocidade APROVADA e sem fala
/// (estrada gera apenas sugestão); zoom só como sugestão (pending, clip.zoom null); música apenas sugerida; V1/A1 vinculados;
/// transições conservadoras; trechos curtos são mantidos com revisão humana, salvo os que arredondam para 0 quadros.
/// Nada é enviado ao DaVinci nesta fase.
/// </summary>
public static class EditPlanner
{
    public const string Author = "planner-rules/1";
    public const string SchemaVersion = "editplan/1";
    public const string TrackV1 = "V1";
    public const string TrackA1 = "A1";
    public const int DissolveFrames = 12;
    public const long DissolveMinClipFrames = 48;
    public const string MusicSuggestionMessage = "Sugerir trilha de 2-Assets com licença conhecida";
    public const string NoMediaMessage = "Nenhuma mídia de vídeo ou áudio com metadados válidos para planejar.";

    /// <summary>Origem de corte aplicado: corte aprovado pelo usuário.</summary>
    public const string OriginApproved = "approved_by_user";

    /// <summary>Origem de corte aplicado: corte automático trivial de alta evidência.</summary>
    public const string OriginAuto = "auto_trivial";

    /// <summary>
    /// Único tipo de corte aplicado automaticamente (ADR-014): silêncio. Filler, repetição, falso início, prolongamento
    /// e pausa (heurística "high" não calibrada; docs/07 exige revisão) ficam como pendentes.
    /// </summary>
    public const string AutoCutKind = "silence";

    /// <summary>Origem de corte aplicado: trecho entre cortes que arredonda para 0 quadros na timeline (removido, registrado).</summary>
    public const string OriginResidualSubFrame = "residual_sub_frame";

    public const string ResidualSegmentKind = "residual_segment";
    public const string SpeedSuggestionKind = "speed_suggestion";
    public const string ZoomSuggestionKind = "zoom_suggestion";
    public const string SuggestionPendingKind = "suggestion_pending";
    public const string MediaExcludedKind = "media_excluded";

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private sealed record AppliedCutItem(Guid SuggestionId, long StartUs, long EndUs, string Kind, string Origin);

    private sealed record SpeedItem(Guid SuggestionId, long StartUs, long EndUs, int Factor);

    /// <summary>União de cortes sobrepostos na fonte (um intervalo removido).</summary>
    private sealed class CutGroup
    {
        public long Start;
        public long End;
        public readonly List<AppliedCutItem> Items = [];
    }

    /// <summary>
    /// Trecho entre fronteiras de corte/velocidade. Anchor = sugestão adjacente (origem registrada se o trecho for removido
    /// por arredondar para 0 quadros).
    /// </summary>
    private sealed class Piece
    {
        public long In;
        public long Out;
        public CutGroup? Leading;
        public CutGroup? Trailing;
        public Guid? Anchor;
        public bool GapBefore;
    }

    private sealed class MediaWork
    {
        public required MediaAsset Media { get; init; }
        public required int SessionIndex { get; init; }
        public required DateTime RecordedAtUtc { get; init; }
        public required string TimestampSource { get; init; }
        public required long DurationUs { get; init; }
        public required bool IsVideo { get; init; }
        public required bool HasAudioTrack { get; init; }
        public required bool SpeechKnown { get; init; }
        public required IReadOnlyList<(long StartUs, long EndUs)> Speech { get; init; }
        public required bool SceneAnalyzed { get; init; }
        public required string SceneCategory { get; init; }
        public required bool SceneHypothesis { get; init; }
        public readonly List<AppliedCutItem> Cuts = [];
        public readonly List<AppliedCutItem> SubFrameRemovals = [];
        public readonly List<SpeedItem> Speeds = [];
        public readonly List<CutGroup> Groups = [];
        /// <summary>Pendências desta mídia; só entram no plano se a mídia permanecer incluída.</summary>
        public readonly List<PlanPendingItem> Pending = [];
    }

    private sealed class Segment
    {
        public required MediaWork W { get; init; }
        public required Piece P { get; init; }
        public int SpeedNum { get; set; } = 1;
        public int SpeedDen { get; set; } = 1;
        public Guid? ApprovedSpeedId { get; set; }
        public string? SpeedLine { get; set; }
        public bool ZoomSuggested { get; set; }
        public double ZoomValue { get; set; }
        public bool Residual { get; set; }
        public string Band { get; set; } = "high";
        public bool NeedsReview { get; set; }
        public long StartFrame { get; set; }
        public long DurationFrames { get; set; }
        public Guid? V1Id { get; set; }
        public Guid? A1Id { get; set; }

        public List<AppliedCutItem> CutItems
        {
            get
            {
                var list = new List<AppliedCutItem>();
                if (P.Leading is not null) list.AddRange(P.Leading.Items);
                if (P.Trailing is not null && !ReferenceEquals(P.Trailing, P.Leading)) list.AddRange(P.Trailing.Items);
                return list;
            }
        }
    }

    public static PlanBuildResult Build(PlanningInput input, PlanBuildContext ctx)
    {
        // Regra crítica do perfil: reviewAmbiguous é sempre true, mesmo que a entrada traga false.
        var profile = input.Profile with { ReviewAmbiguous = true };
        var zone = input.DisplayZone;
        var pending = new List<PlanPendingItem>();
        int excludedCount = 0;

        // 1) Elegibilidade por metadados: demais mídias ficam registradas como excluídas (nunca silenciosas).
        var eligible = new List<MediaAsset>();
        foreach (var media in input.Media.DistinctBy(m => m.MediaId).OrderBy(m => m.RelativePath, StringComparer.Ordinal))
        {
            var reason = PlanEligibility.ExclusionReason(media);
            if (reason is null)
            {
                eligible.Add(media);
                continue;
            }
            excludedCount++;
            pending.Add(new PlanPendingItem(null, media.MediaId, MediaExcludedKind, $"{media.RelativePath}: {reason}"));
        }
        var eligibleById = eligible.ToDictionary(m => m.MediaId);

        // 2) Ordem cronológica real: recordedAt; empate por relativePath. Sessões vêm do ChronologyService.
        var sessionsByIndex = input.Chronology.Sessions.ToDictionary(s => s.SessionIndex);
        var ordered = input.Chronology.Sessions
            .SelectMany(s => s.Clips)
            .Where(c => eligibleById.ContainsKey(c.MediaId))
            .DistinctBy(c => c.MediaId)
            .OrderBy(c => AsUtc(c.RecordedAtUtc))
            .ThenBy(c => c.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var inChronology = ordered.Select(c => c.MediaId).ToHashSet();
        foreach (var media in eligible.Where(m => !inChronology.Contains(m.MediaId)))
        {
            excludedCount++;
            pending.Add(new PlanPendingItem(null, media.MediaId, MediaExcludedKind,
                $"{media.RelativePath}: fora da cronologia (sem data de gravação)"));
        }
        if (ordered.Count == 0)
        {
            return new PlanBuildResult(null, "NO_MEDIA_TO_PLAN", NoMediaMessage);
        }

        // 3) Base de tempo: pedido explícito ou moda das taxas das mídias incluídas.
        var timeBase = input.RequestedTimeBase is { } requested
            ? new RationalTimeBase(requested.FrameDurationNum, requested.FrameDurationDen)
            : TimelineMath.ChooseTimelineTimeBase(ordered.Select(c => (double?)eligibleById[c.MediaId].Metadata!.FrameRate));

        // 4) Trabalho por mídia: fala (transcrição) e cena (análise).
        var worksById = new Dictionary<Guid, MediaWork>();
        var works = new List<MediaWork>();
        foreach (var item in ordered)
        {
            var media = eligibleById[item.MediaId];
            input.Analyses.TryGetValue(media.MediaId, out var analysis);
            var scene = analysis?.Scene;
            bool speechKnown = PlanEligibility.SpeechKnown(analysis);
            var work = new MediaWork
            {
                Media = media,
                SessionIndex = item.SessionIndex,
                RecordedAtUtc = AsUtc(item.RecordedAtUtc),
                TimestampSource = item.TimestampSource,
                DurationUs = PlanEligibility.DurationUs(media),
                IsVideo = media.Kind == MediaKind.Video,
                HasAudioTrack = media.Kind == MediaKind.Audio
                    || (media.Kind == MediaKind.Video && media.Metadata!.AudioChannels > 0),
                SpeechKnown = speechKnown,
                Speech = speechKnown
                    ? PlanEligibility.SpeechIntervals(analysis!.Transcript!)
                    : Array.Empty<(long StartUs, long EndUs)>(),
                SceneAnalyzed = scene is not null,
                SceneCategory = scene is not null ? scene.Category.ToLowerInvariant() : "indeterminado",
                SceneHypothesis = scene?.IsHypothesis ?? true,
            };
            works.Add(work);
            worksById[media.MediaId] = work;
        }

        // 5) Sugestões: estado efetivo pela última decisão (serviço único, por Version).
        // Estado efetivo (status + intervalo): modify aprovado aplica o intervalo ajustado; o original vale sem decisão.
        var effectiveById = EffectiveSuggestionStatus.ResolveDetailed(input.Suggestions, input.Decisions);
        var pendingSpeedSuggestion = new HashSet<Guid>();
        var pendingMediaSpeed = new HashSet<Guid>();
        foreach (var s in input.Suggestions.OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.SuggestionId))
        {
            if (!worksById.TryGetValue(s.MediaId, out var w)) continue;
            var eff = effectiveById[s.SuggestionId];
            var status = eff.Status;
            if (status == SuggestionStatus.Rejected) continue;

            var kind = SuggestionStorageMapper.ResolveDtoKind(s);
            long a = Math.Clamp(eff.StartMs * 1000, 0, w.DurationUs);
            long b = Math.Clamp(eff.EndMs * 1000, 0, w.DurationUs);
            bool valid = b > a;

            switch (s.SuggestedAction)
            {
                case "cut":
                    if (!valid)
                    {
                        w.Pending.Add(SuggestionPending(s, "intervalo vazio ou fora da mídia: não aplicado."));
                    }
                    else if (status == SuggestionStatus.Approved)
                    {
                        w.Cuts.Add(new AppliedCutItem(s.SuggestionId, a, b, kind, OriginApproved));
                    }
                    else if (status == SuggestionStatus.Proposed && profile.AutoApplyTrivialCuts
                             && kind == AutoCutKind && !s.NeedsHumanReview && s.ConfidenceBand == "high")
                    {
                        w.Cuts.Add(new AppliedCutItem(s.SuggestionId, a, b, kind, OriginAuto));
                    }
                    else
                    {
                        w.Pending.Add(SuggestionPending(s, CutPendingMessage(status, kind, a, b)));
                    }
                    break;

                case "speed2x":
                case "speed4x":
                {
                    int factor = s.SuggestedAction == "speed4x" ? 4 : 2;
                    if (status == SuggestionStatus.Approved && valid)
                    {
                        w.Speeds.Add(new SpeedItem(s.SuggestionId, a, b, factor));
                    }
                    else
                    {
                        w.Pending.Add(SuggestionPending(s, $"velocidade {factor}× {DescribeStatus(status)}."));
                    }
                    break;
                }

                default:
                    w.Pending.Add(SuggestionPending(s, $"ação '{s.SuggestedAction}' ainda não é aplicada pelo plano v1."));
                    break;
            }
        }

        foreach (var w in works)
        {
            w.Groups.AddRange(BuildGroups(w.Cuts));
        }

        // 6) Trechos e velocidade. Velocidade ≠ 1 só com sugestão APROVADA e sem fala; estrada gera apenas sugestão.
        var segments = new List<Segment>();
        double zoomFactor = profile.ZoomStrength switch { "subtle" => 1.05, "medium" => 1.10, _ => 0 };

        foreach (var w in works)
        {
            foreach (var p in BuildPieces(w))
            {
                var seg = new Segment { W = w, P = p };
                ResolveSpeed(seg, w, profile, pendingSpeedSuggestion, pendingMediaSpeed);
                segments.Add(seg);
            }
        }

        // 7) Layout final e remoção apenas de trechos que arredondam para 0 quadros na sequência.
        // O arredondamento é cumulativo (LayoutSequential): a duração de cada trecho é a da timeline final.
        // Trechos com duração 0 saem (registrados como residual_sub_frame) e o layout é recalculado até estabilizar.
        IReadOnlyList<(long StartFrame, long DurationFrames)> layout;
        while (true)
        {
            layout = TimelineMath.LayoutSequential(
                segments.Select(s => (s.P.In, s.P.Out, s.SpeedNum, s.SpeedDen)).ToList(), timeBase, 0);
            var zeroIndexes = Enumerable.Range(0, segments.Count).Where(i => layout[i].DurationFrames == 0).ToList();
            if (zeroIndexes.Count == 0) break;

            foreach (var i in zeroIndexes)
            {
                var z = segments[i];
                z.W.SubFrameRemovals.Add(new AppliedCutItem(z.P.Anchor ?? Guid.Empty, z.P.In, z.P.Out, "cut", OriginResidualSubFrame));
            }
            for (int k = zeroIndexes.Count - 1; k >= 0; k--) segments.RemoveAt(zeroIndexes[k]);
        }

        // 8) Mídias incluídas: as que ficaram sem trecho viram media_excluded (com motivo).
        var includedWorks = works.Where(w => segments.Any(s => ReferenceEquals(s.W, w))).ToList();
        foreach (var w in works.Where(w => !includedWorks.Contains(w)))
        {
            excludedCount++;
            pending.Add(new PlanPendingItem(null, w.Media.MediaId, MediaExcludedKind,
                $"{w.Media.RelativePath}: nenhum trecho restou após os cortes (todos com 0 quadros na timeline)"));
        }
        if (segments.Count == 0)
        {
            return new PlanBuildResult(null, "NO_MEDIA_TO_PLAN", NoMediaMessage);
        }
        foreach (var w in includedWorks) pending.AddRange(w.Pending);

        // 9) Trechos residuais (< 0,25 s) são mantidos com revisão humana; GapBefore recalculado na sequência final.
        for (int k = 0; k < segments.Count; k++)
        {
            var seg = segments[k];
            seg.Residual = seg.P.Out - seg.P.In < PlanEligibility.MinSegmentUs;
            bool sameMediaPrev = k > 0 && ReferenceEquals(segments[k - 1].W, seg.W);
            seg.P.GapBefore = sameMediaPrev ? segments[k - 1].P.Out != seg.P.In : seg.P.In > 0;
        }

        // 10) Zoom: só sugestão. Segundo trecho de um jump cut em cena "conversa".
        if (zoomFactor > 0)
        {
            for (int k = 1; k < segments.Count; k++)
            {
                var cur = segments[k];
                var prev = segments[k - 1];
                if (cur.W.IsVideo && cur.W.SceneCategory == "conversa"
                    && ReferenceEquals(prev.W, cur.W) && cur.P.GapBefore)
                {
                    cur.ZoomSuggested = true;
                    cur.ZoomValue = zoomFactor;
                }
            }
        }

        // 11) Confiança e revisão por trecho.
        foreach (var seg in segments)
        {
            var items = seg.CutItems;
            bool autoCut = items.Any(c => c.Origin == OriginAuto);
            bool approvedCut = items.Any(c => c.Origin == OriginApproved);
            bool approvedSpeed = seg.ApprovedSpeedId is not null;
            bool automatic = autoCut || seg.ZoomSuggested;
            seg.Band = automatic ? "low" : (approvedCut || approvedSpeed) ? "medium" : "high";
            seg.NeedsReview = seg.ZoomSuggested || seg.Residual || (profile.ReviewAmbiguous && seg.Band == "low");
        }

        for (int i = 0; i < segments.Count; i++)
        {
            segments[i].StartFrame = layout[i].StartFrame;
            segments[i].DurationFrames = layout[i].DurationFrames;
        }

        // 12) Seções por sessão da cronologia (hipóteses até revisão humana).
        var sections = new List<PlanSection>();
        var sectionIdBySession = new Dictionary<int, Guid>();
        var sessionRanges = segments
            .GroupBy(s => s.W.SessionIndex)
            .Select(g => (Session: sessionsByIndex[g.Key], Segs: g.ToList()))
            .ToList();
        for (int i = 0; i < sessionRanges.Count; i++)
        {
            var (session, segs) = sessionRanges[i];
            var first = segs[0];
            var last = segs[^1];
            var sectionId = DeterministicIds.Create("section", input.ProjectId.ToString("D"),
                session.StartTimeUtc.Ticks.ToString(Inv), session.SessionIndex.ToString(Inv));
            sectionIdBySession[session.SessionIndex] = sectionId;

            string role = sessionRanges.Count == 1 ? "desenvolvimento"
                : i == 0 ? "abertura"
                : i == sessionRanges.Count - 1 ? "encerramento"
                : "desenvolvimento";

            // Horário vindo da data de modificação do arquivo (fallback) não é preciso: o título diz isso.
            bool approximateStart = segs.Any(s => s.W.TimestampSource == PlanEligibility.FallbackTimestampSource);
            string title = approximateStart
                ? $"Sessão {session.SessionIndex} — data aproximada (arquivo)"
                : $"Sessão {session.SessionIndex} — {LocalStamp(session.StartTimeUtc, zone)}";

            sections.Add(new PlanSection(
                sectionId,
                i + 1,
                role,
                title,
                SectionRationale(session, segs),
                true,
                first.StartFrame,
                last.StartFrame + last.DurationFrames));
        }

        // 13) Clipes: V1 para vídeo; A1 vinculado (mesmo recorte) para vídeo com áudio; só-áudio apenas em A1.
        // Zoom nunca vai para o clipe: a sugestão vai em pending "zoom_suggestion" com o clipId.
        var clips = new List<PlanClip>();
        foreach (var seg in segments)
        {
            var w = seg.W;
            var p = seg.P;
            var mediaId = w.Media.MediaId;
            Guid? sectionId = sectionIdBySession.TryGetValue(w.SessionIndex, out var sid) ? sid : null;
            var evidence = BuildEvidence(seg, zone);
            var rationale = BuildRationale(seg);
            var suggestionIds = new List<Guid>();
            suggestionIds.AddRange(seg.CutItems.Select(c => c.SuggestionId));
            if (seg.ApprovedSpeedId is Guid speedId) suggestionIds.Add(speedId);
            suggestionIds = suggestionIds.Distinct().OrderBy(g => g).ToList();

            Guid? v1Id = w.IsVideo
                ? DeterministicIds.Create("clip", TrackV1, mediaId.ToString("D"), p.In.ToString(Inv), p.Out.ToString(Inv))
                : null;
            Guid? a1Id = w.HasAudioTrack
                ? DeterministicIds.Create("clip", TrackA1, mediaId.ToString("D"), p.In.ToString(Inv), p.Out.ToString(Inv))
                : null;
            seg.V1Id = v1Id;
            seg.A1Id = a1Id;

            if (seg.ZoomSuggested && v1Id is Guid zoomClip)
            {
                pending.Add(new PlanPendingItem(null, mediaId, ZoomSuggestionKind,
                    $"Zoom {ZoomLabel(seg.ZoomValue)} ({profile.ZoomStrength}) sugerido: jump cut em conversa; revisar.", zoomClip));
            }
            if (seg.Residual)
            {
                pending.Add(new PlanPendingItem(null, mediaId, ResidualSegmentKind,
                    $"Trecho residual curto ({Seconds(p.Out - p.In, "0.000")} s) entre cortes: revisar.", v1Id ?? a1Id));
            }

            if (v1Id is Guid v)
            {
                clips.Add(new PlanClip(v, TrackV1, mediaId, w.Media.RelativePath, sectionId, p.In, p.Out,
                    seg.StartFrame, seg.DurationFrames, seg.SpeedNum, seg.SpeedDen, a1Id, null,
                    rationale, evidence, suggestionIds, seg.Band, seg.NeedsReview));
            }
            if (a1Id is Guid a)
            {
                clips.Add(new PlanClip(a, TrackA1, mediaId, w.Media.RelativePath, sectionId, p.In, p.Out,
                    seg.StartFrame, seg.DurationFrames, seg.SpeedNum, seg.SpeedDen, v1Id, null,
                    rationale, evidence, suggestionIds, seg.Band, seg.NeedsReview));
            }
        }

        // 14) Transições entre adjacentes na mesma faixa. Dissolve só entre seções e com ambos ≥ 48 frames.
        var transitions = new List<PlanTransition>();
        for (int k = 0; k + 1 < segments.Count; k++)
        {
            var a = segments[k];
            var b = segments[k + 1];
            if (a.V1Id is Guid va && b.V1Id is Guid vb)
            {
                bool dissolve = a.W.SessionIndex != b.W.SessionIndex
                    && a.DurationFrames >= DissolveMinClipFrames && b.DurationFrames >= DissolveMinClipFrames;
                transitions.Add(dissolve
                    ? new PlanTransition(va, vb, "dissolve", DissolveFrames, true)
                    : new PlanTransition(va, vb, "cut", 0, false));
            }
            if (a.A1Id is Guid aa && b.A1Id is Guid ab)
            {
                transitions.Add(new PlanTransition(aa, ab, "cut", 0, false));
            }
        }

        // 15) Cortes aplicados (aprovados, automáticos e remoções residuais) somente de mídias incluídas.
        var appliedCuts = new List<PlanAppliedCut>();
        foreach (var w in includedWorks)
        {
            foreach (var c in w.Cuts.Concat(w.SubFrameRemovals)
                         .OrderBy(c => c.StartUs).ThenBy(c => c.EndUs).ThenBy(c => c.SuggestionId))
            {
                appliedCuts.Add(new PlanAppliedCut(c.SuggestionId, w.Media.MediaId, c.StartUs, c.EndUs, "cut", c.Origin));
            }
        }

        if (profile.MusicPolicy == "suggest_only" && sections.Count >= 1)
        {
            pending.Add(new PlanPendingItem(null, null, "music_suggestion", MusicSuggestionMessage));
        }

        var tracks = new List<PlanTrack>
        {
            new(TrackV1, "video", "V1 — vídeo"),
            new(TrackA1, "dialogue", "A1 — áudio (diálogo)"),
        };

        // 16) Resumo derivado do plano (nunca estimado).
        long timelineFrames = clips.Count == 0 ? 0 : clips.Max(c => c.TimelineStartFrame + c.DurationFrames);
        var summary = new PlanSummaryDto(
            SourceDurationUs: includedWorks.Sum(w => w.DurationUs),
            TimelineDurationFrames: timelineFrames,
            RemovedSourceUs: includedWorks.Sum(w => w.Groups.Sum(g => g.End - g.Start) + w.SubFrameRemovals.Sum(c => c.EndUs - c.StartUs)),
            ApprovedCutsApplied: includedWorks.Sum(w => w.Cuts.Count(c => c.Origin == OriginApproved)),
            AutoCutsApplied: includedWorks.Sum(w => w.Cuts.Count(c => c.Origin == OriginAuto)),
            PendingSuggestions: pending.Count(p => p.Kind == SuggestionPendingKind),
            ExcludedMedia: excludedCount);

        // 17) Modelo de domínio, fingerprint e validação contra as mídias de entrada.
        var mediaSetFingerprint = PlanValidation.MediaSetFingerprintOf(input.Media);
        var domainPlan = new PlanRevision(
            ctx.PlanId, input.ProjectId, ctx.Revision, ctx.BaseRevision, ctx.CreatedAtUtc, ctx.Author,
            PlanMapping.ToProfileSnapshot(profile), timeBase, input.TimeZoneId, mediaSetFingerprint,
            tracks, sections, clips, transitions, appliedCuts, pending);

        var planFingerprint = PlanFingerprint.ComputePlan(domainPlan);
        var issues = PlanValidation.Run(domainPlan, input.Media, input.Analyses);
        var validation = PlanMapping.ToValidationDto(issues, ctx.CreatedAtUtc);

        var dto = PlanMapping.ToDto(domainPlan, summary, validation, planFingerprint);
        return new PlanBuildResult(dto, null, null);
    }

    // ------------------------------------------------------------------ Cortes e trechos

    private static List<CutGroup> BuildGroups(IEnumerable<AppliedCutItem> cuts)
    {
        var groups = new List<CutGroup>();
        foreach (var c in cuts.OrderBy(c => c.StartUs).ThenBy(c => c.EndUs).ThenBy(c => c.SuggestionId))
        {
            var last = groups.Count > 0 ? groups[^1] : null;
            if (last is not null && c.StartUs <= last.End)
            {
                last.End = Math.Max(last.End, c.EndUs);
                last.Items.Add(c);
            }
            else
            {
                var group = new CutGroup { Start = c.StartUs, End = c.EndUs };
                group.Items.Add(c);
                groups.Add(group);
            }
        }
        return groups;
    }

    /// <summary>
    /// Trechos entre cortes (base [0, duração)), divididos nas fronteiras de velocidades aprovadas.
    /// Os trechos cobrem a mídia sem lacunas: duração da mídia = soma dos trechos + soma dos cortes.
    /// </summary>
    private static List<Piece> BuildPieces(MediaWork w)
    {
        var raw = new List<Piece>();
        long prev = 0;
        CutGroup? before = null;
        foreach (var g in w.Groups)
        {
            if (g.Start > prev) raw.Add(new Piece { In = prev, Out = g.Start, Leading = before, Trailing = g });
            prev = g.End;
            before = g;
        }
        if (prev < w.DurationUs) raw.Add(new Piece { In = prev, Out = w.DurationUs, Leading = before });

        var pieces = new List<Piece>();
        foreach (var p in raw)
        {
            var bounds = new SortedSet<long> { p.In, p.Out };
            foreach (var sp in w.Speeds)
            {
                if (sp.StartUs > p.In && sp.StartUs < p.Out) bounds.Add(sp.StartUs);
                if (sp.EndUs > p.In && sp.EndUs < p.Out) bounds.Add(sp.EndUs);
            }
            var arr = bounds.ToArray();
            for (int i = 0; i + 1 < arr.Length; i++)
            {
                var piece = new Piece
                {
                    In = arr[i],
                    Out = arr[i + 1],
                    Leading = i == 0 ? p.Leading : null,
                    Trailing = i + 2 == arr.Length ? p.Trailing : null,
                };
                piece.Anchor = piece.Leading?.Items[0].SuggestionId
                    ?? piece.Trailing?.Items[0].SuggestionId
                    ?? SpeedAnchor(w, piece.In, piece.Out);
                pieces.Add(piece);
            }
        }
        return pieces;
    }

    /// <summary>Velocidade aprovada que delimita (ou contém) o trecho; usada como origem quando não há corte adjacente.</summary>
    private static Guid? SpeedAnchor(MediaWork w, long inUs, long outUs) =>
        w.Speeds
            .Where(sp => (sp.StartUs <= inUs && outUs <= sp.EndUs)
                || sp.StartUs == inUs || sp.StartUs == outUs || sp.EndUs == inUs || sp.EndUs == outUs)
            .OrderBy(sp => sp.SuggestionId)
            .Select(sp => (Guid?)sp.SuggestionId)
            .FirstOrDefault();

    /// <summary>
    /// Define velocidade e pendências de um trecho. Nunca altera SpeedNum/SpeedDen sem velocidade APROVADA:
    /// trecho de estrada sem velocidade aprovada gera apenas sugestão (pending "speed_suggestion") com motivo.
    /// </summary>
    private static void ResolveSpeed(
        Segment seg,
        MediaWork w,
        EditorialProfileDto profile,
        HashSet<Guid> pendingApprovedSpeeds,
        HashSet<Guid> pendingMediaSuggestions)
    {
        var p = seg.P;
        bool speechOverlap = w.SpeechKnown && PlanEligibility.Overlaps(w.Speech, p.In, p.Out);
        var covering = w.Speeds
            .Where(sp => sp.StartUs <= p.In && p.Out <= sp.EndUs)
            .OrderByDescending(sp => sp.Factor)
            .ThenBy(sp => sp.SuggestionId)
            .FirstOrDefault();

        if (covering is not null)
        {
            if (!w.SpeechKnown)
            {
                seg.SpeedLine = "Velocidade aprovada não aplicada: fala desconhecida (sem transcrição)";
                if (pendingApprovedSpeeds.Add(covering.SuggestionId))
                    w.Pending.Add(new PlanPendingItem(covering.SuggestionId, w.Media.MediaId, SpeedSuggestionKind,
                        "Velocidade aprovada não aplicada: sem transcrição, a fala é desconhecida."));
            }
            else if (speechOverlap)
            {
                seg.SpeedLine = "Velocidade aprovada não aplicada: há fala no trecho";
                if (pendingApprovedSpeeds.Add(covering.SuggestionId))
                    w.Pending.Add(new PlanPendingItem(covering.SuggestionId, w.Media.MediaId, SpeedSuggestionKind,
                        "Velocidade aprovada não aplicada: há fala no trecho."));
            }
            else
            {
                seg.SpeedNum = covering.Factor;
                seg.SpeedDen = 1;
                seg.ApprovedSpeedId = covering.SuggestionId;
                seg.SpeedLine = $"Velocidade {covering.Factor}× aprovada pelo usuário; sem fala no trecho";
            }
            return;
        }

        // Sem velocidade aprovada: estrada (hipótese de cena) gera só sugestão, conforme speedBroll ("off" = não sugerir).
        if (!w.IsVideo || w.SceneCategory != "estrada" || profile.SpeedBroll == "off") return;

        int factor = profile.SpeedBroll == "4x" ? 4 : 2;
        if (!w.SpeechKnown)
        {
            seg.SpeedLine = $"Velocidade {factor}× sugerida (cena estrada, hipótese): fala desconhecida (sem transcrição); não aplicada";
            if (pendingMediaSuggestions.Add(w.Media.MediaId))
                w.Pending.Add(new PlanPendingItem(null, w.Media.MediaId, SpeedSuggestionKind,
                    $"Trecho de estrada sem transcrição: velocidade {factor}× sugerida, não aplicada (fala desconhecida). Só entra no plano com aprovação."));
        }
        else if (speechOverlap)
        {
            seg.SpeedLine = "Velocidade não sugerida: há fala no trecho";
        }
        else
        {
            seg.SpeedLine = $"Velocidade {factor}× sugerida (cena estrada, hipótese; sem fala no trecho); não aplicada até aprovação";
            if (pendingMediaSuggestions.Add(w.Media.MediaId))
                w.Pending.Add(new PlanPendingItem(null, w.Media.MediaId, SpeedSuggestionKind,
                    $"Trecho de estrada (hipótese) sem fala: velocidade {factor}× sugerida, não aplicada. Só entra no plano com aprovação."));
        }
    }

    // ------------------------------------------------------------------ Textos (PT-BR) e evidências

    private static List<string> BuildEvidence(Segment seg, TimeZoneInfo zone)
    {
        var w = seg.W;
        var list = new List<string>
        {
            $"Cronologia: {LocalStamp(w.RecordedAtUtc, zone)} ({SourceLabel(w.TimestampSource)})",
            SceneLine(w),
            SpeechLine(w, seg.P),
        };
        foreach (var c in seg.CutItems)
        {
            var label = c.Origin == OriginApproved ? "Corte aprovado" : "Corte automático trivial";
            list.Add($"{label}: {KindLabel(c.Kind)} {Seconds(c.EndUs - c.StartUs, "0.0")} s");
        }
        if (seg.SpeedLine is not null) list.Add(seg.SpeedLine);
        if (seg.ZoomSuggested) list.Add($"Zoom {ZoomLabel(seg.ZoomValue)}: jump cut em conversa (sugestão)");
        if (seg.Residual) list.Add($"Trecho residual curto ({Seconds(seg.P.Out - seg.P.In, "0.000")} s) entre cortes");
        return list;
    }

    private static string BuildRationale(Segment seg)
    {
        var parts = new List<string> { $"Trecho de {seg.W.Media.RelativePath} (sessão {seg.W.SessionIndex})" };
        var items = seg.CutItems;
        if (items.Any(c => c.Origin == OriginApproved)) parts.Add("após corte aprovado");
        if (items.Any(c => c.Origin == OriginAuto)) parts.Add("após corte automático trivial");
        if (seg.ApprovedSpeedId is not null) parts.Add($"velocidade {seg.SpeedNum}× aprovada");
        if (seg.ZoomSuggested) parts.Add("zoom sutil sugerido (jump cut)");
        if (seg.Residual) parts.Add("trecho residual curto (revisão humana)");
        return string.Join("; ", parts) + ".";
    }

    private static string SectionRationale(ChronologySession session, List<Segment> segs)
    {
        var media = segs.Select(s => s.W).DistinctBy(w => w.Media.MediaId).ToList();
        int fallback = media.Count(w => w.TimestampSource == PlanEligibility.FallbackTimestampSource);
        string basis = fallback == 0
            ? $"horários pelos metadados ({SourceLabel(media[0].TimestampSource)})"
            : fallback == media.Count
                ? "horários vindos da data de modificação do arquivo (fallback), não confirmados"
                : "parte dos horários vem da data de modificação do arquivo (fallback), não confirmados";
        return $"Cronologia: sessão {session.SessionIndex}, {media.Count} mídia(s), {segs.Count} trecho(s); {basis}";
    }

    private static string SceneLine(MediaWork w)
    {
        if (!w.SceneAnalyzed) return "Cena: não analisada (indeterminado)";
        return w.SceneHypothesis ? $"Cena: {w.SceneCategory} (hipótese)" : $"Cena: {w.SceneCategory}";
    }

    private static string SpeechLine(MediaWork w, Piece p)
    {
        if (!w.SpeechKnown) return "Fala: desconhecida (sem transcrição)";
        return PlanEligibility.Overlaps(w.Speech, p.In, p.Out)
            ? "Fala: há fala no trecho (transcrição)"
            : "Fala: sem fala no trecho (transcrição)";
    }

    private static string SourceLabel(string source) =>
        source == PlanEligibility.FallbackTimestampSource ? "fallback: data de modificação do arquivo" : source;

    private static PlanPendingItem SuggestionPending(EditSuggestion s, string text) =>
        new(s.SuggestionId, s.MediaId, SuggestionPendingKind, $"Sugestão: {text}");

    private static string CutPendingMessage(SuggestionStatus status, string kind, long a, long b)
    {
        var range = $"{KindLabel(kind)} {Seconds(a, "0.0")}–{Seconds(b, "0.0")} s";
        return status switch
        {
            SuggestionStatus.Deferred => $"corte adiado ({range}): decisão pendente.",
            SuggestionStatus.Modified => $"corte modificado ({range}): revisar.",
            _ => $"corte sugerido ({range}) aguardando decisão.",
        };
    }

    private static string DescribeStatus(SuggestionStatus status) => status switch
    {
        SuggestionStatus.Approved => "aprovada, mas sem trecho aplicável",
        SuggestionStatus.Deferred => "adiada (aguardando decisão)",
        SuggestionStatus.Modified => "modificada: revisar",
        _ => "aguardando decisão",
    };

    private static string KindLabel(string kind) => kind switch
    {
        "silence" or "pause" => "silêncio",
        "hesitation" => "hesitação",
        "filler" => "preenchimento",
        "repetition" => "repetição",
        "false_start" => "recomeço",
        "prolongation" => "prolongamento",
        _ => kind,
    };

    private static string ZoomLabel(double zoom) => zoom.ToString("0.00", CultureInfo.InvariantCulture).Replace('.', ',');

    private static string Seconds(long us, string format) =>
        (us / 1_000_000d).ToString(format, CultureInfo.InvariantCulture).Replace('.', ',');

    private static string LocalStamp(DateTime utc, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTimeFromUtc(AsUtc(utc), zone).ToString("dd/MM HH:mm", CultureInfo.InvariantCulture);

    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };
}
