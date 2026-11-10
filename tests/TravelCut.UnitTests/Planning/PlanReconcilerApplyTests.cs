using System.Text.Json;
using TravelCut.Application.Planning;
using TravelCut.Contracts.Dtos;
using TravelCut.Domain.Planning;
using Xunit;
using static TravelCut.Tests.Planning.ManualEditFixture;

namespace TravelCut.Tests.Planning;

public class PlanReconcilerApplyTests
{
    private static PlanManualEdit Ex(int seq, Guid m, long a, long b) => PlanManualEdit.Create(seq, ManualEditKinds.Exclude, m, a, b, introducedInRevision: 2);
    private static PlanManualEdit Inc(int seq, Guid m, long a, long b) => PlanManualEdit.Create(seq, ManualEditKinds.Include, m, a, b, introducedInRevision: 2);
    private static PlanManualEdit Place(int seq, Guid m, long a, long b, Guid? am, long? ai) => PlanManualEdit.Create(seq, ManualEditKinds.Place, m, a, b, am, ai, introducedInRevision: 2);
    private static PlanApplyResult Apply(EditPlanDto plan, params PlanManualEdit[] e) => PlanReconciler.Apply(plan, e, Tb, new PlanApplyContext(Durations));
    private static string CanonOf(List<Spec> specs) => Canon(Build(specs, Durations));

    [Fact]
    public void Apply_EmptyOverlay_ReturnsAutoPlanUnchanged()
    {
        var auto = Default();
        var r = PlanReconciler.Apply(auto, Array.Empty<PlanManualEdit>(), Tb);
        Assert.Same(auto, r.Plan);
        Assert.Equal(JsonSerializer.Serialize(auto), JsonSerializer.Serialize(PlanReconciler.Apply(auto, null, Tb).Plan));
    }

    [Fact]
    public void Exclude_SplitsSegmentInTwo_AndRipples()
    {
        var auto = Default();
        var r = Apply(auto, Ex(1, A, 3 * S, 5 * S));
        Assert.False(r.Refused);
        var exp = DefaultSpecs(); exp[0] = new Spec(A, 0, 3 * S); exp.Insert(1, new Spec(A, 5 * S, 10 * S));
        Assert.Equal(CanonOf(exp), Canon(r.Plan!));
        Assert.Equal(auto.Summary.RemovedSourceUs + 2 * S, r.Plan!.Summary.RemovedSourceUs);
        Assert.Contains(r.Plan.AppliedCuts, c => c.Origin == PlanReconciler.OriginManualEdit && c.SourceInUs == 3 * S && c.SourceOutUs == 5 * S);
        Assert.Equal(PlanFingerprint.ComputePlan(PlanMapping.ToDomain(r.Plan)), r.Plan.PlanFingerprint);
        Assert.NotNull(r.Plan.ManualEdits);
    }

    [Fact]
    public void Include_RestoresGap_GluedToSourceNeighbor()
    {
        var r = Apply(Default(), Inc(1, A, 10 * S, 15 * S));
        var exp = DefaultSpecs(); exp[0] = new Spec(A, 0, 30 * S); exp.RemoveAt(1);
        Assert.Equal(CanonOf(exp), Canon(r.Plan!));
        Assert.Contains(r.Conflicts, c => c.Type == PlanReconciler.ConflictCutReopened && c.Severity == "warning");   // include vence com aviso
        Assert.DoesNotContain(r.Plan!.AppliedCuts, c => c.MediaId == A && c.SourceInUs < 15 * S && c.SourceOutUs > 10 * S);
    }

    [Fact]
    public void Include_OnMediaWithoutSegments_IsUnanchoredConflict_AndPlanUnchanged()
    {
        var other = G(77);
        var durs = new Dictionary<Guid, long>(Durations) { [other] = 30 * S };
        var auto = Default();
        var r = PlanReconciler.Apply(auto, new[] { Inc(1, other, 0, 5 * S) }, Tb, new PlanApplyContext(durs));
        Assert.Contains(r.Conflicts, c => c.Type == PlanReconciler.ConflictIncludeUnanchored);
        Assert.Equal("conflict", r.Outcomes.Single().Status);
        Assert.Equal(Canon(auto), Canon(r.Plan!));
    }

    [Fact]
    public void Place_MovesAfterAnchor_AndInheritsPredecessorSection()
    {
        var r = Apply(Default(), Place(1, B, 0, 20 * S, C, 12 * S));
        var exp = DefaultSpecs(); var b0 = exp[3]; exp.RemoveAt(3); exp.Add(b0);
        Assert.Equal(CanonOf(exp), Canon(r.Plan!));
        var moved = r.Plan!.Clips.First(c => c.MediaId == B && c.SourceInUs == 0 && c.TrackId == "V1");
        Assert.Equal(G(100 + 3), moved.SectionId);
        Assert.Empty(r.Plan.Validation.Issues.Where(i => i.Severity == "error"));
    }

    [Fact]
    public void Place_WithLostAnchor_KeepsAutomaticOrder()
    {
        var auto = Default();
        var r = Apply(auto, Place(1, B, 0, 20 * S, G(88), 0));
        Assert.Contains(r.Conflicts, c => c.Type == PlanReconciler.ConflictOrderAnchorLost);
        Assert.Equal(Canon(auto), Canon(r.Plan!));
    }

    [Fact]
    public void Place_OverRangeRemovedByAutomation_IsCutReopened_WithRestoreAction()
    {
        var auto = Default();
        var r = Apply(auto, Place(1, A, 10 * S, 15 * S, C, 12 * S));
        var c = Assert.Single(r.Conflicts, x => x.Type == PlanReconciler.ConflictCutReopened);
        Assert.Equal("restaurar trecho", c.SuggestedResolution);
        Assert.Equal(Canon(auto), Canon(r.Plan!));
    }

    [Fact]
    public void Place_PartiallyCut_ShrinksAndWarns()
    {
        var r = Apply(Default(), Place(1, A, 0, 20 * S, B, 25 * S));
        Assert.Equal("applied", r.Outcomes.Single().Status);
        Assert.Contains(r.Conflicts, c => c.Type == PlanReconciler.ConflictCutReopened);
        Assert.False(r.Refused);
    }

    [Fact]
    public void Duplicate_GetsUniqueClipIds_SuffixedByEditId()
    {
        var dup = PlanManualEdit.Create(1, ManualEditKinds.Duplicate, A, 0, 10 * S, A, 0, introducedInRevision: 2);
        var r = Apply(Default(), dup);
        var copies = r.Plan!.Clips.Where(c => c.MediaId == A && c.SourceInUs == 0 && c.SourceOutUs == 10 * S && c.TrackId == "V1").ToList();
        Assert.Equal(2, copies.Count);
        Assert.Equal(r.Plan.Clips.Count, r.Plan.Clips.Select(c => c.ClipId).Distinct().Count());
        Assert.Contains(copies, c => c.ClipId == DeterministicIds.Create("clip", "V1", A.ToString("D"), "0", (10 * S).ToString(), dup.EditId.ToString("D")));
    }

    [Fact]
    public void Transition_OnMovedBoundary_IsDropped_ButKeptWhenBoundaryIsIntact()
    {
        var auto = Default(dissolveAt: 2);          // dissolve A[30,45) -> B[0,20)
        Assert.Contains(auto.Transitions, t => t.Kind == "dissolve");
        var dropped = Apply(auto, Ex(1, A, 40 * S, 45 * S));
        Assert.Contains(dropped.Conflicts, c => c.Type == PlanReconciler.ConflictTransitionDropped);
        Assert.DoesNotContain(dropped.Plan!.Transitions, t => t.Kind == "dissolve");
        var kept = Apply(auto, Ex(1, C, 0, 5 * S));
        Assert.DoesNotContain(kept.Conflicts, c => c.Type == PlanReconciler.ConflictTransitionDropped);
        Assert.Contains(kept.Plan!.Transitions, t => t.Kind == "dissolve");
    }

    [Fact]
    public void SubFrameRemainder_IsRemovedAndRecorded()
    {
        var r = Apply(Default(), Ex(1, A, 0, 10 * S - 10));
        Assert.False(r.Refused);
        Assert.Contains(r.Plan!.AppliedCuts, c => c.Origin == EditPlanner.OriginResidualSubFrame && c.MediaId == A);
        Assert.All(r.Plan.Clips, c => Assert.True(c.DurationFrames > 0));
    }

    [Fact]
    public void LatestEditWins_AndApplyIsDeterministic()
    {
        var edits = new[] { Ex(1, A, 3 * S, 5 * S), Inc(2, A, 3 * S, 4 * S) };
        var r1 = Apply(Default(), edits);
        var r2 = Apply(Default(), edits);
        Assert.Equal(JsonSerializer.Serialize(r1.Plan), JsonSerializer.Serialize(r2.Plan));
        Assert.Contains(r1.Plan!.Clips, c => c.MediaId == A && c.SourceInUs == 0 && c.SourceOutUs == 4 * S);
        Assert.Contains(r1.Plan.Clips, c => c.MediaId == A && c.SourceInUs == 5 * S);
    }

    [Fact]
    public void Property_SetsSpeed_AndRelayouts()
    {
        var auto = Default();
        var p = PlanManualEdit.Create(1, ManualEditKinds.Property, B, 25 * S, 40 * S, speedNum: 3, speedDen: 2, introducedInRevision: 2);
        var r = Apply(auto, p);
        var c = r.Plan!.Clips.First(x => x.MediaId == B && x.SourceInUs == 25 * S);
        Assert.Equal((3, 2), (c.SpeedNum, c.SpeedDen));
        Assert.True(r.Plan.Summary.TimelineDurationFrames < auto.Summary.TimelineDurationFrames);
    }
}
