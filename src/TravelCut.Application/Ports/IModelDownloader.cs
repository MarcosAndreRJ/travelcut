namespace TravelCut.Application.Ports;

public sealed record DownloadProgressUpdate(
    long BytesRead,
    long? TotalBytes,
    double? Percent
);

public interface IModelDownloader
{
    Task DownloadFileAsync(
        string url,
        string destinationFilePath,
        string expectedSha256,
        IProgress<DownloadProgressUpdate>? progress = null,
        CancellationToken ct = default
    );
}
