using TravelCut.Domain.ValueObjects;
using Xunit;

namespace TravelCut.Tests.Domain;

public class TimeRangeTests
{
    [Fact]
    public void DurationTicks_CalculatesDifferenceCorrectly()
    {
        var range = new TimeRange(100, 250);
        Assert.Equal(150, range.DurationTicks);
    }

    [Fact]
    public void OverlapsWith_DetectsIntersection()
    {
        var a = new TimeRange(100, 200);
        var b = new TimeRange(150, 250);
        var c = new TimeRange(201, 300);

        Assert.True(a.OverlapsWith(b));
        Assert.True(b.OverlapsWith(a));
        Assert.False(a.OverlapsWith(c));
    }

    [Fact]
    public void Contains_ChecksPointInclusion()
    {
        var range = new TimeRange(50, 100);

        Assert.True(range.Contains(50));
        Assert.True(range.Contains(75));
        Assert.False(range.Contains(100)); // Intervalo semiaberto [start, end)
        Assert.False(range.Contains(49));
    }
}
