using TravelCut.Application.Planning;
using TravelCut.Contracts.Dtos;
using TravelCut.Domain.Entities;
using TravelCut.Domain.Enums;
using TravelCut.Domain.Planning;
using TravelCut.Domain.ValueObjects;
using Xunit;

namespace TravelCut.UnitTests.Planning;

/// <summary>Planejador puro sobre a fixture de viagem (sem banco nem disco).</summary>
public class EditPlannerTests
{
    private static readonly RationalTimeBase Ntsc2997 = new(1001, 30000);

    private static Guid M(int n) => TravelFixture.M(n);
    private static Guid S(int n) => TravelFixture.S(n);

    private static EditPlanDto Plan(EditorialProfileDto? profile = null, TimelineTimeBaseDto? tb = null) =>
        TravelFixture.BuildPlan(TravelFixture.Input(profile: profile, timeBase: tb));

    private static List<PlanClipDto> V1Of(EditPlanDto plan, int mediaN) =>
        plan.Clips.Where(c => c.TrackId == "V1" && c.MediaId == M(mediaN)).OrderBy(c => c.TimelineStartFrame).ToList();

    // ------------------------------------------------------------------ Cronologia e seções

    [Fact]
    public void Sections_FollowChronology_WithRolesAndTitlesFromRealTime()
    {
        var plan = Plan();

        Assert.Equal(3, plan.Sections.Count);
        Assert.Equal(new[] { "abertura", "desenvolvimento", "encerramento" }, plan.Sections.Select(s => s.Role));
        Assert.Equal("Sessão 1 — 14/10 09:00", plan.Sections[0].Title);
        Assert.Equal("Sessão 2 — 14/10 11:00", plan.Sections[1].Title);
        // Sessão 3 usa a data de modificação do arquivo (fallback): título não pode parecer preciso (M8).
        Assert.Equal("Sessão 3 — data aproximada (arquivo)", plan.Sections[2].Title);
        Assert.All(plan.Sections, s => Assert.True(s.IsHypothesis));
        Assert.Equal(0, plan.Sections[0].StartFrame);
        Assert.True(plan.Sections[0].EndFrame <= plan.Sections[1].StartFrame);
        Assert.True(plan.Sections[1].EndFrame <= plan.Sections[2].StartFrame);
        // Sessão 3 usa a data de modificação do arquivo (sem creation_time): deve dizer isso.
        Assert.Contains("data de modificação do arquivo (fallback)", plan.Sections[2].Rationale);
    }

    [Fact]
    public void Clips_FollowChronologicalOrder()
    {
        var plan = Plan();

        var order = plan.Clips
            .Where(c => c.TrackId == "V1")
            .OrderBy(c => c.TimelineStartFrame)
            .Select(c => c.MediaId)
            .Distinct()
            .ToList();

        Assert.Equal(new[] { M(1), M(2), M(3), M(4), M(5), M(8) }, order);
    }

    [Fact]
    public void Chronology_TieAtSameInstant_IsBrokenByRelativePath()
    {
        var a = TravelFixture.Media().Single(m => m.MediaId == M(1));
        var b = TravelFixture.Media().Single(m => m.MediaId == M(2));
        b.Metadata!.CreationTimeUtc = a.Metadata!.CreationTimeUtc;
        a.RelativePath = "zeta.mp4";
        b.RelativePath = "alfa.mp4";

        var plan = TravelFixture.BuildPlan(TravelFixture.Input(media: new[] { a, b }));

        var first = plan.Clips.Where(c => c.TrackId == "V1").OrderBy(c => c.TimelineStartFrame).First();
        Assert.Equal("alfa.mp4", first.RelativePath);
    }

    // ------------------------------------------------------------------ Cortes

    [Fact]
    public void Cuts_OnlyApprovedAndTrivialAreApplied()
    {
        var plan = Plan();

        var applied = plan.AppliedCuts.Select(c => c.SuggestionId).OrderBy(x => x).ToList();
        Assert.Equal(new[] { S(1), S(3), S(7) }.OrderBy(x => x).ToList(), applied);
        Assert.Equal("approved_by_user", plan.AppliedCuts.Single(c => c.SuggestionId == S(1)).Origin);
        Assert.Equal("auto_trivial", plan.AppliedCuts.Single(c => c.SuggestionId == S(3)).Origin);
        Assert.Equal("approved_by_user", plan.AppliedCuts.Single(c => c.SuggestionId == S(7)).Origin);
        Assert.DoesNotContain(plan.AppliedCuts, c => c.SuggestionId == S(2)); // rejeitada
        Assert.DoesNotContain(plan.AppliedCuts, c => c.SuggestionId == S(4)); // proposta com revisão
        Assert.DoesNotContain(plan.AppliedCuts, c => c.SuggestionId == S(5)); // adiada
    }

    [Fact]
    public void RemovedSourceUs_IsExactSumOfAppliedCutsWithoutDoubleCounting()
    {
        var plan = Plan();

        // 2,4 s (S1) + 2,5 s (S3) + 14,9 s (S7) = 19,8 s
        Assert.Equal(19_800_000, plan.Summary.RemovedSourceUs);
        Assert.Equal(2, plan.Summary.ApprovedCutsApplied);
        Assert.Equal(1, plan.Summary.AutoCutsApplied);
    }

    [Fact]
    public void ShortResidualSegment_IsKept_WithHumanReviewEvidenceAndPending()
    {
        // B1: o trecho de 0,1 s (24,9–25 s do drone) NÃO é descartado: vira clipe com revisão humana.
        var plan = Plan();

        var drone = V1Of(plan, 5);
        Assert.Equal(2, drone.Count);
        var residual = drone[1];
        Assert.Equal(24_900_000, residual.SourceInUs);
        Assert.Equal(25_000_000, residual.SourceOutUs);
        Assert.True(residual.DurationFrames >= 1);
        Assert.True(residual.NeedsHumanReview);
        Assert.Contains("Trecho residual curto (0,100 s) entre cortes", residual.Evidence);
        Assert.Contains(plan.Pending, p => p.Kind == "residual_segment" && p.ClipId == residual.ClipId && p.MediaId == M(5));
        Assert.DoesNotContain(plan.AppliedCuts, c => c.Origin == "residual_sub_frame");
        Assert.DoesNotContain(plan.Clips, c => c.Evidence.Any(e => e.Contains("descartado")));
    }

    [Fact]
    public void TrivialCut_IsNotApplied_WhenAutoApplyIsDisabled()
    {
        var profile = EditorialProfiles.Default() with { AutoApplyTrivialCuts = false };

        var plan = Plan(profile);

        Assert.DoesNotContain(plan.AppliedCuts, c => c.SuggestionId == S(3));
        Assert.Equal(0, plan.Summary.AutoCutsApplied);
        Assert.Contains(plan.Pending, p => p.SuggestionId == S(3) && p.Kind == "suggestion_pending");
        Assert.Equal(2, V1Of(plan, 1).Count); // sem o corte de 40,0–42,5 s: dois trechos em vez de três
    }

    [Fact]
    public void Pending_ContainsProposedDeferredAndApprovedButUnappliedSuggestions()
    {
        var plan = Plan();

        Assert.Contains(plan.Pending, p => p.SuggestionId == S(4) && p.Kind == "suggestion_pending");
        Assert.Contains(plan.Pending, p => p.SuggestionId == S(5) && p.Kind == "suggestion_pending");
        Assert.Contains(plan.Pending, p => p.SuggestionId == S(6) && p.Kind == "speed_suggestion");
        Assert.DoesNotContain(plan.Pending, p => p.SuggestionId == S(2)); // rejeitada não vira pendência
    }

    // ------------------------------------------------------------------ Velocidade

    [Fact]
    public void EstradaWithKnownNoSpeech_IsOnlySuggested_NeverApplied()
    {
        // B2: sem velocidade APROVADA não há aceleração, mesmo com transcrição conhecida e sem fala.
        var plan = Plan();

        var clip = Assert.Single(V1Of(plan, 3));
        Assert.Equal(1, clip.SpeedNum);
        Assert.Equal(1, clip.SpeedDen);
        Assert.Contains("Cena: estrada (hipótese)", clip.Evidence);
        Assert.Contains(clip.Evidence, e => e.StartsWith("Velocidade 2× sugerida", StringComparison.Ordinal));
        Assert.DoesNotContain(clip.Evidence, e => e.StartsWith("Velocidade 2×:", StringComparison.Ordinal));
        Assert.Empty(clip.SuggestionIds); // nenhuma sugestão aprovada originou este clipe
        Assert.DoesNotContain(plan.Clips, c => c.SpeedNum != 1 || c.SpeedDen != 1);
        Assert.Contains(plan.Pending, p => p.Kind == "speed_suggestion" && p.MediaId == M(3) && p.SuggestionId is null
            && p.Message.Contains("não aplicada", StringComparison.Ordinal));
    }

    [Fact]
    public void NoAutomaticSpeed_AnywhereWithoutApproval()
    {
        // B2: nenhum clipe muda de velocidade sem sugestão aprovada (fixture tem estrada em M2, M3 e M4).
        var plan = Plan();

        Assert.All(plan.Clips, c => Assert.Equal((1, 1), (c.SpeedNum, c.SpeedDen)));
        Assert.All(plan.Clips, c => Assert.DoesNotContain(c.Evidence, e => e.StartsWith("Velocidade 2×:", StringComparison.Ordinal)));
    }

    [Fact]
    public void EstradaWithoutTranscript_StaysAt1x_AndReportsPending()
    {
        var plan = Plan();

        var clips = V1Of(plan, 4);
        Assert.Equal(2, clips.Count); // dividido pela velocidade aprovada em 20 s
        Assert.All(clips, c => Assert.Equal(1, c.SpeedNum));
        Assert.All(clips, c => Assert.Contains(c.Evidence, e => e.Contains("fala desconhecida")));
        Assert.Contains(plan.Pending, p => p.Kind == "speed_suggestion" && p.MediaId == M(4) && p.SuggestionId is null);
        Assert.Contains(plan.Pending, p => p.Kind == "speed_suggestion" && p.SuggestionId == S(6));
    }

    [Fact]
    public void Speed_IsNeverAppliedOverSpeech_EvenWhenApproved()
    {
        // Velocidade aprovada sobre trecho com fala (M2 tem fala em 1–6 s): não pode ser aplicada.
        var extra = new TravelCut.Domain.Entities.EditSuggestion
        {
            SuggestionId = S(9),
            MediaId = M(2),
            SourceInterval = new TimeRange(1000, 6000),
            EvidenceRefs = "{\"kind\":\"speed\"}",
            ConfidenceBand = "medium",
            NeedsHumanReview = true,
            SuggestedAction = "speed2x",
            Author = "test",
            CreatedAtUtc = new DateTime(2026, 10, 14, 9, 0, 0, DateTimeKind.Utc),
        };
        var decision = new TravelCut.Domain.Entities.EditDecision
        {
            DecisionId = TravelFixture.D(9),
            SuggestionId = S(9),
            Action = "approve",
            TimestampUtc = new DateTime(2026, 10, 14, 10, 30, 0, DateTimeKind.Utc),
        };

        var plan = TravelFixture.BuildPlan(TravelFixture.Input(
            suggestions: TravelFixture.Suggestions().Append(extra),
            decisions: TravelFixture.Decisions().Append(decision)));

        // Trechos que sobrepõem a fala (1–6 s) ficam em 1×. O trecho 0–1 s não tem fala, mas só aceleraria com
        // velocidade aprovada (B2): nesta fixture a velocidade aprovada em M2 cobre o trecho com fala e não é aplicada.
        var overSpeech = V1Of(plan, 2).Where(c => c.SourceInUs < 6_000_000 && c.SourceOutUs > 1_000_000).ToList();
        Assert.NotEmpty(overSpeech);
        Assert.All(overSpeech, c => Assert.Equal(1, c.SpeedNum));
        Assert.Contains(plan.Pending, p => p.SuggestionId == S(9) && p.Kind == "speed_suggestion");
        Assert.DoesNotContain(plan.Validation.Issues, i => i.Code == "SPEED_OVER_SPEECH");
    }

    [Fact]
    public void SpeedBroll4x_SuggestsFourTimes_ButNeverApplies()
    {
        var profile = EditorialProfiles.Default() with { SpeedBroll = "4x" };

        var plan = Plan(profile);

        Assert.Equal(1, Assert.Single(V1Of(plan, 3)).SpeedNum);
        Assert.Contains(plan.Pending, p => p.Kind == "speed_suggestion" && p.MediaId == M(3) && p.Message.Contains("4×", StringComparison.Ordinal));
        Assert.Equal(1, Assert.Single(V1Of(plan, 2)).SpeedNum); // fala: continua 1× e sem sugestão de velocidade automática
        Assert.DoesNotContain(plan.Pending, p => p.Kind == "speed_suggestion" && p.MediaId == M(2) && p.SuggestionId is null);
    }

    [Fact]
    public void SpeedBrollOff_DoesNotEvenSuggest()
    {
        var profile = EditorialProfiles.Default() with { SpeedBroll = "off" };

        var plan = Plan(profile);

        Assert.Equal(1, Assert.Single(V1Of(plan, 3)).SpeedNum);
        Assert.DoesNotContain(plan.Pending, p => p.Kind == "speed_suggestion" && p.MediaId == M(3));
    }

    // ------------------------------------------------------------------ Zoom, música, transições

    // E04: zoom só nasce de sugestões "zoom" (caminho único); aprovado/autoaplicado vira clip.zoom, o resto é pendência.

    private static EditSuggestion ZoomSug(int n, long startMs, long endMs, double scale = 1.05, bool focalKnown = true, string status = "proposed")
    {
        var p = new SmartZoomParametersDto(SmartZoomConstants.TypePunchIn, scale, 0.5, 0.5, (endMs - startMs) / 1000.0, 1.0, scale,
            "medium", focalKnown);
        return new EditSuggestion
        {
            SuggestionId = S(100 + n), MediaId = M(1), SourceInterval = new TimeRange(startMs, endMs),
            Kind = SuggestionKind.SubtleZoom, Rationale = "zoom", EvidenceRefs = System.Text.Json.JsonSerializer.Serialize(p),
            ConfidenceBand = "high", NeedsHumanReview = true, SuggestedAction = "zoom",
            Status = SuggestionStatus.Proposed, Author = "smart-zoom-engine",
            CreatedAtUtc = new DateTime(2026, 10, 14, 9, 30, n, DateTimeKind.Utc),
        };
    }

    private static EditDecision ZoomDecision(int n, string action, int seq) => new()
    {
        DecisionId = TravelFixture.D(100 + seq), SuggestionId = S(100 + n), Action = action, Reviewer = "user",
        TimestampUtc = new DateTime(2026, 10, 14, 10, 30, seq, DateTimeKind.Utc), Version = seq,
    };

    private static EditPlanDto ZoomPlan(IEnumerable<EditSuggestion> zooms, IEnumerable<EditDecision>? decisions = null, EditorialProfileDto? profile = null) =>
        TravelFixture.BuildPlan(TravelFixture.Input(
            profile: profile,
            suggestions: TravelFixture.Suggestions().Concat(zooms),
            decisions: TravelFixture.Decisions().Concat(decisions ?? [])));

    // M1 (60 s) tem clipes [0;10], [12,4;40] e [42,5;60] após os cortes S1 (aprovado) e S3 (auto).

    [Fact]
    public void Zoom_WithoutZoomSuggestion_NothingAppliedNorPending()
    {
        var plan = Plan();

        Assert.All(plan.Clips, c => Assert.Null(c.Zoom));
        Assert.DoesNotContain(plan.Pending, p => p.Kind == "zoom_suggestion");
    }

    [Fact]
    public void Zoom_ApprovedByUser_IsMaterializedOnV1Clip_AndUndoRevertsToPending()
    {
        var approved = ZoomPlan([ZoomSug(1, 12_400, 40_000)], [ZoomDecision(1, "approve", 1)]);
        var clip = V1Of(approved, 1)[1];
        Assert.Equal(1.05, clip.Zoom);
        Assert.Contains(S(101), clip.SuggestionIds);
        Assert.Contains(clip.Evidence, e => e.Contains("aprovado pelo usuário", StringComparison.Ordinal));
        Assert.DoesNotContain(approved.Pending, p => p.Kind == "zoom_suggestion");
        Assert.All(approved.Clips.Where(c => c.TrackId != "V1"), c => Assert.Null(c.Zoom));

        // Desfazer: volta a pendente e a política NÃO reaplica (decisão explícita no histórico).
        var undone = ZoomPlan([ZoomSug(1, 12_400, 40_000)], [ZoomDecision(1, "approve", 1), ZoomDecision(1, "undo", 2)]);
        Assert.All(undone.Clips, c => Assert.Null(c.Zoom));
        Assert.Contains(undone.Pending, p => p.Kind == "zoom_suggestion" && p.SuggestionId == S(101));

        // Idempotente: mesmas entradas, mesmo plano.
        var again = ZoomPlan([ZoomSug(1, 12_400, 40_000)], [ZoomDecision(1, "approve", 1)]);
        Assert.Equal(approved.Clips.Select(c => c.Zoom), again.Clips.Select(c => c.Zoom));
    }

    [Fact]
    public void Zoom_Rejected_IsNeitherAppliedNorPending()
    {
        var plan = ZoomPlan([ZoomSug(1, 12_400, 40_000)], [ZoomDecision(1, "reject", 1)]);

        Assert.All(plan.Clips, c => Assert.Null(c.Zoom));
        Assert.DoesNotContain(plan.Pending, p => p.Kind == "zoom_suggestion");
    }

    [Fact]
    public void Zoom_Balanced_AutoAppliesDiscreteZoomWithKnownFraming()
    {
        var plan = ZoomPlan([ZoomSug(1, 12_400, 40_000)]);

        var clip = V1Of(plan, 1)[1];
        Assert.Equal(1.05, clip.Zoom);
        Assert.Contains(clip.Evidence, e => e.Contains("aplicado automaticamente (origem=auto", StringComparison.Ordinal) && e.Contains("Zoom 1,05", StringComparison.Ordinal));
        Assert.DoesNotContain(plan.Pending, p => p.Kind == "zoom_suggestion");
        Assert.False(clip.NeedsHumanReview);
    }

    [Fact]
    public void Zoom_Conservative_NeverAutoApplies()
    {
        var profile = EditorialProfiles.Default() with { SilencePolicy = "conservative" };

        var plan = ZoomPlan([ZoomSug(1, 12_400, 40_000)], profile: profile);

        Assert.All(plan.Clips, c => Assert.Null(c.Zoom));
        Assert.Contains(plan.Pending, p => p.Kind == "zoom_suggestion" && p.SuggestionId == S(101));
    }

    [Fact]
    public void Zoom_StrengthOff_NeverAutoApplies_ButApprovedStillWins()
    {
        var off = EditorialProfiles.Default() with { ZoomStrength = "off" };

        var auto = ZoomPlan([ZoomSug(1, 12_400, 40_000)], profile: off);
        Assert.All(auto.Clips, c => Assert.Null(c.Zoom));
        Assert.Contains(auto.Pending, p => p.Kind == "zoom_suggestion");

        var manual = ZoomPlan([ZoomSug(1, 12_400, 40_000)], [ZoomDecision(1, "approve", 1)], off);
        Assert.Equal(1.05, V1Of(manual, 1)[1].Zoom); // decisão manual > config
    }

    [Fact]
    public void Zoom_UncertainFraming_GoesToReview()
    {
        var plan = ZoomPlan([ZoomSug(1, 12_400, 40_000, focalKnown: false)]);

        Assert.All(plan.Clips, c => Assert.Null(c.Zoom));
        Assert.Contains(plan.Pending, p => p.Kind == "zoom_suggestion" && p.SuggestionId == S(101) && p.ClipId == V1Of(plan, 1)[1].ClipId);
    }

    [Fact]
    public void Zoom_OverlappingAppliedCut_GoesToReview()
    {
        // 38–44 s cruza o corte automático S3 (40,0–42,5).
        var plan = ZoomPlan([ZoomSug(1, 38_000, 44_000)]);

        Assert.All(plan.Clips, c => Assert.Null(c.Zoom));
        Assert.Contains(plan.Pending, p => p.Kind == "zoom_suggestion");
    }

    [Fact]
    public void Zoom_StrongScaleOrShortDuration_GoesToReview_InBalanced()
    {
        // Perfil medium + escala 1,10 > 1,06 do Balanced; e zoom de 1,0 s < 1,5 s.
        var medium = EditorialProfiles.Default() with { ZoomStrength = "medium" };
        var strong = ZoomPlan([ZoomSug(1, 12_400, 40_000, scale: 1.10)], profile: medium);
        Assert.All(strong.Clips, c => Assert.Null(c.Zoom));

        var shortZoom = ZoomPlan([ZoomSug(2, 12_400, 13_400)]);
        Assert.All(shortZoom.Clips, c => Assert.Null(c.Zoom));
        Assert.Contains(shortZoom.Pending, p => p.Kind == "zoom_suggestion");
    }

    [Fact]
    public void Zoom_BalancedLimitsToOnePerWindow_AggressiveAllowsTwo()
    {
        // Dois clipes de M1 dentro de 20 s: [12,4;40] e [42,5;60] -> inicios 12,4 e 42,5 (30 s) não colidem; usa-se 2 zooms no mesmo clipe.
        var zooms = new[] { ZoomSug(1, 12_400, 40_000), ZoomSug(2, 12_400, 40_000) };

        var balanced = ZoomPlan(zooms);
        Assert.Equal(1.05, V1Of(balanced, 1)[1].Zoom);
        Assert.Single(balanced.Pending, p => p.Kind == "zoom_suggestion"); // o 2º no mesmo clipe fica pendente

        // Janela: zoom em clipes adjacentes próximos (0–10 s e 12,4–40 s: inícios a 12,4 s).
        var nearby = ZoomPlan([ZoomSug(1, 0, 10_000), ZoomSug(2, 12_400, 40_000)]);
        Assert.Equal(1.05, V1Of(nearby, 1)[0].Zoom);
        Assert.Null(V1Of(nearby, 1)[1].Zoom);
        Assert.Contains(nearby.Pending, p => p.Kind == "zoom_suggestion" && p.SuggestionId == S(102));

        var aggressive = EditorialProfiles.Default() with { SilencePolicy = "aggressive" };
        var wide = ZoomPlan([ZoomSug(1, 0, 10_000), ZoomSug(2, 12_400, 40_000)], profile: aggressive);
        Assert.Equal(1.05, V1Of(wide, 1)[0].Zoom);
        Assert.Equal(1.05, V1Of(wide, 1)[1].Zoom);
    }

    [Fact]
    public void Music_IsOnlySuggested_AndNeverPlacedOnA2()
    {
        var plan = Plan();

        Assert.Single(plan.Pending, p => p.Kind == "music_suggestion" && p.MediaId is null);
        Assert.DoesNotContain(plan.Tracks, t => t.TrackId == "A2");
        Assert.DoesNotContain(plan.Clips, c => c.TrackId == "A2");
    }

    [Fact]
    public void Music_IsAbsent_WhenPolicyOff()
    {
        var profile = EditorialProfiles.Default() with { MusicPolicy = "off" };

        var plan = Plan(profile);

        Assert.DoesNotContain(plan.Pending, p => p.Kind == "music_suggestion");
    }

    [Fact]
    public void Dissolve_OnlyBetweenSessions_WhenBothClipsHaveAtLeast48Frames()
    {
        var plan = Plan();

        var dissolve = Assert.Single(plan.Transitions, t => t.Kind == "dissolve");
        Assert.Equal(12, dissolve.DurationFrames);
        Assert.True(dissolve.NeedsHumanReview);
        var from = plan.Clips.Single(c => c.ClipId == dissolve.FromClipId);
        var to = plan.Clips.Single(c => c.ClipId == dissolve.ToClipId);
        Assert.Equal("V1", from.TrackId);
        Assert.Equal(M(3), from.MediaId);
        Assert.Equal(M(4), to.MediaId);
        Assert.All(plan.Transitions.Where(t => t.Kind == "cut"), t =>
        {
            Assert.Equal(0, t.DurationFrames);
            Assert.False(t.NeedsHumanReview);
        });
    }

    [Fact]
    public void Dissolve_IsReplacedByCut_WhenAClipHasFewerThan48Frames()
    {
        var media = TravelFixture.Media();
        media.Single(m => m.MediaId == M(3)).Metadata!.DurationSeconds = 1.2; // 0,6 s a 2× ≈ 18 frames

        var plan = TravelFixture.BuildPlan(TravelFixture.Input(media: media));

        Assert.DoesNotContain(plan.Transitions, t => t.Kind == "dissolve");
    }

    // ------------------------------------------------------------------ V1/A1, validação, resumo

    [Fact]
    public void V1AndA1_AreReciprocallyLinked_WithIdenticalTimingAndSpeed()
    {
        var plan = Plan();
        var byId = plan.Clips.ToDictionary(c => c.ClipId);

        var videoWithAudio = plan.Clips.Where(c => c.TrackId == "V1" && c.LinkedClipId is not null).ToList();
        Assert.Equal(8, videoWithAudio.Count);
        foreach (var v in videoWithAudio)
        {
            var a = byId[v.LinkedClipId!.Value];
            Assert.Equal("A1", a.TrackId);
            Assert.Equal(v.ClipId, a.LinkedClipId);
            Assert.Equal(v.MediaId, a.MediaId);
            Assert.Equal(v.SourceInUs, a.SourceInUs);
            Assert.Equal(v.SourceOutUs, a.SourceOutUs);
            Assert.Equal(v.TimelineStartFrame, a.TimelineStartFrame);
            Assert.Equal(v.DurationFrames, a.DurationFrames);
            Assert.Equal(v.SpeedNum, a.SpeedNum);
            Assert.Equal(v.SpeedDen, a.SpeedDen);
        }

        var audioOnly = Assert.Single(plan.Clips, c => c.MediaId == M(6));
        Assert.Equal("A1", audioOnly.TrackId);
        Assert.Null(audioOnly.LinkedClipId);
        Assert.DoesNotContain(plan.Clips, c => c.TrackId == "A1" && c.MediaId == M(5)); // drone sem áudio
        Assert.Equal(new[] { "V1", "A1" }, plan.Tracks.Select(t => t.TrackId));
    }

    [Fact]
    public void Fixture_ProducesValidPlan_WithoutErrors()
    {
        var plan = Plan();

        Assert.True(plan.Validation.IsValid);
        Assert.DoesNotContain(plan.Validation.Issues, i => i.Severity == "error");
        Assert.Equal(1001, plan.TimeBase.FrameDurationNum);
        Assert.Equal(30000, plan.TimeBase.FrameDurationDen);
        Assert.Equal("29,97 fps", plan.TimeBase.Label);
    }

    [Fact]
    public void RequestedTimeBase_IsUsed()
    {
        var plan = Plan(tb: new TimelineTimeBaseDto(1, 25, "25 fps"));

        Assert.Equal(1, plan.TimeBase.FrameDurationNum);
        Assert.Equal(25, plan.TimeBase.FrameDurationDen);
        Assert.Equal("25 fps", plan.TimeBase.Label);
    }

    [Fact]
    public void TimeBase_VfrMediaWithAverage2997_AndCfr2997_ChoosesNtsc2997()
    {
        // N3: M1 é VFR com taxa gravada (média) 29,97; as demais mídias incluídas são 29,97 CFR. Sem pedido explícito.
        var media = TravelFixture.Media();
        var vfr = media.Single(m => m.MediaId == M(1));
        vfr.Metadata!.FrameRate = 29.97;
        vfr.Metadata.IsVariableFrameRate = true;

        var plan = TravelFixture.BuildPlan(TravelFixture.Input(media: media));

        Assert.Equal(1001, plan.TimeBase.FrameDurationNum);
        Assert.Equal(30000, plan.TimeBase.FrameDurationDen);
        Assert.Contains(plan.Clips, c => c.MediaId == M(1));
    }

    [Fact]
    public void Summary_IsDerivedFromPlan()
    {
        var plan = Plan();

        Assert.Equal(235_000_000, plan.Summary.SourceDurationUs); // 60+40+30+40+25+20+20 s (sem a mídia sem metadados)
        Assert.Equal(plan.Clips.Max(c => c.TimelineStartFrame + c.DurationFrames), plan.Summary.TimelineDurationFrames);
        Assert.Equal(1, plan.Summary.ExcludedMedia);
        // M2: pendingSuggestions conta só "suggestion_pending" (S4 proposta e S5 adiada).
        Assert.Equal(plan.Pending.Count(p => p.Kind == "suggestion_pending"), plan.Summary.PendingSuggestions);
        Assert.Equal(2, plan.Summary.PendingSuggestions);
        Assert.Equal(plan.MediaSetFingerprint, PlanValidation.MediaSetFingerprintOf(TravelFixture.Media()));
    }

    [Fact]
    public void MediaWithoutMetadata_IsExcluded_WithPendingReason()
    {
        var plan = Plan();

        Assert.DoesNotContain(plan.Clips, c => c.MediaId == M(7));
        Assert.Contains(plan.Pending, p => p.Kind == "media_excluded" && p.MediaId == M(7)
            && p.Message.Contains("sem metadados do FFprobe", StringComparison.Ordinal));
    }

    [Fact]
    public void NoEligibleMedia_ReturnsNoMediaToPlan()
    {
        var onlyFailed = TravelFixture.Media().Where(m => m.MediaId == M(7)).ToList();

        var result = EditPlanner.Build(TravelFixture.Input(media: onlyFailed), TravelFixture.Context());

        Assert.Null(result.Plan);
        Assert.Equal("NO_MEDIA_TO_PLAN", result.ErrorCode);
    }

    [Fact]
    public void Evidence_IsPortuguese_AndCarriesRealTimeAndCutDetails()
    {
        var plan = Plan();

        var first = V1Of(plan, 1)[0];
        Assert.Contains("Cronologia: 14/10 09:00 (creation_time)", first.Evidence);
        var afterApprovedCut = V1Of(plan, 1)[1];
        Assert.Contains("Corte aprovado: silêncio 2,4 s", afterApprovedCut.Evidence);
        Assert.Contains("Corte automático trivial: silêncio 2,5 s", afterApprovedCut.Evidence);
    }

    [Fact]
    public void SameInputs_ProduceSameFingerprintAndIds()
    {
        var first = Plan();
        var second = Plan();

        Assert.Equal(first.PlanFingerprint, second.PlanFingerprint);
        Assert.Equal(first.Clips.Select(c => c.ClipId), second.Clips.Select(c => c.ClipId));
        Assert.Equal(first.Sections.Select(s => s.SectionId), second.Sections.Select(s => s.SectionId));
    }

    [Fact]
    public void ReviewAmbiguous_IsForcedTrue_InGeneratedPlan()
    {
        var profile = EditorialProfiles.Default() with { ReviewAmbiguous = false };

        var plan = Plan(profile);

        Assert.True(plan.Profile.ReviewAmbiguous);
    }
}
