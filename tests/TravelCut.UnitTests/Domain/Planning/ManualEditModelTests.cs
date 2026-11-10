using System.Text.Json;
using TravelCut.Application.Planning;
using TravelCut.Contracts.Dtos;
using TravelCut.Domain.Planning;
using Xunit;

namespace TravelCut.Tests.Domain.Planning;

public class ManualEditModelTests
{
    private static Guid G(int n) => new($"00000000-0000-0000-0000-{n:D12}");
    private static PlanManualEdit Ex(int seq, long a, long b, int rev = 1) => PlanManualEdit.Create(seq, ManualEditKinds.Exclude, G(10), a, b, introducedInRevision: rev);
    private static PlanManualEdit In(int seq, long a, long b, int rev = 1) => PlanManualEdit.Create(seq, ManualEditKinds.Include, G(10), a, b, introducedInRevision: rev);

    [Fact]
    public void NullOrEmptyOverlay_KeepsFrozenFingerprint()
    {
        const string golden = "f958e140208b9ed2202a7a05bb1fa52123c39cecbc461d9cd4bfde6a3aa7d1a8";
        Assert.Equal(golden, PlanFingerprint.ComputePlan(PlanFingerprintGoldenTests.GoldenPlan(null)));
        Assert.Equal(golden, PlanFingerprint.ComputePlan(PlanFingerprintGoldenTests.GoldenPlan(Array.Empty<PlanManualEdit>())));
    }

    [Fact]
    public void NonEmptyOverlay_ChangesFingerprint_AndIgnoresSeqRevisionAndEditSplitting()
    {
        var plain = PlanFingerprint.ComputePlan(PlanFingerprintGoldenTests.GoldenPlan());
        var a = PlanFingerprint.ComputePlan(PlanFingerprintGoldenTests.GoldenPlan(new[] { Ex(1, 100, 300, 1) }));
        // Mesma intenção por outro caminho: duas faixas adjacentes, Seq/revisão diferentes, ordem de entrada invertida.
        var b = PlanFingerprint.ComputePlan(PlanFingerprintGoldenTests.GoldenPlan(new[] { Ex(9, 200, 300, 7), Ex(4, 100, 200, 3) }));
        Assert.NotEqual(plain, a);
        Assert.Equal(a, b);
        Assert.NotEqual(a, PlanFingerprint.ComputePlan(PlanFingerprintGoldenTests.GoldenPlan(new[] { Ex(1, 100, 301) })));
    }

    [Fact]
    public void EditId_IsDeterministic_AndContentOnly()
    {
        Assert.Equal(Ex(1, 5, 9, 1).EditId, Ex(7, 5, 9, 4).EditId);
        Assert.NotEqual(Ex(1, 5, 9).EditId, Ex(1, 5, 10).EditId);
        Assert.NotEqual(Ex(1, 5, 9).EditId, In(1, 5, 9).EditId);
    }

    [Fact]
    public void Compact_LatestWins_AndIsIdempotent()
    {
        var c = ManualEdits.Compact(new[] { Ex(1, 0, 100), In(2, 40, 60), Ex(3, 50, 70) });
        Assert.Equal(new[] { (ManualEditKinds.Exclude, 0L, 40L), (ManualEditKinds.Include, 40L, 50L), (ManualEditKinds.Exclude, 50L, 100L) },
            c.Select(e => (e.Kind, e.SourceInUs, e.SourceOutUs)).ToArray());
        Assert.Equal(new[] { 1, 2, 3 }, c.Select(e => e.Seq).ToArray());
        Assert.Equal(c, ManualEdits.Compact(c));
        Assert.Empty(ManualEdits.Compact(null));
    }

    [Fact]
    public void Compact_RestoringAnExclusion_LeavesOnlyTheInclude()
    {
        var c = ManualEdits.Compact(new[] { Ex(1, 0, 100), In(2, 0, 100) });
        Assert.Single(c);
        Assert.Equal(ManualEditKinds.Include, c[0].Kind);
    }

    [Fact]
    public void DtoJson_ToleratesAbsentManualEdits_AndRoundTrips()
    {
        var plan = PlanFingerprintGoldenTests.GoldenPlan(new[] { Ex(1, 100, 300), PlanManualEdit.Create(2, ManualEditKinds.Place, G(10), 0, 50, G(10), 20_000_000) });
        var dto = PlanMapping.ToDto(plan, new PlanSummaryDto(0, 450, 0, 0, 0, 0, 0), new PlanValidationDto(true, new(), plan.CreatedAtUtc), "fp");
        Assert.Equal(2, dto.ManualEdits!.Count);
        var back = JsonSerializer.Deserialize<EditPlanDto>(JsonSerializer.Serialize(dto))!;
        Assert.Equal(dto.ManualEdits, back.ManualEdits);
        Assert.Equal(PlanFingerprint.ComputePlan(plan), PlanFingerprint.ComputePlan(PlanMapping.ToDomain(back)));

        // Plano antigo: JSON sem o campo "manualEdits".
        var node = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(dto))!.AsObject();
        node.Remove("manualEdits");
        var old = JsonSerializer.Deserialize<EditPlanDto>(node.ToJsonString())!;
        Assert.Null(old.ManualEdits);
        Assert.Equal("editplan/1", EditPlanner.SchemaVersion);
        Assert.Null(PlanMapping.ToDomain(old).ManualEdits);
    }
}
