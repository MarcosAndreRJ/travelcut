using TravelCut.Domain.Planning;
using TravelCut.Domain.ValueObjects;
using Xunit;

namespace TravelCut.Tests.Domain.Planning;

public class TimelineMathTests
{
    private static readonly RationalTimeBase Tb2997 = RationalTimeBase.Fps2997;
    private const long Hour = 3_600_000_000L;

    [Fact]
    public void OneHourAt2997_Speed1x_Is107892()
        => Assert.Equal(107892, TimelineMath.DurationToFrames(Hour, 1, 1, Tb2997));

    [Fact]
    public void OneHourAt2997_Speed2x_IsHalf() => Assert.Equal(53946, TimelineMath.DurationToFrames(Hour, 2, 1, Tb2997));

    [Fact]
    public void OneHourAt2997_Speed4x_IsQuarter() => Assert.Equal(26973, TimelineMath.DurationToFrames(Hour, 4, 1, Tb2997));

    [Fact]
    public void OneHourAt2997_Speed1001Over1000()
    {
        decimal exact = 108_000_000_000m / 1_002_001m;
        Assert.Equal((long)Math.Round(exact, MidpointRounding.ToEven), TimelineMath.DurationToFrames(Hour, 1001, 1000, Tb2997));
    }

    [Fact]
    public void HalfEven_OnExactTies()
    {
        var tb = new RationalTimeBase(1, 1); // 1 fps
        Assert.Equal(2, TimelineMath.DurationToFrames(2_500_000, 1, 1, tb)); // 2.5 -> 2
        Assert.Equal(4, TimelineMath.DurationToFrames(3_500_000, 1, 1, tb)); // 3.5 -> 4
        Assert.Equal(0, TimelineMath.DurationToFrames(500_000, 1, 1, tb));   // 0.5 -> 0
        Assert.Equal(2, TimelineMath.DurationToFrames(1_500_000, 1, 1, tb)); // 1.5 -> 2
        // velocidade: 5 s a 2x = 2.5 frames
        Assert.Equal(2, TimelineMath.DurationToFrames(5_000_000, 2, 1, tb));
    }

    [Fact]
    public void Layout_HalfEven_UsesAbsolutePositionParity()
    {
        var tb = new RationalTimeBase(1, 1);
        // início 1 + 2.5 = 3.5 -> 4 (par); duração = 3
        var l = TimelineMath.LayoutSequential(new[] { (0L, 2_500_000L, 1, 1) }, tb, 1);
        Assert.Equal((1L, 3L), l[0]);
        // início 0 + 2.5 = 2.5 -> 2
        Assert.Equal((0L, 2L), TimelineMath.LayoutSequential(new[] { (0L, 2_500_000L, 1, 1) }, tb)[0]);
    }

    [Fact]
    public void Layout_NoDrift_1000Segments()
    {
        var segs = Enumerable.Repeat((0L, 33_367L, 1, 1), 1000).ToList();
        var l = TimelineMath.LayoutSequential(segs, Tb2997);
        long exact = (long)Math.Round(1000m * 33_367m * 30_000m / (1_000_000m * 1001m), MidpointRounding.ToEven);
        Assert.Equal(exact, l[^1].StartFrame + l[^1].DurationFrames);
    }

    [Fact]
    public void Layout_DiffersFromSumOfIndividualRoundings()
    {
        // 16683 µs = 0.49999.. frames: cada um arredonda para 0 isoladamente, mas a soma exata de 1000 = ~500 frames.
        var segs = Enumerable.Repeat((0L, 16_683L, 1, 1), 1000).ToList();
        long individual = segs.Sum(s => TimelineMath.DurationToFrames(s.Item2, 1, 1, Tb2997));
        var l = TimelineMath.LayoutSequential(segs, Tb2997);
        long layoutTotal = l[^1].StartFrame + l[^1].DurationFrames;
        Assert.Equal(0, individual);
        Assert.Equal(500, layoutTotal);
        Assert.Equal(layoutTotal, l.Sum(x => x.DurationFrames));
    }

    [Fact]
    public void Layout_ZeroFrameSegment_HasZeroDuration()
    {
        var l = TimelineMath.LayoutSequential(new[] { (0L, 1_000L, 1, 1) }, Tb2997);
        Assert.Equal((0L, 0L), l[0]);
    }

    [Fact]
    public void Layout_RejectsInvertedOrInvalidSpeed()
    {
        Assert.Throws<ArgumentException>(() => TimelineMath.LayoutSequential(new[] { (10L, 5L, 1, 1) }, Tb2997));
        Assert.Throws<ArgumentOutOfRangeException>(() => TimelineMath.LayoutSequential(new[] { (0L, 5L, 0, 1) }, Tb2997));
    }

    [Fact]
    public void Layout_Property_RandomSegments_FixedSeed()
    {
        var rng = new Random(12345);
        var tbs = new[] { RationalTimeBase.Fps23976, RationalTimeBase.Fps2997, RationalTimeBase.Fps5994, RationalTimeBase.Fps25, new RationalTimeBase(1, 120) };
        var speeds = new[] { (1, 1), (2, 1), (4, 1), (1001, 1000), (1, 2), (3, 2) };
        for (int iter = 0; iter < 200; iter++)
        {
            var tb = tbs[rng.Next(tbs.Length)];
            long start = rng.Next(0, 1000);
            int n = rng.Next(1, 30);
            var segs = new List<(long, long, int, int)>();
            for (int i = 0; i < n; i++)
            {
                var sp = speeds[rng.Next(speeds.Length)];
                long a = rng.NextInt64(0, 1_000_000_000);
                segs.Add((a, a + rng.NextInt64(0, 20_000_000), sp.Item1, sp.Item2));
            }
            var l = TimelineMath.LayoutSequential(segs, tb, start);
            decimal acc = start;
            for (int i = 0; i < n; i++)
            {
                Assert.True(l[i].DurationFrames >= 0);
                Assert.Equal(i == 0 ? start : l[i - 1].StartFrame + l[i - 1].DurationFrames, l[i].StartFrame);
                var s = segs[i];
                acc += (s.Item2 - s.Item1) * (decimal)tb.Denominator * s.Item4 / (1_000_000m * tb.Numerator * s.Item3);
                long boundary = l[i].StartFrame + l[i].DurationFrames;
                Assert.True(Math.Abs((decimal)boundary - acc) <= 0.5m + 1e-12m, $"drift iter {iter} seg {i}");
            }
        }
    }

    [Fact]
    public void FramesToMicroseconds_RoundTrip_Property()
    {
        var rng = new Random(7);
        foreach (var tb in new[] { Tb2997, RationalTimeBase.Fps5994, RationalTimeBase.Fps24, new RationalTimeBase(1, 120) })
            for (int i = 0; i < 200; i++)
            {
                long f = rng.NextInt64(0, 5_000_000);
                long us = TimelineMath.FramesToMicroseconds(f, tb);
                Assert.Equal(f, TimelineMath.DurationToFrames(us, 1, 1, tb));
            }
        Assert.Equal(33_367, TimelineMath.FramesToMicroseconds(1, Tb2997)); // 33366.67
    }

    [Fact]
    public void FramesToMicroseconds_HalfEvenTie()
    {
        var tb = new RationalTimeBase(1, 2_000_000); // 0.5 µs por frame
        Assert.Equal(0, TimelineMath.FramesToMicroseconds(1, tb));
        Assert.Equal(2, TimelineMath.FramesToMicroseconds(3, tb)); // 1.5 -> 2
    }

    [Fact]
    public void LargeValues_NoOverflow()
    {
        long tenHours = 36_000_000_000L;
        Assert.Equal(4_320_000, TimelineMath.DurationToFrames(tenHours, 1, 1, new RationalTimeBase(1, 120)));
        long fourHours = 14_400_000_000L;
        long f = TimelineMath.DurationToFrames(fourHours, 1001, 1000, RationalTimeBase.Fps5994);
        decimal exact = 14_400m * 60000m / 1001m * 1000m / 1001m;
        Assert.Equal((long)Math.Round(exact, MidpointRounding.ToEven), f);
        var segs = Enumerable.Repeat((0L, tenHours / 100, 7, 3), 100).ToList();
        var l = TimelineMath.LayoutSequential(segs, new RationalTimeBase(1, 120), long.MaxValue / 4);
        Assert.Equal(100, l.Count);
    }

    [Theory]
    [InlineData(0, "00:00:00:00")]
    [InlineData(29, "00:00:00:29")]
    [InlineData(30, "00:00:01:00")]
    [InlineData(107892, "00:59:56:12")] // NDF: 107892 / 30 = 3596 s + 12 frames
    [InlineData(-31, "-00:00:01:01")]
    public void FormatTimecode_NonDropFrame_2997(long frames, string expected)
        => Assert.Equal(expected, TimelineMath.FormatTimecode(frames, Tb2997));

    [Fact]
    public void FormatTimecode_OtherRates()
    {
        Assert.Equal("00:00:01:00", TimelineMath.FormatTimecode(24, RationalTimeBase.Fps23976));
        Assert.Equal("00:00:01:00", TimelineMath.FormatTimecode(60, RationalTimeBase.Fps5994));
        Assert.Equal("01:00:00:00", TimelineMath.FormatTimecode(90_000, RationalTimeBase.Fps25));
    }

    [Theory]
    [InlineData(23.976, 1001, 24000)]
    [InlineData(29.97, 1001, 30000)]
    [InlineData(59.94, 1001, 60000)]
    [InlineData(24.0, 1, 24)]
    [InlineData(25.0, 1, 25)]
    [InlineData(30.0, 1, 30)]
    [InlineData(50.0, 1, 50)]
    [InlineData(60.0, 1, 60)]
    public void ChooseTimeBase_SingleRate(double fps, long num, long den)
        => Assert.Equal(new RationalTimeBase(num, den), TimelineMath.ChooseTimelineTimeBase(new double?[] { fps }));

    [Fact]
    public void ChooseTimeBase_VfrAverage2997_CountsWithCfr2997()
    {
        // Mídia VFR entra pela média gravada (29,97) e soma à moda das CFR 29,97: escolhe 29,97.
        Assert.Equal(RationalTimeBase.Fps2997, TimelineMath.ChooseTimelineTimeBase(new double?[] { 29.97, 29.97, 29.97, 30 }));
    }

    [Fact]
    public void ChooseTimeBase_Mode_Tie_None_Vfr()
    {
        Assert.Equal(RationalTimeBase.Fps25, TimelineMath.ChooseTimelineTimeBase(new double?[] { 25, 29.97, 25, null }));
        Assert.Equal(RationalTimeBase.Fps2997, TimelineMath.ChooseTimelineTimeBase(new double?[] { 25, 24 })); // empate
        Assert.Equal(RationalTimeBase.Fps2997, TimelineMath.ChooseTimelineTimeBase(Array.Empty<double?>()));
        Assert.Equal(RationalTimeBase.Fps2997, TimelineMath.ChooseTimelineTimeBase(new double?[] { null, double.NaN, 0 }));
        // VFR médio 29,83 está fora da tolerância: ignorado
        Assert.Equal(RationalTimeBase.Fps25, TimelineMath.ChooseTimelineTimeBase(new double?[] { 29.83, 29.83, 25 }));
        // 29,95 dentro da tolerância de 29,97
        Assert.Equal(RationalTimeBase.Fps2997, TimelineMath.ChooseTimelineTimeBase(new double?[] { 29.95, 29.95, 25 }));
        Assert.Equal(RationalTimeBase.Fps2997, TimelineMath.ChooseTimelineTimeBase(new double?[] { 29.83 }));
    }
}
