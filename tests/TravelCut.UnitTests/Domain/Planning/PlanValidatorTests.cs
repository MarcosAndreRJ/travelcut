using TravelCut.Domain.Planning;
using TravelCut.Domain.ValueObjects;
using Xunit;

namespace TravelCut.Tests.Domain.Planning;

public class PlanValidatorTests
{
    private static readonly Guid M1 = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid M2 = new("22222222-2222-2222-2222-222222222222");
    private static readonly RationalTimeBase Tb = new(1, 30); // 30 fps simplifica contas
    private const long Sec = 1_000_000;

    private static Guid G(int n) => new($"00000000-0000-0000-0000-{n:D12}");

    // Clip de vídeo: [inS,outS) segundos a 1x, 30 fps => dur = (out-in)*30 frames.
    private static PlanClip Clip(int id, string track, long inS, long outS, long start, long? dur = null,
        int sn = 1, int sd = 1, Guid? media = null, Guid? link = null)
        => new(G(id), track, media ?? M1, "1-Bruto/a.mp4", null, inS * Sec, outS * Sec, start,
            dur ?? (long)((outS - inS) * 30 * sd / sn), sn, sd, link, null, "r", new List<string>(), new List<Guid>(), "high", false);

    private static readonly PlanProfileSnapshot Profile = new("viagens", 1, "conservative", "normal", "strict",
        "subtle", "auto", "suggest_only", true, true);

    private static PlanRevision Plan(IEnumerable<PlanClip> clips, IEnumerable<PlanTransition>? tr = null,
        IEnumerable<PlanSection>? secs = null, string fp = "fp")
        => new(G(900), G(901), 1, null, DateTime.UtcNow, "test", Profile, Tb, "UTC", fp,
            new[] { new PlanTrack("V1", "video", "V1"), new PlanTrack("A1", "dialogue", "A1") },
            (secs ?? Array.Empty<PlanSection>()).ToList(), clips.ToList(),
            (tr ?? Array.Empty<PlanTransition>()).ToList(), new List<PlanAppliedCut>(), new List<PlanPendingItem>());

    private static readonly Dictionary<Guid, MediaFacts> Media = new()
    {
        [M1] = new(M1, 100 * Sec, "f1", "1-Bruto/a.mp4"),
        [M2] = new(M2, 100 * Sec, "f2", "1-Bruto/b.mp4"),
    };

    private static readonly IReadOnlyDictionary<Guid, IReadOnlyList<(long, long)>> NoSpeech =
        new Dictionary<Guid, IReadOnlyList<(long, long)>>();

    private static IReadOnlyList<PlanIssue> Run(PlanRevision p, Dictionary<Guid, IReadOnlyList<(long, long)>>? speech = null, string fp = "fp")
        => PlanValidator.Validate(p, Media, speech ?? NoSpeech, fp);

    private static void Has(IReadOnlyList<PlanIssue> i, string code, PlanIssueSeverity sev = PlanIssueSeverity.Error)
        => Assert.Contains(i, x => x.Code == code && x.Severity == sev);

    private static void HasNot(IReadOnlyList<PlanIssue> i, string code) => Assert.DoesNotContain(i, x => x.Code == code);

    [Fact]
    public void ValidPlan_NoIssues()
    {
        var p = Plan(new[] { Clip(1, "V1", 0, 10, 0), Clip(2, "V1", 20, 25, 300) });
        Assert.Empty(Run(p));
    }

    [Fact]
    public void LinkedAudio_Correct_NoIssues_AndBrokenCases()
    {
        var v = Clip(1, "V1", 0, 10, 0, link: G(2));
        var a = Clip(2, "A1", 0, 10, 0, link: G(1));
        Assert.Empty(Run(Plan(new[] { v, a })));

        var aBad = a with { SourceOutUs = 9 * Sec, DurationFrames = 270 };
        Has(Run(Plan(new[] { v, aBad })), "LINK_MISMATCH");
        var aSpeed = a with { SpeedNum = 2 };
        Has(Run(Plan(new[] { v, aSpeed })), "LINK_MISMATCH");
        var aPos = a with { TimelineStartFrame = 5 };
        Has(Run(Plan(new[] { v, aPos })), "LINK_MISMATCH");
        var aNoBack = a with { LinkedClipId = null };
        Has(Run(Plan(new[] { v, aNoBack })), "LINK_MISMATCH");
        Has(Run(Plan(new[] { v })), "LINK_MISMATCH"); // alvo inexistente
    }

    [Fact]
    public void EmptyInterval()
    {
        Has(Run(Plan(new[] { Clip(1, "V1", 5, 5, 0, dur: 10) })), "EMPTY_INTERVAL");
        Has(Run(Plan(new[] { Clip(1, "V1", 0, 5, 0, dur: 0) })), "EMPTY_INTERVAL");
        HasNot(Run(Plan(new[] { Clip(1, "V1", 0, 5, 0) })), "EMPTY_INTERVAL");
    }

    [Fact]
    public void ClipOutsideMedia()
    {
        Has(Run(Plan(new[] { Clip(1, "V1", 0, 101, 0) })), "CLIP_OUTSIDE_MEDIA");
        Has(Run(Plan(new[] { Clip(1, "V1", 0, 5, 0) with { SourceInUs = -1 } })), "CLIP_OUTSIDE_MEDIA");
        HasNot(Run(Plan(new[] { Clip(1, "V1", 0, 100, 0) })), "CLIP_OUTSIDE_MEDIA");
    }

    [Fact]
    public void MediaMissing()
    {
        var p = Plan(new[] { Clip(1, "V1", 0, 5, 0, media: Guid.NewGuid()) });
        Has(Run(p), "MEDIA_MISSING");
        HasNot(Run(Plan(new[] { Clip(1, "V1", 0, 5, 0) })), "MEDIA_MISSING");
    }

    [Fact]
    public void TrackOverlap_OnlySameTrack()
    {
        Has(Run(Plan(new[] { Clip(1, "V1", 0, 10, 0), Clip(2, "V1", 10, 20, 299) })), "TRACK_OVERLAP");
        HasNot(Run(Plan(new[] { Clip(1, "V1", 0, 10, 0), Clip(2, "A1", 10, 20, 100) })), "TRACK_OVERLAP");
        HasNot(Run(Plan(new[] { Clip(1, "V1", 0, 10, 0), Clip(2, "V1", 10, 20, 300) })), "TRACK_OVERLAP");
    }

    [Fact]
    public void NegativePosition()
    {
        Has(Run(Plan(new[] { Clip(1, "V1", 0, 10, -1) })), "NEGATIVE_POSITION");
        HasNot(Run(Plan(new[] { Clip(1, "V1", 0, 10, 0) })), "NEGATIVE_POSITION");
    }

    [Fact]
    public void InvalidSpeed()
    {
        Has(Run(Plan(new[] { Clip(1, "V1", 0, 10, 0, dur: 300) with { SpeedNum = 0 } })), "INVALID_SPEED");
        Has(Run(Plan(new[] { Clip(1, "V1", 0, 10, 0, dur: 300) with { SpeedDen = -1 } })), "INVALID_SPEED");
        HasNot(Run(Plan(new[] { Clip(1, "V1", 0, 10, 0) })), "INVALID_SPEED");
    }

    [Fact]
    public void SpeedOverSpeech()
    {
        var speech = new Dictionary<Guid, IReadOnlyList<(long, long)>> { [M1] = new List<(long, long)> { (4 * Sec, 6 * Sec) } };
        var fast = Clip(1, "V1", 0, 10, 0, sn: 2); // 2x => 150 frames
        Has(Run(Plan(new[] { fast }), speech), "SPEED_OVER_SPEECH");
        // 2x fora da fala: ok
        var fastOutside = Clip(1, "V1", 6, 16, 0, sn: 2);
        HasNot(Run(Plan(new[] { fastOutside }), speech), "SPEED_OVER_SPEECH");
        // 1x sobre fala: ok
        HasNot(Run(Plan(new[] { Clip(1, "V1", 0, 10, 0) }), speech), "SPEED_OVER_SPEECH");
        // fala termina exatamente no in: não intersecta
        var edge = new Dictionary<Guid, IReadOnlyList<(long, long)>> { [M1] = new List<(long, long)> { (0, 6 * Sec) } };
        HasNot(Run(Plan(new[] { fastOutside }), edge), "SPEED_OVER_SPEECH");
    }

    [Fact]
    public void SpeedOverUnknownSpeech_IsError_WhenMediaHasNoTranscript()
    {
        // A1: sem entrada em speechByMedia a fala é desconhecida: velocidade ≠ 1 não pode existir.
        var fast = Clip(1, "V1", 0, 10, 0, sn: 2);
        var issues = Run(Plan(new[] { fast }));

        var issue = Assert.Single(issues, i => i.Code == "SPEED_OVER_SPEECH");
        Assert.Equal(PlanIssueSeverity.Error, issue.Severity);
        Assert.Contains("fala desconhecida (sem transcrição)", issue.Message);
        Assert.Equal(G(1), issue.ClipId);

        // Mesmo clipe a 1× não gera erro; e com transcrição conhecida (sem fala no trecho) 2× é aceito.
        HasNot(Run(Plan(new[] { Clip(1, "V1", 0, 10, 0) })), "SPEED_OVER_SPEECH");
        var known = new Dictionary<Guid, IReadOnlyList<(long, long)>> { [M1] = new List<(long, long)>() };
        HasNot(Run(Plan(new[] { fast }), known), "SPEED_OVER_SPEECH");
    }

    [Fact]
    public void ClipOnTrackMissingFromPlan_IsInvalidTrack()
    {
        // M5: clipe com trackId fora de plan.Tracks (V1 e A1 existem na fixture).
        var onA2 = Clip(1, "A2", 0, 10, 0);
        Has(Run(Plan(new[] { onA2 })), "INVALID_TRACK");
        HasNot(Run(Plan(new[] { Clip(1, "V1", 0, 10, 0) })), "INVALID_TRACK");
    }

    [Fact]
    public void Transition_TooLong_AndNotAdjacent()
    {
        var c1 = Clip(1, "V1", 0, 10, 0); var c2 = Clip(2, "V1", 10, 20, 300); var c3 = Clip(3, "V1", 20, 30, 600);
        PlanTransition T(int a, int b, long d) => new(G(a), G(b), "dissolve", d, false);
        HasNot(Run(Plan(new[] { c1, c2 }, new[] { T(1, 2, 150) })), "TRANSITION_OUT_OF_BOUNDS"); // exatamente metade
        Has(Run(Plan(new[] { c1, c2 }, new[] { T(1, 2, 151) })), "TRANSITION_OUT_OF_BOUNDS");
        Has(Run(Plan(new[] { c1, c2, c3 }, new[] { T(1, 3, 10) })), "TRANSITION_OUT_OF_BOUNDS");
        var a1 = Clip(4, "A1", 10, 20, 300);
        Has(Run(Plan(new[] { c1, a1 }, new[] { T(1, 4, 10) })), "TRANSITION_OUT_OF_BOUNDS");
        Has(Run(Plan(new[] { c1 }, new[] { T(1, 99, 10) })), "TRANSITION_OUT_OF_BOUNDS");
    }

    [Fact]
    public void DurationMismatch_WarningOneFrame_ErrorMore()
    {
        Has(Run(Plan(new[] { Clip(1, "V1", 0, 10, 0, dur: 301) })), "DURATION_MISMATCH", PlanIssueSeverity.Warning);
        Has(Run(Plan(new[] { Clip(1, "V1", 0, 10, 0, dur: 302) })), "DURATION_MISMATCH", PlanIssueSeverity.Error);
        HasNot(Run(Plan(new[] { Clip(1, "V1", 0, 10, 0) })), "DURATION_MISMATCH");
    }

    [Fact]
    public void DurationMismatch_CumulativeLayoutIsAccepted_At2997()
    {
        // Sequência gerada pelo próprio LayoutSequential (29,97) não deve gerar DURATION_MISMATCH.
        var tb = RationalTimeBase.Fps2997;
        var segs = Enumerable.Range(0, 40).Select(i => ((long)i * 10 * Sec, (long)i * 10 * Sec + 3_333_333 + i * 7, 1, 1)).ToList();
        var lay = TimelineMath.LayoutSequential(segs, tb);
        var clips = segs.Select((s, i) => new PlanClip(G(i + 1), "V1", M1, "p", null, s.Item1, s.Item2, lay[i].StartFrame,
            lay[i].DurationFrames, 1, 1, null, null, "r", new List<string>(), new List<Guid>(), "high", false)).ToList();
        var media = new Dictionary<Guid, MediaFacts> { [M1] = new(M1, 1000 * Sec, "f", "p") };
        var p = Plan(clips) with { TimeBase = tb };
        var issues = PlanValidator.Validate(p, media, NoSpeech, "fp");
        Assert.Empty(issues);
    }

    [Fact]
    public void MediaChanged_Warning()
    {
        var p = Plan(new[] { Clip(1, "V1", 0, 10, 0) });
        Has(Run(p, fp: "other"), "MEDIA_CHANGED", PlanIssueSeverity.Warning);
        HasNot(Run(p), "MEDIA_CHANGED");
    }

    [Fact]
    public void SectionBounds()
    {
        PlanSection S(int id, long a, long b) => new(G(id), id, "desenvolvimento", "t", "r", true, a, b);
        var clips = new[] { Clip(1, "V1", 0, 10, 0) }; // fim = 300
        HasNot(Run(Plan(clips, secs: new[] { S(1, 0, 150), S(2, 150, 300) })), "SECTION_BOUNDS");
        Has(Run(Plan(clips, secs: new[] { S(1, 0, 301) })), "SECTION_BOUNDS", PlanIssueSeverity.Warning);
        Has(Run(Plan(clips, secs: new[] { S(1, -1, 10) })), "SECTION_BOUNDS", PlanIssueSeverity.Warning);
        Has(Run(Plan(clips, secs: new[] { S(1, 0, 200), S(2, 100, 300) })), "SECTION_BOUNDS", PlanIssueSeverity.Warning);
    }
}
