using TravelCut.Domain.ValueObjects;

namespace TravelCut.Domain.Entities;

public class EditPlan
{
    public Guid PlanId { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public int Revision { get; set; } = 1;
    public int BaseRevision { get; set; } = 0;
    public RationalTimeBase TimelineTimeBase { get; set; } = RationalTimeBase.Fps30;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public string Notes { get; set; } = string.Empty;

    public List<TimelineClip> Clips { get; set; } = [];
}
