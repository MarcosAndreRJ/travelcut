using TravelCut.Domain.ValueObjects;
using Xunit;

namespace TravelCut.Tests.Domain;

public class RationalTimeBaseTests
{
    [Fact]
    public void StandardFpsPresets_HaveExpectedValues()
    {
        Assert.Equal(24.0, RationalTimeBase.Fps24.ToDouble(), 4);
        Assert.Equal(25.0, RationalTimeBase.Fps25.ToDouble(), 4);
        Assert.Equal(29.9700, RationalTimeBase.Fps2997.ToDouble(), 3);
        Assert.Equal(30.0, RationalTimeBase.Fps30.ToDouble(), 4);
        Assert.Equal(59.9400, RationalTimeBase.Fps5994.ToDouble(), 3);
        Assert.Equal(60.0, RationalTimeBase.Fps60.ToDouble(), 4);
    }

    [Fact]
    public void SecondsToTicks_AndTicksToSeconds_RoundTripAccurately()
    {
        var timeBase = RationalTimeBase.Fps30;
        double inputSeconds = 12.5;

        long ticks = timeBase.SecondsToTicks(inputSeconds);
        Assert.Equal(375, ticks);

        double outputSeconds = timeBase.TicksToSeconds(ticks);
        Assert.Equal(inputSeconds, outputSeconds, 5);
    }

    [Fact]
    public void SecondsToTicks_WithNonDrop2997_CalculatesProperly()
    {
        var timeBase = RationalTimeBase.Fps2997;
        double inputSeconds = 100.0;

        long ticks = timeBase.SecondsToTicks(inputSeconds);
        double converted = timeBase.TicksToSeconds(ticks);

        Assert.True(Math.Abs(inputSeconds - converted) < 0.001);
    }
}
