namespace TravelCut.Domain.Entities.Editorial;

public enum EditorialTransitionType
{
    Cut = 0,
    VideoFadeIn = 1,
    VideoFadeOut = 2,
    VideoDissolve = 3,
    AudioFadeIn = 4,
    AudioFadeOut = 5,
    AudioCrossfade = 6
}

public enum TransitionPresetDuration
{
    Short = 0,    // 0.2s
    Standard = 1, // 0.4s
    Smooth = 2,   // 0.8s
    Custom = 3
}

public enum TransitionStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2,
    Deferred = 3
}

public enum TransitionOrigin
{
    Import = 0,
    Manual = 1,
    SceneBoundary = 2
}

public class EditorialTransition
{
    public Guid TransitionId { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public Guid MediaIdA { get; set; }
    public Guid MediaIdB { get; set; }
    public EditorialTransitionType Type { get; set; } = EditorialTransitionType.Cut;
    public double DurationSeconds { get; set; } = 0.4;
    public TransitionPresetDuration Preset { get; set; } = TransitionPresetDuration.Standard;
    public TransitionStatus Status { get; set; } = TransitionStatus.Pending;
    public TransitionOrigin Origin { get; set; } = TransitionOrigin.Import;
    public string Rationale { get; set; } = string.Empty;
    public bool IsAdjacentOnTimeline { get; set; } = true;
    public bool InsufficientHandles { get; set; } = false;
    public string? HandleWarningMessage { get; set; }
    public double AvailableHandleFramesA { get; set; } = 0;
    public double AvailableHandleFramesB { get; set; } = 0;
    public Guid? TimelineClipIdA { get; set; }
    public Guid? TimelineClipIdB { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public static double PresetToSeconds(TransitionPresetDuration preset, double customSeconds = 0.4) =>
        preset switch
        {
            TransitionPresetDuration.Short => 0.2,
            TransitionPresetDuration.Standard => 0.4,
            TransitionPresetDuration.Smooth => 0.8,
            TransitionPresetDuration.Custom => Math.Max(0.05, customSeconds),
            _ => 0.4
        };
}
