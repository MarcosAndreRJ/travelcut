using TravelCut.Domain.ValueObjects;

namespace TravelCut.Domain.Entities.Editorial;

public enum SceneEnvironment
{
    Unclassified = 0,
    Interior = 1,
    Exterior = 2,
    Mixed = 3
}

public enum SceneInteraction
{
    Other = 0,
    DirectToCamera = 1,
    Dialogue = 2,
    Interview = 3,
    Narration = 4,
    Broll = 5,
    Landscape = 6
}

public enum SceneFraming
{
    Unspecified = 0,
    Wide = 1,
    Medium = 2,
    CloseUp = 3
}

public enum SceneMovement
{
    Unspecified = 0,
    Static = 1,
    Moving = 2
}

public enum SceneEditorialProfile
{
    Natural = 0,
    Dynamic = 1,
    Cinematic = 2
}

public class SceneClassificationEntry
{
    public Guid EntryId { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public Guid MediaId { get; set; }
    public double StartSeconds { get; set; } = 0.0;
    public double EndSeconds { get; set; } = 0.0;
    public bool IsFullVideo { get; set; } = true;
    public SceneEnvironment Environment { get; set; } = SceneEnvironment.Unclassified;
    public SceneInteraction Interaction { get; set; } = SceneInteraction.Other;
    public SceneFraming Framing { get; set; } = SceneFraming.Unspecified;
    public SceneMovement Movement { get; set; } = SceneMovement.Unspecified;
    public SceneEditorialProfile EditorialProfile { get; set; } = SceneEditorialProfile.Natural;
    public string Notes { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public bool Covers(double timestampSeconds)
    {
        if (IsFullVideo) return true;
        return timestampSeconds >= StartSeconds && timestampSeconds <= EndSeconds;
    }

    public bool Overlaps(double start, double end)
    {
        if (IsFullVideo) return true;
        return Math.Max(StartSeconds, start) < Math.Min(EndSeconds, end);
    }
}
