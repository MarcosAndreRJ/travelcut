namespace TravelCut.Domain.ValueObjects;

public readonly record struct TimeRange(long StartTicks, long EndTicks)
{
    public long DurationTicks => EndTicks >= StartTicks ? EndTicks - StartTicks : 0;

    public bool IsEmpty => DurationTicks <= 0;

    public bool Contains(long tick) => tick >= StartTicks && tick < EndTicks;

    public bool Overlaps(TimeRange other)
    {
        return StartTicks < other.EndTicks && other.StartTicks < EndTicks;
    }

    public bool OverlapsWith(TimeRange other) => Overlaps(other);

    public static TimeRange FromSeconds(double startSeconds, double endSeconds, RationalTimeBase timeBase)
    {
        long start = timeBase.SecondsToTicks(startSeconds);
        long end = timeBase.SecondsToTicks(endSeconds);
        return new TimeRange(start, Math.Max(start, end));
    }

    public override string ToString() => $"[{StartTicks}..{EndTicks}) ({DurationTicks} ticks)";
}
