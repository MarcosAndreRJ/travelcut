using TravelCut.Domain.Enums;

namespace TravelCut.Domain.Entities;

public class MediaAsset
{
    public Guid MediaId { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public string RelativePath { get; set; } = string.Empty;
    public long ByteLength { get; set; }
    public DateTime LastWriteUtc { get; set; }
    public string Fingerprint { get; set; } = string.Empty;
    public MediaKind Kind { get; set; } = MediaKind.Video;
    public bool IsArchived { get; set; } = false;
    public DateTime DiscoveredAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Tags livres do usuário (normalizadas na importação). Persistidas como JSON em TagsJson.</summary>
    public List<string> Tags { get; set; } = new();

    public MediaMetadata? Metadata { get; set; }
}
