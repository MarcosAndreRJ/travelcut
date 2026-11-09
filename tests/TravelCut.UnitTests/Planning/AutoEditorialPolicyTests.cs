using TravelCut.Application.Planning;
using TravelCut.Contracts.Dtos;
using TravelCut.Domain.Entities;
using Xunit;

namespace TravelCut.UnitTests.Planning;

/// <summary>Política editorial automática (F14.8 I1): matriz por perfil, precedência e E06 no planejador.</summary>
public class AutoEditorialPolicyTests
{
    private static readonly AutoEditorialPolicy Policy = AutoEditorialPolicy.Instance;
    private const long S = 1_000_000;

    private static PolicyWord W(string text, double start, double end) => new(text, (long)(start * S), (long)(end * S));

    private static AutoEditCandidate Silence(double start, double end, string kind = "silence", bool decided = false,
        bool flagged = false, bool transcript = true, params PolicyWord[] extra)
    {
        var words = new List<PolicyWord> { W("ontem", start - 3, start - 2), W("chegamos", end + 2, end + 3) };
        words.AddRange(extra);
        return new AutoEditCandidate(kind, (long)(start * S), (long)(end * S), transcript, words, flagged, decided);
    }

    [Fact]
    public void Silence_Balanced_UnambiguousSilence_Applies_WithAuditTrail()
    {
        var v = Policy.Evaluate(Silence(10, 12.5), AutoEditorialProfile.Balanced);

        Assert.Equal(PolicyAction.Apply, v.Action);
        Assert.Equal(AutoEditorialPolicy.Version, v.PolicyVersion);
        Assert.Equal(AutoEditorialProfile.Balanced, v.Profile);
        Assert.False(string.IsNullOrWhiteSpace(v.Reason));
        Assert.NotEmpty(v.Evidence);
    }

    [Theory]
    [InlineData(1.5, AutoEditorialProfile.Conservative, PolicyAction.Review)]
    [InlineData(1.5, AutoEditorialProfile.Balanced, PolicyAction.Review)]
    [InlineData(1.5, AutoEditorialProfile.Aggressive, PolicyAction.Apply)]
    [InlineData(2.2, AutoEditorialProfile.Conservative, PolicyAction.Review)]
    [InlineData(2.2, AutoEditorialProfile.Balanced, PolicyAction.Apply)]
    [InlineData(2.2, AutoEditorialProfile.Aggressive, PolicyAction.Apply)]
    [InlineData(3.0, AutoEditorialProfile.Conservative, PolicyAction.Apply)]
    [InlineData(0.6, AutoEditorialProfile.Aggressive, PolicyAction.Review)]
    public void Silence_Threshold_DependsOnProfile(double duration, AutoEditorialProfile profile, PolicyAction expected)
    {
        Assert.Equal(expected, Policy.Evaluate(Silence(10, 10 + duration), profile).Action);
    }

    [Fact]
    public void Silence_WithSpeechInsideMargin_IsReview()
    {
        var c = Silence(10, 13, extra: W("hum", 11.0, 11.3));
        Assert.Equal(PolicyAction.Review, Policy.Evaluate(c, AutoEditorialProfile.Aggressive).Action);
    }

    [Fact]
    public void Silence_WithoutTranscript_IsReview()
    {
        Assert.Equal(PolicyAction.Review,
            Policy.Evaluate(Silence(10, 14, transcript: false), AutoEditorialProfile.Aggressive).Action);
    }

    [Fact]
    public void Silence_WithUncertainAlignment_IsReview()
    {
        var c = Silence(10, 13, extra: W("zero", 9.0, 9.0));
        Assert.Equal(PolicyAction.Review, Policy.Evaluate(c, AutoEditorialProfile.Balanced).Action);
    }

    [Fact]
    public void ShortExpressivePause_IsSkipped_AndLongOneGoesToReview()
    {
        var words = new[] { W("sério?", 8, 9.9), W("então", 12, 13) };
        var shortPause = new AutoEditCandidate("silence", 10 * S, (long)(11.2 * S), true, words, false, false);
        Assert.Equal(PolicyAction.Skip, Policy.Evaluate(shortPause, AutoEditorialProfile.Balanced).Action);

        var longWords = new[] { W("sério?", 6, 7.9), W("então", 14, 15) };
        var longPause = new AutoEditCandidate("silence", 8 * S, 12 * S, true, longWords, false, false);
        Assert.Equal(PolicyAction.Review, Policy.Evaluate(longPause, AutoEditorialProfile.Balanced).Action);
    }

    [Fact]
    public void ShortLaughterPause_IsSkipped_AndPauseKindNeverApplies()
    {
        var words = new[] { W("kkkkk", 9, 10), W("bora", 12, 13) };
        var c = new AutoEditCandidate("pause", 10 * S, 11 * S, true, words, false, false);
        Assert.Equal(PolicyAction.Skip, Policy.Evaluate(c, AutoEditorialProfile.Aggressive).Action);

        foreach (var p in Enum.GetValues<AutoEditorialProfile>())
        {
            Assert.NotEqual(PolicyAction.Apply, Policy.Evaluate(Silence(10, 15, kind: "pause"), p).Action);
        }
    }

    [Fact]
    public void Repetition_AppliesOnlyInAggressive_WithStrongEvidence()
    {
        var words = new[] { W("vamos", 5.0, 5.4), W("vamos", 5.5, 5.9), W("lá", 6.0, 6.3) };
        var c = new AutoEditCandidate("repetition", (long)(5.0 * S), (long)(5.9 * S), true, words, true, false);

        Assert.Equal(PolicyAction.Review, Policy.Evaluate(c, AutoEditorialProfile.Conservative).Action);
        Assert.Equal(PolicyAction.Review, Policy.Evaluate(c, AutoEditorialProfile.Balanced).Action);
        Assert.Equal(PolicyAction.Apply, Policy.Evaluate(c, AutoEditorialProfile.Aggressive).Action);
    }

    [Fact]
    public void Repetition_OfNegationOrAnswer_IsReviewEvenInAggressive()
    {
        var words = new[] { W("não,", 5.0, 5.3), W("não", 5.4, 5.7), W("foi", 5.8, 6.0), W("isso", 6.1, 6.4) };
        var c = new AutoEditCandidate("repetition", (long)(5.0 * S), (long)(5.7 * S), true, words, true, false);
        var v = Policy.Evaluate(c, AutoEditorialProfile.Aggressive);

        Assert.Equal(PolicyAction.Review, v.Action);
    }

    [Fact]
    public void Repetition_WithoutContinuation_OrWithGap_IsReview()
    {
        var noTail = new[] { W("vamos", 5.0, 5.4), W("vamos", 5.5, 5.9) };
        Assert.Equal(PolicyAction.Review, Policy.Evaluate(
            new AutoEditCandidate("repetition", 5 * S, (long)(5.9 * S), true, noTail, true, false),
            AutoEditorialProfile.Aggressive).Action);

        var gap = new[] { W("vamos", 5.0, 5.4), W("vamos", 6.5, 6.9), W("lá", 7.0, 7.3) };
        Assert.Equal(PolicyAction.Review, Policy.Evaluate(
            new AutoEditCandidate("repetition", 5 * S, (long)(6.9 * S), true, gap, true, false),
            AutoEditorialProfile.Aggressive).Action);
    }

    [Theory]
    [InlineData("hesitation")]
    [InlineData("filler")]
    [InlineData("false_start")]
    [InlineData("prolongation")]
    [InlineData("zoom_suggestion")]
    [InlineData("transition")]
    public void OtherKinds_AreAlwaysReview(string kind)
    {
        foreach (var p in Enum.GetValues<AutoEditorialProfile>())
        {
            var c = new AutoEditCandidate(kind, 10 * S, 14 * S, true, [W("a", 1, 2), W("b", 20, 21)], false, false);
            Assert.Equal(PolicyAction.Review, Policy.Evaluate(c, p).Action);
        }
    }

    [Fact]
    public void ExplicitDecision_AlwaysWins_OverAutomation()
    {
        foreach (var p in Enum.GetValues<AutoEditorialProfile>())
        {
            var v = Policy.Evaluate(Silence(10, 20, decided: true), p);
            Assert.Equal(PolicyAction.Review, v.Action);
            Assert.Contains("decisão manual", v.Reason);
        }
    }

    [Fact]
    public void UpstreamReviewFlag_BlocksConservativeAndBalanced_ButNotRecheckedAggressive()
    {
        var c = Silence(10, 13, flagged: true);
        Assert.Equal(PolicyAction.Review, Policy.Evaluate(c, AutoEditorialProfile.Conservative).Action);
        Assert.Equal(PolicyAction.Review, Policy.Evaluate(c, AutoEditorialProfile.Balanced).Action);
        Assert.Equal(PolicyAction.Apply, Policy.Evaluate(c, AutoEditorialProfile.Aggressive).Action);
    }

    [Theory]
    [InlineData(null, AutoEditorialProfile.Balanced)]
    [InlineData("", AutoEditorialProfile.Balanced)]
    [InlineData("conservative", AutoEditorialProfile.Conservative)]
    [InlineData("AGGRESSIVE", AutoEditorialProfile.Aggressive)]
    [InlineData("balanced", AutoEditorialProfile.Balanced)]
    [InlineData("xyz", AutoEditorialProfile.Balanced)]
    public void ProfileParsing_DefaultsToBalanced(string? name, AutoEditorialProfile expected)
    {
        Assert.Equal(expected, AutoEditorialProfiles.FromName(name));
    }

    [Fact]
    public void DefaultEditorialProfile_IsBalanced()
    {
        Assert.Equal(AutoEditorialProfile.Balanced, AutoEditorialProfiles.FromName(EditorialProfiles.Default().SilencePolicy));
    }

    // ------------------------------------------------------------------ Planejador: precedência (E07) e revisão (E06)

    private static Guid S3 => TravelFixture.S(3);

    private static PlanningInput InputWithDecisions(params EditDecision[] decisions) =>
        TravelFixture.Input(decisions: decisions.ToList());

    private static EditDecision Decision(string action, int version) => new()
    {
        DecisionId = Guid.NewGuid(),
        SuggestionId = S3,
        Action = action,
        Reviewer = "user",
        TimestampUtc = new DateTime(2026, 10, 14, 10, 0, 0, DateTimeKind.Utc).AddMinutes(version),
        Version = version,
    };

    [Fact]
    public void Planner_AutoAppliesUnambiguousSilence_WhenNoDecisionExists()
    {
        var plan = TravelFixture.BuildPlan(InputWithDecisions());

        var cut = Assert.Single(plan.AppliedCuts, c => c.SuggestionId == S3);
        Assert.Equal(EditPlanner.OriginAuto, cut.Origin);
    }

    [Fact]
    public void Planner_ApproveThenUndo_IsNeverReappliedByAutomation()
    {
        var plan = TravelFixture.BuildPlan(InputWithDecisions(Decision("approve", 1)));
        Assert.Equal(EditPlanner.OriginApproved, Assert.Single(plan.AppliedCuts, c => c.SuggestionId == S3).Origin);

        var afterUndo = TravelFixture.BuildPlan(InputWithDecisions(Decision("approve", 1), Decision("undo", 2)));

        Assert.DoesNotContain(afterUndo.AppliedCuts, c => c.SuggestionId == S3);
        Assert.Single(afterUndo.Pending, p => p.SuggestionId == S3);
    }

    [Fact]
    public void Planner_RejectedSuggestion_IsNeverReappliedByAutomation()
    {
        var plan = TravelFixture.BuildPlan(InputWithDecisions(Decision("reject", 1)));
        Assert.DoesNotContain(plan.AppliedCuts, c => c.SuggestionId == S3);
    }

    [Fact]
    public void Planner_ConservativeProfile_RequiresLongerSilence()
    {
        // S3 tem 2,5 s: passa no Conservative (>= 2,5 s); com 2,0 s ficaria em revisão.
        var conservative = EditorialProfiles.Default() with { SilencePolicy = "conservative" };
        var plan = TravelFixture.BuildPlan(TravelFixture.Input(profile: conservative, decisions: new List<EditDecision>()));
        Assert.Contains(plan.AppliedCuts, c => c.SuggestionId == S3 && c.Origin == EditPlanner.OriginAuto);
    }

    [Fact]
    public void Planner_AutoOnlyClip_DoesNotNeedReview_ButKeepsTrail()
    {
        var profile = EditorialProfiles.Default() with { ZoomStrength = "off" };
        var plan = TravelFixture.BuildPlan(TravelFixture.Input(profile: profile, decisions: new List<EditDecision>()));

        var autoClips = plan.Clips.Where(c => c.SuggestionIds.Contains(S3)).ToList();
        Assert.NotEmpty(autoClips);
        foreach (var clip in autoClips)
        {
            // O clipe pode também ter corte residual; E06 vale para clipes só com cortes autoaplicados.
            if (clip.Evidence.Any(e => e.Contains("residual"))) continue;
            Assert.False(clip.NeedsHumanReview);
            Assert.Contains(clip.Evidence, e => e.Contains("origem=auto") && e.Contains(AutoEditorialPolicy.Version));
        }
        Assert.Contains(autoClips, c => !c.NeedsHumanReview);
    }

    [Fact]
    public void Planner_DisabledAutoApply_KeepsEverythingPending()
    {
        var profile = EditorialProfiles.Default() with { AutoApplyTrivialCuts = false };
        var plan = TravelFixture.BuildPlan(TravelFixture.Input(profile: profile, decisions: new List<EditDecision>()));
        Assert.DoesNotContain(plan.AppliedCuts, c => c.Origin == EditPlanner.OriginAuto);
    }
}
