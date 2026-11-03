using TravelCut.Domain.Enums;
using TravelCut.Domain.ValueObjects;

namespace TravelCut.Domain.Entities;

public class EditSuggestion
{
    public Guid SuggestionId { get; set; } = Guid.NewGuid();
    public Guid MediaId { get; set; }
    public TimeRange SourceInterval { get; set; }
    public SuggestionKind Kind { get; set; }
    public string Rationale { get; set; } = string.Empty;
    public string EvidenceRefs { get; set; } = string.Empty;
    public string ConfidenceBand { get; set; } = "high"; // low, medium, high
    public bool NeedsHumanReview { get; set; } = false;
    public string SuggestedAction { get; set; } = "cut"; // cut, trim, speed2x, speed4x, zoom, bed
    public SuggestionStatus Status { get; set; } = SuggestionStatus.Proposed;
    public string Author { get; set; } = "vad-analyzer";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
