using TravelCut.Application.Planning;
using TravelCut.Domain.Planning;
using Xunit;
using static TravelCut.Tests.Planning.ManualEditFixture;

namespace TravelCut.Tests.Planning;

public class ManualEditDeriverTests
{
    private static PlanRevision Dom(IReadOnlyList<Spec> specs, IReadOnlyList<PlanManualEdit>? e = null)
        => PlanMapping.ToDomain(Build(specs, Durations, e));
    private static IReadOnlyList<PlanClip> Clips(IReadOnlyList<Spec> specs) => Dom(specs).Clips;

    [Fact]
    public void Derive_SamePlan_IsEmpty()
    {
        var r = Dom(DefaultSpecs());
        Assert.Empty(ManualEditDeriver.Derive(r, r.Clips));
        Assert.Empty(ManualEditDeriver.DeriveDelta(r, r.Clips));
    }

    [Fact]
    public void Derive_SamePlan_KeepsBaseOverlay()
    {
        var ex = PlanManualEdit.Create(1, ManualEditKinds.Exclude, A, 1 * S, 2 * S, introducedInRevision: 1);
        var r = Dom(DefaultSpecs(), new[] { ex });
        Assert.Empty(ManualEditDeriver.DeriveDelta(r, r.Clips));
        Assert.Equal(ManualEdits.Compact(new[] { ex }), ManualEditDeriver.Derive(r, r.Clips));
    }

    [Fact]
    public void Derive_IncludeAndExclude_ByMicrosecondDifference()
    {
        var edited = DefaultSpecs();
        edited[0] = edited[0] with { Out = 8 * S };           // exclui A [8s,10s)
        edited.Insert(1, new Spec(A, 10 * S, 15 * S));       // inclui A [10s,15s)
        var d = ManualEditDeriver.DeriveDelta(Dom(DefaultSpecs()), Clips(edited));
        Assert.Equal(new[] { (ManualEditKinds.Exclude, 8 * S, 10 * S), (ManualEditKinds.Include, 10 * S, 15 * S) },
            d.Select(e => (e.Kind, e.SourceInUs, e.SourceOutUs)).ToArray());
        Assert.All(d, e => Assert.Equal(2, e.IntroducedInRevision));
    }

    [Fact]
    public void Derive_Reorder_EmitsMinimalPlace_ViaLongestIncreasingSubsequence()
    {
        var s = DefaultSpecs();
        var moved = s[3];                                    // B [0,20) vai para o fim
        s.RemoveAt(3); s.Add(moved);
        var d = ManualEditDeriver.DeriveDelta(Dom(DefaultSpecs()), Clips(s));
        var e = Assert.Single(d);
        Assert.Equal(ManualEditKinds.Place, e.Kind);
        Assert.Equal((B, 0L, 20 * S), (e.MediaId, e.SourceInUs, e.SourceOutUs));
        Assert.Equal((C, (long?)(12 * S)), (e.AfterMediaId, e.AfterSourceInUs));

        // Mover o último para o início: também 1 place, âncora nula.
        var s2 = DefaultSpecs();
        var last = s2[^1]; s2.RemoveAt(s2.Count - 1); s2.Insert(0, last);
        var e2 = Assert.Single(ManualEditDeriver.DeriveDelta(Dom(DefaultSpecs()), Clips(s2)));
        Assert.Equal(ManualEditKinds.Place, e2.Kind);
        Assert.Equal(C, e2.MediaId);
        Assert.Null(e2.AfterMediaId);
    }

    [Fact]
    public void Derive_SpeedChange_EmitsProperty_AndRepeatedAtomEmitsDuplicate()
    {
        var s = DefaultSpecs();
        s[3] = s[3] with { Sn = 3, Sd = 2 };
        var p = Assert.Single(ManualEditDeriver.DeriveDelta(Dom(DefaultSpecs()), Clips(s)));
        Assert.Equal((ManualEditKinds.Property, (int?)3, (int?)2), (p.Kind, p.SpeedNum, p.SpeedDen));

        var s2 = DefaultSpecs();
        s2.Insert(1, s2[0]);                                  // A [0,10) repetido logo depois
        var d = Assert.Single(ManualEditDeriver.DeriveDelta(Dom(DefaultSpecs()), Clips(s2)));
        Assert.Equal(ManualEditKinds.Duplicate, d.Kind);
    }

    [Fact]
    public void Derive_AccumulatesOnBaseOverlay_LatestWins()
    {
        var ex = PlanManualEdit.Create(1, ManualEditKinds.Exclude, A, 8 * S, 10 * S, introducedInRevision: 1);
        var baseSpecs = DefaultSpecs(); baseSpecs[0] = baseSpecs[0] with { Out = 8 * S };
        var r = Dom(baseSpecs, new[] { ex });
        var all = ManualEditDeriver.Derive(r, Clips(DefaultSpecs()));   // usuário restaura o trecho
        var only = Assert.Single(all);
        Assert.Equal(ManualEditKinds.Include, only.Kind);
        Assert.Equal((8 * S, 10 * S), (only.SourceInUs, only.SourceOutUs));
    }
}
