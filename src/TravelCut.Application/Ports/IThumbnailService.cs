namespace TravelCut.Application.Ports;

public static class ThumbnailStatus
{
    public const string Ok = "ok";
    public const string FfmpegUnavailable = "ffmpeg_unavailable";
    public const string Failed = "failed";
    public const string NotSupported = "not_supported";
}

public sealed record ThumbnailResult(string Status, string? DataUrl, string? Reason);

/// <summary>Geração de miniaturas (subprocesso FFmpeg com cache em 3-Trabalho/cache/thumbs).</summary>
public interface IThumbnailService
{
    Task<ThumbnailResult> GenerateAsync(string projectRoot, string mediaPath, double atSeconds, CancellationToken ct = default);
}
