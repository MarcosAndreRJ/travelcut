using TravelCut.Domain.Planning;
using TravelCut.Domain.ValueObjects;
using Xunit;

namespace TravelCut.Tests.Domain.Planning;

/// <summary>F14.7b: o fingerprint de planos sem overlay de edições manuais é congelado (golden) e não pode mudar.</summary>
public class PlanFingerprintGoldenTests
{
    private static Guid G(int n) => new($"00000000-0000-0000-0000-{n:D12}");

    internal static PlanRevision GoldenPlan(IReadOnlyList<PlanManualEdit>? manualEdits = null)
    {
        var profile = new PlanProfileSnapshot("viagens", 1, "conservative", "normal", "strict", "subtle", "auto", "suggest_only", true, true);
        var clips = new List<PlanClip>
        {
            new(G(1), "V1", G(10), "a.mp4", G(7), 0, 10_000_000, 0, 300, 1, 1, G(2), null, "r", new List<string> { "e1" }, new List<Guid> { G(5) }, "high", false),
            new(G(2), "A1", G(10), "a.mp4", G(7), 0, 10_000_000, 0, 300, 1, 1, G(1), null, "r", new List<string> { "e1" }, new List<Guid> { G(5) }, "high", false),
            new(G(3), "V1", G(10), "a.mp4", G(7), 20_000_000, 25_000_000, 300, 150, 1, 1, null, 1.05, "r", new List<string>(), new List<Guid>(), "high", true, 0.5, 0.5, 1.5, "punch_in"),
        };
        return new PlanRevision(G(99), G(100), 3, 2, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), "user", profile,
            RationalTimeBase.Fps2997, "UTC", "mfp",
            new[] { new PlanTrack("V1", "video", "V1"), new PlanTrack("A1", "dialogue", "A1") },
            new[] { new PlanSection(G(7), 1, "abertura", "t", "r", true, 0, 450) }, clips,
            new[] { new PlanTransition(G(1), G(3), "dissolve", 12, true, "auto", G(8)) },
            new[] { new PlanAppliedCut(G(4), G(10), 10_000_000, 20_000_000, "cut", "auto_trivial") },
            new[] { new PlanPendingItem(G(5), null, "suggestion_pending", "m"), new PlanPendingItem(null, G(10), "zoom_suggestion", "z", G(3)) },
            null, manualEdits);
    }

    [Fact]
    public void PlanWithoutOverlay_HasFrozenFingerprint()
        => Assert.Equal("f958e140208b9ed2202a7a05bb1fa52123c39cecbc461d9cd4bfde6a3aa7d1a8", PlanFingerprint.ComputePlan(GoldenPlan()));
}
