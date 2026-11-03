using TravelCut.Domain.Planning;
using TravelCut.Domain.ValueObjects;
using Xunit;

namespace TravelCut.Tests.Domain.Planning;

public class PlanFingerprintTests
{
    private static Guid G(int n) => new($"00000000-0000-0000-0000-{n:D12}");

    private static PlanProfileSnapshot DefaultProfile() => new("viagens", 1, "conservative", "normal", "strict",
        "subtle", "auto", "suggest_only", true, true);

    private static PlanRevision Make(int rev = 1, DateTime? at = null, string author = "planner-rules/1", long outUs = 10_000_000,
        bool reverse = false, PlanProfileSnapshot? profile = null, string timeZone = "UTC", Guid? pendingClip = null)
    {
        var clips = new List<PlanClip>
        {
            new(G(1), "V1", G(10), "a.mp4", null, 0, outUs, 0, 300, 1, 1, null, null, "r", new List<string> { "e1" }, new List<Guid> { G(5), G(4) }, "high", false),
            new(G(2), "V1", G(10), "a.mp4", null, 20_000_000, 25_000_000, 300, 150, 1, 1, null, null, "r", new List<string>(), new List<Guid>(), "high", true),
        };
        if (reverse) clips.Reverse();
        return new PlanRevision(Guid.NewGuid(), G(100), rev, rev > 1 ? rev - 1 : null, at ?? DateTime.UtcNow, author,
            profile ?? DefaultProfile(), RationalTimeBase.Fps2997, timeZone, "mfp", new[] { new PlanTrack("V1", "video", "V1") },
            new[] { new PlanSection(G(7), 0, "abertura", "t", "r", true, 0, 450) }, clips,
            new[] { new PlanTransition(G(1), G(2), "cut", 0, false) }, new[] { new PlanAppliedCut(G(4), G(10), 10_000_000, 20_000_000, "silence", "auto_trivial") },
            new[] { new PlanPendingItem(G(5), null, "suggestion_pending", "m"), new PlanPendingItem(null, G(10), "zoom_suggestion", "z", pendingClip) });
    }

    [Fact]
    public void SameContent_DifferentMetadata_SameHash()
    {
        var a = PlanFingerprint.ComputePlan(Make(1, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), "user"));
        var b = PlanFingerprint.ComputePlan(Make(5, new DateTime(2030, 6, 1, 0, 0, 0, DateTimeKind.Utc), "revert"));
        Assert.Equal(a, b);
        Assert.Matches("^[0-9a-f]{64}$", a);
    }

    [Fact]
    public void OneMicrosecondChange_ChangesHash()
        => Assert.NotEqual(PlanFingerprint.ComputePlan(Make()), PlanFingerprint.ComputePlan(Make(outUs: 10_000_001)));

    [Fact]
    public void ClipInputOrder_DoesNotMatter()
        => Assert.Equal(PlanFingerprint.ComputePlan(Make()), PlanFingerprint.ComputePlan(Make(reverse: true)));

    [Fact]
    public void AnyProfileField_ChangesHash_NotOnlyIdAndVersion()
    {
        // M4: o bloco de perfil inteiro entra no fingerprint (ex.: mudar só zoomStrength ou musicPolicy).
        var baseHash = PlanFingerprint.ComputePlan(Make());
        var d = DefaultProfile();
        Assert.NotEqual(baseHash, PlanFingerprint.ComputePlan(Make(profile: d with { ZoomStrength = "medium" })));
        Assert.NotEqual(baseHash, PlanFingerprint.ComputePlan(Make(profile: d with { MusicPolicy = "off" })));
        Assert.NotEqual(baseHash, PlanFingerprint.ComputePlan(Make(profile: d with { SpeedBroll = "off" })));
        Assert.NotEqual(baseHash, PlanFingerprint.ComputePlan(Make(profile: d with { SilencePolicy = "aggressive" })));
        Assert.NotEqual(baseHash, PlanFingerprint.ComputePlan(Make(profile: d with { AutoApplyTrivialCuts = false })));
        Assert.Equal(baseHash, PlanFingerprint.ComputePlan(Make(profile: DefaultProfile())));
    }

    [Fact]
    public void TimeZoneId_ChangesHash()
        => Assert.NotEqual(PlanFingerprint.ComputePlan(Make(timeZone: "UTC")),
            PlanFingerprint.ComputePlan(Make(timeZone: "America/Sao_Paulo")));

    [Fact]
    public void PendingClipId_ChangesHash()
        => Assert.NotEqual(PlanFingerprint.ComputePlan(Make(pendingClip: G(1))),
            PlanFingerprint.ComputePlan(Make(pendingClip: G(2))));

    [Fact]
    public void MediaSet_OrderIndependent_AndSensitive()
    {
        var m1 = new MediaFacts(G(1), 100, "a", "x/a.mp4");
        var m2 = new MediaFacts(G(2), 200, "b", "x/b.mp4");
        var h = PlanFingerprint.ComputeMediaSet(new[] { m1, m2 });
        Assert.Equal(h, PlanFingerprint.ComputeMediaSet(new[] { m2, m1 }));
        Assert.Matches("^[0-9a-f]{64}$", h);
        Assert.NotEqual(h, PlanFingerprint.ComputeMediaSet(new[] { m1, m2 with { Fingerprint = "c" } }));
        Assert.NotEqual(h, PlanFingerprint.ComputeMediaSet(new[] { m1 }));
    }
}
