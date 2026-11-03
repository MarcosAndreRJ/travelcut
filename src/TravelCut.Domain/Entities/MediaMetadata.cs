using TravelCut.Domain.ValueObjects;

namespace TravelCut.Domain.Entities;

/// <summary>
/// Estados honestos da análise de metadados. Nunca usar valores estimados quando o status não é "ok".
/// </summary>
public static class MediaMetadataStatus
{
    public const string Ok = "ok";
    public const string FfprobeUnavailable = "ffprobe_unavailable";
    public const string Failed = "failed";
    public const string NotRequested = "not_requested";
}

public class MediaMetadata
{
    public Guid MediaId { get; set; }
    public string Codec { get; set; } = string.Empty;
    public int Width { get; set; }
    public int Height { get; set; }
    public RationalTimeBase TimeBase { get; set; } = RationalTimeBase.Fps30;
    public double FrameRate { get; set; }
    public double DurationSeconds { get; set; }
    public long DurationTicks { get; set; }
    public int AudioChannels { get; set; }
    public int AudioSampleRate { get; set; }
    public DateTime? CreationTimeUtc { get; set; }
    public string SourceDateEvidence { get; set; } = string.Empty;
    public bool IsVariableFrameRate { get; set; }
    public string? CameraModel { get; set; }
    public string? GpsLocation { get; set; }
    public int RotationDegrees { get; set; }
    public string MetadataStatus { get; set; } = MediaMetadataStatus.NotRequested;
}
