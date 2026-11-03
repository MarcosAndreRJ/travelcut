using TravelCut.Domain.Enums;
using TravelCut.Domain.ValueObjects;

namespace TravelCut.Domain.Entities;

public class Scene
{
    public Guid SceneId { get; set; } = Guid.NewGuid();
    public Guid MediaId { get; set; }
    public TimeRange SourceInterval { get; set; }
    public SceneClassification Classification { get; set; } = SceneClassification.Other;
    public double Confidence { get; set; } = 1.0;
    public string Rationale { get; set; } = string.Empty;
    public string EvidenceRefs { get; set; } = string.Empty;
}
