using System.Text.Json.Serialization;

namespace TravelCut.Contracts.Dtos;

public record TranscriptDto(
    [property: JsonPropertyName("transcriptId")] Guid TranscriptId,
    [property: JsonPropertyName("mediaId")] Guid MediaId,
    [property: JsonPropertyName("modelId")] string ModelId,
    [property: JsonPropertyName("language")] string Language,
    [property: JsonPropertyName("fullText")] string FullText,
    [property: JsonPropertyName("audioDurationSeconds")] double AudioDurationSeconds,
    [property: JsonPropertyName("words")] List<TranscriptWordDto> Words
);

public record TranscriptWordDto(
    [property: JsonPropertyName("seq")] int Seq,
    [property: JsonPropertyName("start")] double StartSeconds,
    [property: JsonPropertyName("end")] double EndSeconds,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("confidence")] double Confidence
);
