using System.Text.Json.Serialization;

namespace TravelCut.Contracts.Dtos;

// Prévia (F07). Reprodução real via <video> do WebView2 servindo arquivos do projeto por host virtual
// (https://media.travelcut.studio/<caminho relativo>), somente leitura. Nada é frame-accurate sem medição.
// Sem FFmpeg: proxies e forma de onda ficam "ffmpeg_unavailable" (nada estimado).
// Métodos IPC: media.getPlaybackSource, media.getWaveform, media.proxy.start, media.proxy.status.

public record PlaybackSourceRequest(
    [property: JsonPropertyName("projectId")] Guid ProjectId,
    [property: JsonPropertyName("mediaId")] Guid MediaId
);

public record PlaybackSourceDto(
    [property: JsonPropertyName("mediaId")] Guid MediaId,
    [property: JsonPropertyName("status")] string Status,          // original | proxy | unavailable
    [property: JsonPropertyName("url")] string? Url,               // https://media.travelcut.studio/...
    [property: JsonPropertyName("mimeType")] string? MimeType,     // ex.: video/mp4
    [property: JsonPropertyName("codec")] string? Codec,           // do FFprobe; null se desconhecido
    [property: JsonPropertyName("likelyPlayable")] bool? LikelyPlayable, // true h264/aac/vp9 em mp4/mov/webm; false prores/braw/insv/dnxhd; null desconhecido (ex.: HEVC depende da extensão do Windows)
    [property: JsonPropertyName("proxyStatus")] string ProxyStatus, // none | ready | generating | failed | ffmpeg_unavailable
    [property: JsonPropertyName("reason")] string? Reason
);

public record WaveformRequest(
    [property: JsonPropertyName("projectId")] Guid ProjectId,
    [property: JsonPropertyName("mediaId")] Guid MediaId,
    [property: JsonPropertyName("startSeconds")] double StartSeconds,
    [property: JsonPropertyName("endSeconds")] double EndSeconds,
    [property: JsonPropertyName("buckets")] int Buckets            // 16..2000
);

public record WaveformDto(
    [property: JsonPropertyName("status")] string Status,          // ok | ffmpeg_unavailable | no_audio | failed
    [property: JsonPropertyName("startSeconds")] double StartSeconds,
    [property: JsonPropertyName("bucketSeconds")] double BucketSeconds,
    [property: JsonPropertyName("peaks")] List<double> Peaks,      // pico absoluto normalizado 0..1 por bucket (áudio decodificado real)
    [property: JsonPropertyName("reason")] string? Reason
);

public record StartProxyRequest(
    [property: JsonPropertyName("projectId")] Guid ProjectId,
    [property: JsonPropertyName("mediaIds")] List<Guid> MediaIds
);

public record ProxyJobStatusDto(
    [property: JsonPropertyName("jobId")] Guid JobId,
    [property: JsonPropertyName("state")] string State,            // queued | running | completed | completed_with_errors | failed | canceled | ffmpeg_unavailable
    [property: JsonPropertyName("processed")] int Processed,
    [property: JsonPropertyName("total")] int Total,
    [property: JsonPropertyName("currentMedia")] string? CurrentMedia,
    [property: JsonPropertyName("items")] List<ProxyItemStatusDto> Items
);

public record ProxyItemStatusDto(
    [property: JsonPropertyName("mediaId")] Guid MediaId,
    [property: JsonPropertyName("state")] string State,            // pending | running | ready | failed | skipped
    [property: JsonPropertyName("message")] string? Message
);
