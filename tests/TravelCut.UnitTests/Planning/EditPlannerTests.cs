using TravelCut.Application.Planning;
using TravelCut.Contracts.Dtos;
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

        // 2,4 s (S1) + 0,5 s (S3) + 14,9 s (S7) = 17,8 s
        Assert.Equal(17_800_000, plan.Summary.RemovedSourceUs);
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
        Assert.Equal(2, V1Of(plan, 1).Count); // sem o corte de 40,0–40,5 s: dois trechos em vez de três
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

    [Fact]
    public void Zoom_IsSuggestedOnly_NeverSetOnClip()
    {
        // M1: clip.Zoom é sempre null; a sugestão vai em pending "zoom_suggestion" com clipId.
        var plan = Plan();

        var m1 = V1Of(plan, 1);
        Assert.Equal(3, m1.Count);
        Assert.All(plan.Clips, c => Assert.Null(c.Zoom));
        Assert.True(m1[1].NeedsHumanReview);
        Assert.True(m1[2].NeedsHumanReview);
        Assert.DoesNotContain(plan.Pending, p => p.ClipId == m1[0].ClipId && p.Kind == "zoom_suggestion");
        Assert.Contains(plan.Pending, p => p.Kind == "zoom_suggestion" && p.MediaId == M(1) && p.ClipId == m1[1].ClipId
            && p.Message.Contains("1,05", StringComparison.Ordinal) && p.Message.Contains("subtle", StringComparison.Ordinal));
        Assert.Contains(plan.Pending, p => p.Kind == "zoom_suggestion" && p.ClipId == m1[2].ClipId);
        Assert.Equal(2, plan.Pending.Count(p => p.Kind == "zoom_suggestion"));
    }

    [Fact]
    public void Zoom_IsNothing_WhenStrengthOff()
    {
        var profile = EditorialProfiles.Default() with { ZoomStrength = "off" };

        var plan = Plan(profile);

        Assert.All(plan.Clips, c => Assert.Null(c.Zoom));
        Assert.DoesNotContain(plan.Pending, p => p.Kind == "zoom_suggestion");
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
        Assert.Contains("Corte automático trivial: silêncio 0,5 s", afterApprovedCut.Evidence);
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
