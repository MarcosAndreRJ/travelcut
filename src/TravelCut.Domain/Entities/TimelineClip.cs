using TravelCut.Domain.ValueObjects;

namespace TravelCut.Domain.Entities;

public class TimelineClip
{
    public Guid ClipId { get; set; } = Guid.NewGuid();
    public Guid PlanId { get; set; }
    public Guid MediaId { get; set; }
    public TimeRange SourceInterval { get; set; }
    public long TimelineStartTicks { get; set; }
    public string TrackId { get; set; } = "V1";
    public RationalTimeBase SpeedRatio { get; set; } = new(1, 1);
    public Guid? DecisionId { get; set; }
    public int SequenceOrder { get; set; }
}
