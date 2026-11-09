using TravelCut.Application.Planning;
using TravelCut.Contracts.Dtos;
using TravelCut.Domain.Entities;
using TravelCut.Domain.Enums;
using TravelCut.Domain.ValueObjects;
using Xunit;

namespace TravelCut.UnitTests.Planning;

/// <summary>E05: transições (fade/dissolve/crossfade) materializadas no plano sobre as bordas reais dos clipes.</summary>
public class EditPlannerTransitionTests
{
    private static Guid M(int n) => TravelFixture.M(n);
    private static Guid S(int n) => TravelFixture.S(n);

    // Fronteiras da fixture em M1 (cortes S1 10,0–12,4 aprovado e S3 40,0–42,5 automático): 10,0→12,4 s e 40,0→42,5 s.
    private const long M1CutAOut = 10_000_000, M1CutAIn = 12_400_000, M1CutBOut = 40_000_000, M1CutBIn = 42_500_000;

    private static EditSuggestion Tr(int n, string kind, int fromMedia, long outUs, int toMedia, long inUs,
        SuggestionStatus status = SuggestionStatus.Proposed, int frames = 12) => new()
    {
        SuggestionId = S(n),
        MediaId = M(fromMedia),
        SourceInterval = new TimeRange(Math.Max(0, outUs / 1000 - 400), outUs / 1000),
        Kind = SuggestionKind.Transition,
        Rationale = "Transição de teste",
        EvidenceRefs = TransitionSupport.Serialize(new TransitionSuggestionParams(
            TransitionSupport.DtoKind, kind, frames, M(fromMedia), outUs, M(toMedia), inUs)),
        ConfidenceBand = "medium",
        NeedsHumanReview = true,
        SuggestedAction = "transition",
        Status = status,
        Author = "transition-recommender/2",
        CreatedAtUtc = new DateTime(2026, 10, 14, 9, 30, 0, DateTimeKind.Utc).AddSeconds(n),
    };

    private static EditSuggestion ApprovedCut(int n, int media, double startS, double endS) => new()
    {
        SuggestionId = S(n),
        MediaId = M(media),
        SourceInterval = new TimeRange((long)(startS * 1000), (long)(endS * 1000)),
        Kind = SuggestionKind.Silence,
        Rationale = "Corte de teste",
        EvidenceRefs = "{\"kind\":\"silence\"}",
        ConfidenceBand = "high",
        NeedsHumanReview = false,
        SuggestedAction = "cut",
        Status = SuggestionStatus.Approved,
        Author = "teste",
        CreatedAtUtc = new DateTime(2026, 10, 14, 9, 20, 0, DateTimeKind.Utc).AddSeconds(n),
    };

    private static EditDecision Dec(int n, int suggestionN, string action, long version) => new()
    {
        DecisionId = TravelFixture.D(n),
        SuggestionId = S(suggestionN),
        Action = action,
        Reviewer = "user",
        Notes = string.Empty,
        TimestampUtc = new DateTime(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc).AddSeconds(version),
        Version = (int)version,
    };

    private static EditorialProfileDto Profile(string policy) => EditorialProfiles.Default() with { SilencePolicy = policy };

    private static EditPlanDto Plan(IEnumerable<EditSuggestion> extra, string policy = "balanced", IEnumerable<EditDecision>? extraDecisions = null) =>
        TravelFixture.BuildPlan(TravelFixture.Input(
            profile: Profile(policy),
            suggestions: TravelFixture.Suggestions().Concat(extra),
            decisions: TravelFixture.Decisions().Concat(extraDecisions ?? [])));

    private static List<PlanClipDto> M1V1(EditPlanDto p) =>
        p.Clips.Where(c => c.TrackId == "V1" && c.MediaId == M(1)).OrderBy(c => c.TimelineStartFrame).ToList();

    private static PlanTransitionDto Between(EditPlanDto p, Guid from, Guid to) =>
        p.Transitions.Single(t => t.FromClipId == from && t.ToClipId == to);

    [Fact]
    public void ValidFade_IsAppliedByBalanced_WithAuditableOrigin_OnV1AndLinkedA1()
    {
        var plan = Plan([Tr(20, "fade", 1, M1CutAOut, 1, M1CutAIn)]);

        var clips = M1V1(plan);
        var v1 = Between(plan, clips[0].ClipId, clips[1].ClipId);
        Assert.Equal("fade", v1.Kind);
        Assert.Equal(12, v1.DurationFrames);
        Assert.Equal("auto", v1.Origin);
        Assert.Equal(S(20), v1.SuggestionId);
        Assert.Equal(AutoEditorialPolicy.Version, v1.PolicyVersion);
        Assert.False(v1.NeedsHumanReview);
        var a1 = Between(plan, clips[0].LinkedClipId!.Value, clips[1].LinkedClipId!.Value);
        Assert.Equal("fade", a1.Kind);
        var effect = Assert.Single(plan.AppliedEffects!, e => e.Kind == "transition");
        Assert.Equal("auto", effect.Origin);
        Assert.DoesNotContain(plan.Pending, p => p.Kind == "transition_suggestion");
        Assert.True(plan.Validation.IsValid);
    }

    [Fact]
    public void ValidFade_ConservativeOnlyReviews_AggressiveApplies()
    {
        var s = new[] { Tr(20, "fade", 1, M1CutAOut, 1, M1CutAIn) };

        var conservative = Plan(s, "conservative");
        Assert.DoesNotContain(conservative.Transitions, t => t.Kind == "fade");
        Assert.Contains(conservative.Pending, p => p.Kind == "transition_suggestion" && p.SuggestionId == S(20) && p.ClipId is not null);

        var aggressive = Plan(s, "aggressive");
        Assert.Contains(aggressive.Transitions, t => t.Kind == "fade" && t.Origin == "auto");
    }

    [Fact]
    public void Density_IsLimited_BalancedOnePerWindow_AggressiveTwo()
    {
        var s = new[] { Tr(20, "fade", 1, M1CutAOut, 1, M1CutAIn), Tr(21, "fade", 1, M1CutBOut, 1, M1CutBIn) };

        var balanced = Plan(s, "balanced");
        Assert.Single(balanced.Transitions, t => t.Kind == "fade" && t.Origin == "auto" && t.FromClipId != Guid.Empty
            && balanced.Clips.Any(c => c.ClipId == t.FromClipId && c.TrackId == "V1"));
        // A segunda fica como corte seco: Skip não polui a fila de pendências.
        Assert.DoesNotContain(balanced.Pending, p => p.Kind == "transition_suggestion");

        var aggressive = Plan(s, "aggressive");
        Assert.Equal(2, aggressive.Transitions.Count(t => t.Kind == "fade" && aggressive.Clips.Any(c => c.ClipId == t.FromClipId && c.TrackId == "V1")));
    }

    [Fact]
    public void DissolveWithoutHandles_IsSkipped_AndHumanApprovalDoesNotMaterializeIt()
    {
        // M2 termina no fim do arquivo (cauda 0) e M3 começa em 0 (cabeça 0): sem handles.
        var proposed = Plan([Tr(20, "dissolve", 2, 40_000_000, 3, 0)]);
        Assert.DoesNotContain(proposed.Transitions, t => t.Kind == "dissolve" && t.SuggestionId == S(20));
        Assert.DoesNotContain(proposed.Pending, p => p.SuggestionId == S(20));

        var approved = Plan([Tr(20, "dissolve", 2, 40_000_000, 3, 0, SuggestionStatus.Approved)]);
        Assert.DoesNotContain(approved.Transitions, t => t.SuggestionId == S(20));
        Assert.Contains(approved.Pending, p => p.SuggestionId == S(20) && p.Kind == "transition_suggestion" && p.Message.Contains("handles"));
    }

    [Fact]
    public void CrossFileDissolveWithHandles_GoesToReview_AndHumanApprovalMaterializesDissolveAndCrossfade()
    {
        var cuts = new[] { ApprovedCut(30, 1, 58.5, 60.0), ApprovedCut(31, 2, 0.0, 0.5) };
        var proposal = Tr(20, "dissolve", 1, 58_500_000, 2, 500_000);

        var review = Plan(cuts.Append(proposal));
        Assert.DoesNotContain(review.Transitions, t => t.SuggestionId == S(20));
        Assert.Contains(review.Pending, p => p.SuggestionId == S(20) && p.Kind == "transition_suggestion");

        var human = Plan(cuts.Append(Tr(20, "dissolve", 1, 58_500_000, 2, 500_000, SuggestionStatus.Approved)));
        var applied = human.Transitions.Where(t => t.SuggestionId == S(20)).ToList();
        Assert.Equal(2, applied.Count);
        Assert.Contains(applied, t => t.Kind == "dissolve" && t.Origin == "human" && t.PolicyVersion is null);
        Assert.Contains(applied, t => t.Kind == "crossfade" && t.Origin == "human");
        Assert.DoesNotContain(human.Pending, p => p.SuggestionId == S(20));
    }

    [Fact]
    public void ManualDecision_BeatsPolicy_AndUndoRestoresPlanWithoutReapplying()
    {
        var s = new[] { Tr(20, "fade", 1, M1CutAOut, 1, M1CutAIn) };
        var baseline = Plan(s, "conservative");

        var approved = Plan(s, "conservative", [Dec(40, 20, "approve", 100)]);
        Assert.Contains(approved.Transitions, t => t.Kind == "fade" && t.Origin == "human" && t.SuggestionId == S(20));
        Assert.NotEqual(baseline.PlanFingerprint, approved.PlanFingerprint);

        var undone = Plan(s, "conservative", [Dec(40, 20, "approve", 100), Dec(41, 20, "undo", 101)]);
        Assert.Equal(baseline.PlanFingerprint, undone.PlanFingerprint);

        // Mesmo em perfil que autoaplicaria, o histórico explícito (desfazer) impede a reaplicação automática.
        var undoneAggressive = Plan(s, "aggressive", [Dec(40, 20, "approve", 100), Dec(41, 20, "undo", 101)]);
        Assert.DoesNotContain(undoneAggressive.Transitions, t => t.Kind == "fade");
        Assert.Contains(undoneAggressive.Pending, p => p.SuggestionId == S(20) && p.Kind == "transition_suggestion");

        var rejected = Plan(s, "aggressive", [Dec(40, 20, "reject", 100)]);
        Assert.DoesNotContain(rejected.Transitions, t => t.Kind == "fade");
        Assert.DoesNotContain(rejected.Pending, p => p.SuggestionId == S(20));
    }

    [Fact]
    public void Build_IsIdempotent_AndOldProjectsHaveNoTransitionMetadata()
    {
        var s = new[] { Tr(20, "fade", 1, M1CutAOut, 1, M1CutAIn) };
        Assert.Equal(Plan(s).PlanFingerprint, Plan(s).PlanFingerprint);

        var old = TravelFixture.BuildPlan(TravelFixture.Input());
        Assert.All(old.Transitions, t => { Assert.Null(t.Origin); Assert.Null(t.SuggestionId); });
        Assert.DoesNotContain(old.AppliedEffects ?? [], e => e.Kind == "transition");
        Assert.DoesNotContain(old.Pending, p => p.Kind == "transition_suggestion");
    }

    [Fact]
    public void StaleSuggestion_ThatNoLongerMatchesABoundary_IsIgnored_ButApprovedOneIsReported()
    {
        var stale = Plan([Tr(20, "fade", 1, 11_000_000, 1, M1CutAIn)]);
        Assert.DoesNotContain(stale.Transitions, t => t.SuggestionId == S(20));
        Assert.DoesNotContain(stale.Pending, p => p.SuggestionId == S(20));

        var staleApproved = Plan([Tr(20, "fade", 1, 11_000_000, 1, M1CutAIn, SuggestionStatus.Approved)]);
        Assert.Contains(staleApproved.Pending, p => p.SuggestionId == S(20) && p.Message.Contains("não existe mais"));
    }

    [Fact]
    public void Policy_Matrix_ForTransitionFacts()
    {
        var policy = AutoEditorialPolicy.Instance;
        static AutoEditCandidate C(TransitionFacts f, bool decided = false) =>
            new("transition", 0, 0, true, [], false, decided, Transition: f);
        var fade = new TransitionFacts("fade", 12, TransitionHandleState.Sufficient, false, 2_500_000, 300, false, 0);

        Assert.Equal(PolicyAction.Apply, policy.Evaluate(C(fade), AutoEditorialProfile.Balanced).Action);
        Assert.Equal(PolicyAction.Review, policy.Evaluate(C(fade), AutoEditorialProfile.Conservative).Action);
        Assert.Equal(PolicyAction.Review, policy.Evaluate(C(fade, decided: true), AutoEditorialProfile.Aggressive).Action);
        Assert.Equal(PolicyAction.Skip, policy.Evaluate(C(fade with { SpeechAtEdge = true }), AutoEditorialProfile.Aggressive).Action);
        Assert.Equal(PolicyAction.Skip, policy.Evaluate(C(fade with { SourceGapUs = 500_000 }), AutoEditorialProfile.Balanced).Action);
        Assert.Equal(PolicyAction.Skip, policy.Evaluate(C(fade with { ShortestClipFrames = 40 }), AutoEditorialProfile.Balanced).Action);
        Assert.Equal(PolicyAction.Skip, policy.Evaluate(C(fade with { Handles = TransitionHandleState.Unknown }), AutoEditorialProfile.Balanced).Action);
        Assert.Equal(PolicyAction.Review, policy.Evaluate(C(fade with { CrossFile = true }), AutoEditorialProfile.Aggressive).Action);
        Assert.Equal(PolicyAction.Review, policy.Evaluate(C(fade with { Kind = "dissolve" }), AutoEditorialProfile.Aggressive).Action);
        Assert.Equal(PolicyAction.Skip, policy.Evaluate(C(fade with { Kind = "dissolve", Handles = TransitionHandleState.Insufficient }), AutoEditorialProfile.Aggressive).Action);
        Assert.Equal(PolicyAction.Review, policy.Evaluate(C(fade with { DurationFrames = 20 }), AutoEditorialProfile.Balanced).Action);
        Assert.Equal(PolicyAction.Skip, policy.Evaluate(C(fade with { TransitionsInWindow = 1 }), AutoEditorialProfile.Balanced).Action);
    }

    [Fact]
    public void EvaluateHandles_MatchesEditorialEffectsRules()
    {
        // Fade não consome handle; dissolve exige metade da duração (6 quadros a 30 fps = 0,2 s) em cada lado.
        Assert.Equal(TransitionHandleState.Sufficient, TransitionSupport.EvaluateHandles("fade", 12, 30, 30, 10_000_000, 10_000_000, 30, 0));
        Assert.Equal(TransitionHandleState.Insufficient, TransitionSupport.EvaluateHandles("dissolve", 12, 30, 30, 10_000_000, 10_000_000, 30, 0));
        Assert.Equal(TransitionHandleState.Sufficient, TransitionSupport.EvaluateHandles("dissolve", 12, 30, 30, 10_000_000, 9_000_000, 30, 500_000));
        Assert.Equal(TransitionHandleState.Unknown, TransitionSupport.EvaluateHandles("dissolve", 12, 30, null, 10_000_000, 9_000_000, 30, 500_000));
        Assert.Equal(TransitionHandleState.Unknown, TransitionSupport.EvaluateHandles("dissolve", 12, 30, 30, 0, 0, 30, 500_000));
        Assert.Equal(TransitionHandleState.Sufficient, TransitionSupport.EvaluateHandles("crossfade", 12, 30, null, 10_000_000, 9_000_000, null, 500_000));
    }
}
