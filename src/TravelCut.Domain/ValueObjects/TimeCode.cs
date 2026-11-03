namespace TravelCut.Domain.ValueObjects;

public readonly record struct TimeCode(int Hours, int Minutes, int Seconds, int Frames)
{
    public static TimeCode FromTicks(long ticks, RationalTimeBase timeBase)
    {
        double fps = timeBase.FramesPerSecond;
        if (fps <= 0) fps = 30.0;

        long totalFrames = ticks;
        int frames = (int)(totalFrames % (long)Math.Round(fps));
        long totalSeconds = (long)(totalFrames / fps);

        int seconds = (int)(totalSeconds % 60);
        int totalMinutes = (int)(totalSeconds / 60);
        int minutes = totalMinutes % 60;
        int hours = totalMinutes / 60;

        return new TimeCode(hours, minutes, seconds, frames);
    }

    public static TimeCode FromSeconds(double seconds, RationalTimeBase timeBase)
    {
        long ticks = timeBase.SecondsToTicks(seconds);
        return FromTicks(ticks, timeBase);
    }

    public override string ToString() => $"{Hours:D2}:{Minutes:D2}:{Seconds:D2}:{Frames:D2}";
}
