using TravelCut.Domain.Enums;

namespace TravelCut.Domain.Entities;

public class ProcessingJob
{
    public Guid JobId { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public JobType Type { get; set; }
    public JobStatus Status { get; set; } = JobStatus.Queued;
    public int CompletedUnits { get; set; }
    public int TotalUnits { get; set; }
    public double ProgressPercentage => TotalUnits > 0 ? Math.Clamp((double)CompletedUnits / TotalUnits * 100.0, 0.0, 100.0) : 0.0;
    public string CurrentStepDescription { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? FinishedAtUtc { get; set; }
}
