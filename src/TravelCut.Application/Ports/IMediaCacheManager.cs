namespace TravelCut.Application.Ports;

public interface IMediaCacheManager
{
    string ComputeCacheKey(string fingerprint, string toolId, string profileVersion, string schemaVersion = "1.0");
    Task<bool> HasArtifactAsync(string projectRoot, string category, string cacheKey, CancellationToken ct = default);
    Task<string?> ReadArtifactTextAsync(string projectRoot, string category, string cacheKey, CancellationToken ct = default);
    Task WriteArtifactTextAsync(string projectRoot, string category, string cacheKey, string content, CancellationToken ct = default);
    Task<byte[]?> ReadArtifactBinaryAsync(string projectRoot, string category, string cacheKey, CancellationToken ct = default);
    Task WriteArtifactBinaryAsync(string projectRoot, string category, string cacheKey, byte[] data, CancellationToken ct = default);
    Task InvalidateArtifactsAsync(string projectRoot, string cacheKeyPattern, CancellationToken ct = default);
}
