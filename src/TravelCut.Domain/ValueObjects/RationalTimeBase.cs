namespace TravelCut.Domain.ValueObjects;

public readonly record struct RationalTimeBase(long Numerator, long Denominator)
{
    public static readonly RationalTimeBase Fps24 = new(1, 24);
    public static readonly RationalTimeBase Fps25 = new(1, 25);
    public static readonly RationalTimeBase Fps30 = new(1, 30);
    public static readonly RationalTimeBase Fps60 = new(1, 60);
    public static readonly RationalTimeBase Fps23976 = new(1001, 24000);
    public static readonly RationalTimeBase Fps2997 = new(1001, 30000);
    public static readonly RationalTimeBase Fps5994 = new(1001, 60000);

    public double FramesPerSecond => (double)Denominator / Numerator;

    public double ToDouble() => FramesPerSecond;

    public double SecondsPerFrame => (double)Numerator / Denominator;

    public long SecondsToTicks(double seconds)
    {
        if (seconds < 0) return 0;
        return (long)Math.Round(seconds * Denominator / Numerator);
    }

    public double TicksToSeconds(long ticks)
    {
        if (ticks < 0) return 0.0;
        return (double)ticks * Numerator / Denominator;
    }

    public static RationalTimeBase FromFps(double fps)
    {
        if (Math.Abs(fps - 23.976) < 0.01) return Fps23976;
        if (Math.Abs(fps - 29.97) < 0.01) return Fps2997;
        if (Math.Abs(fps - 59.94) < 0.01) return Fps5994;
        if (Math.Abs(fps - 24.0) < 0.01) return Fps24;
        if (Math.Abs(fps - 25.0) < 0.01) return Fps25;
        if (Math.Abs(fps - 30.0) < 0.01) return Fps30;
        if (Math.Abs(fps - 60.0) < 0.01) return Fps60;

        long denom = (long)Math.Round(fps * 1000.0);
        return new RationalTimeBase(1000, denom);
    }

    public override string ToString() => $"{Denominator}/{Numerator} fps ({FramesPerSecond:F3} fps)";
}
