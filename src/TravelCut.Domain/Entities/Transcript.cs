namespace TravelCut.Domain.Entities;

public class Transcript
{
    public Guid TranscriptId { get; set; } = Guid.NewGuid();
    public Guid MediaId { get; set; }
    public string ModelId { get; set; } = "large-v3";
    public string Language { get; set; } = "pt";
    public double AudioDurationSeconds { get; set; }
    public string FullText { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public List<TranscriptWord> Words { get; set; } = [];
}

public class TranscriptWord
{
    public int Seq { get; set; }
    public double StartSeconds { get; set; }
    public double EndSeconds { get; set; }
    public string Text { get; set; } = string.Empty;
    public double Confidence { get; set; } = 1.0;
}
