using System.Text.Json.Serialization;

namespace TravelCut.Contracts.Dtos;

public record MediaAssetDto(
    [property: JsonPropertyName("mediaId")] Guid MediaId,
    [property: JsonPropertyName("projectId")] Guid ProjectId,
    [property: JsonPropertyName("relativePath")] string RelativePath,
    [property: JsonPropertyName("byteLength")] long ByteLength,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("codec")] string Codec,
    [property: JsonPropertyName("width")] int Width,
    [property: JsonPropertyName("height")] int Height,
    [property: JsonPropertyName("fps")] double Fps,
    [property: JsonPropertyName("durationSeconds")] double DurationSeconds,
    [property: JsonPropertyName("creationTimeUtc")] DateTime? CreationTimeUtc,
    [property: JsonPropertyName("hasTranscript")] bool HasTranscript,
    [property: JsonPropertyName("hasVAD")] bool HasVAD,
    [property: JsonPropertyName("sceneClassification")] string? SceneClassification,
    [property: JsonPropertyName("thumbnailUrl")] string? ThumbnailUrl,
    [property: JsonPropertyName("tags")] string[] Tags,
    [property: JsonPropertyName("metadataStatus")] string MetadataStatus // ok | ffprobe_unavailable | failed | not_requested
);

public record ScanMediaResultDto(
    [property: JsonPropertyName("totalFilesFound")] int TotalFilesFound,
    [property: JsonPropertyName("newAssetsAdded")] int NewAssetsAdded,
    [property: JsonPropertyName("updatedAssets")] int UpdatedAssets,
    [property: JsonPropertyName("unchangedAssets")] int UnchangedAssets,
    [property: JsonPropertyName("totalBytes")] long TotalBytes,
    [property: JsonPropertyName("durationSecondsTotal")] double DurationSecondsTotal
);

public record MediaDetailsDto(
    [property: JsonPropertyName("mediaId")] Guid MediaId,
    [property: JsonPropertyName("projectId")] Guid ProjectId,
    [property: JsonPropertyName("relativePath")] string RelativePath,
    [property: JsonPropertyName("fullPath")] string FullPath,
    [property: JsonPropertyName("byteLength")] long ByteLength,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("fingerprint")] string Fingerprint,
    [property: JsonPropertyName("lastWriteUtc")] DateTime LastWriteUtc,
    [property: JsonPropertyName("codec")] string Codec,
    [property: JsonPropertyName("width")] int Width,
    [property: JsonPropertyName("height")] int Height,
    [property: JsonPropertyName("frameRate")] double FrameRate,
    [property: JsonPropertyName("timeBase")] string TimeBase,
    [property: JsonPropertyName("durationSeconds")] double DurationSeconds,
    [property: JsonPropertyName("durationTicks")] long DurationTicks,
    [property: JsonPropertyName("audioChannels")] int AudioChannels,
    [property: JsonPropertyName("audioSampleRate")] int AudioSampleRate,
    [property: JsonPropertyName("creationTimeUtc")] DateTime? CreationTimeUtc,
    [property: JsonPropertyName("sourceDateEvidence")] string SourceDateEvidence,
    [property: JsonPropertyName("isVariableFrameRate")] bool IsVariableFrameRate,
    // Adicionado em F05 (opcional, ao final para não quebrar construções existentes): evidência de câmera para classificação de cena.
    [property: JsonPropertyName("cameraModel")] string? CameraModel = null
);

public record ChronologyItemDto(
    [property: JsonPropertyName("mediaId")] Guid MediaId,
    [property: JsonPropertyName("relativePath")] string RelativePath,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("cameraModel")] string? CameraModel,
    [property: JsonPropertyName("recordedAtUtc")] DateTime RecordedAtUtc,
    [property: JsonPropertyName("durationSeconds")] double DurationSeconds,
    [property: JsonPropertyName("estimatedEndAtUtc")] DateTime EstimatedEndAtUtc,
    [property: JsonPropertyName("timestampSource")] string TimestampSource,
    [property: JsonPropertyName("confidence")] string Confidence,
    [property: JsonPropertyName("sessionIndex")] int SessionIndex,
    [property: JsonPropertyName("anomaly")] string? Anomaly
);

public record ChronologySessionDto(
    [property: JsonPropertyName("sessionIndex")] int SessionIndex,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("startTimeUtc")] DateTime StartTimeUtc,
    [property: JsonPropertyName("endTimeUtc")] DateTime EndTimeUtc,
    [property: JsonPropertyName("totalClips")] int TotalClips,
    [property: JsonPropertyName("totalDurationSeconds")] double TotalDurationSeconds,
    [property: JsonPropertyName("cameraModels")] IReadOnlyList<string> CameraModels,
    [property: JsonPropertyName("clips")] IReadOnlyList<ChronologyItemDto> Clips
);

public record ChronologyReportDto(
    [property: JsonPropertyName("sessions")] IReadOnlyList<ChronologySessionDto> Sessions,
    [property: JsonPropertyName("totalClips")] int TotalClips,
    [property: JsonPropertyName("totalDurationSeconds")] double TotalDurationSeconds,
    [property: JsonPropertyName("earliestUtc")] DateTime? EarliestUtc,
    [property: JsonPropertyName("latestUtc")] DateTime? LatestUtc,
    [property: JsonPropertyName("anomaliesDetected")] int AnomaliesDetected
);
