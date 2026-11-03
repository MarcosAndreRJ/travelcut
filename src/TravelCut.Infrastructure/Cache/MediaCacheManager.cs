using System.Security.Cryptography;
using System.Text;
using TravelCut.Application.Ports;

namespace TravelCut.Infrastructure.Cache;

public class MediaCacheManager : IMediaCacheManager
{
    public string ComputeCacheKey(string fingerprint, string toolId, string profileVersion, string schemaVersion = "1.0")
    {
        string raw = $"{fingerprint}:{toolId}:{profileVersion}:{schemaVersion}";
        using var sha = SHA256.Create();
        byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public Task<bool> HasArtifactAsync(string projectRoot, string category, string cacheKey, CancellationToken ct = default)
    {
        string path = ResolvePath(projectRoot, category, cacheKey);
        return Task.FromResult(File.Exists(path));
    }

    public async Task<string?> ReadArtifactTextAsync(string projectRoot, string category, string cacheKey, CancellationToken ct = default)
    {
        string path = ResolvePath(projectRoot, category, cacheKey);
        if (!File.Exists(path))
            return null;

        return await File.ReadAllTextAsync(path, Encoding.UTF8, ct);
    }

    public async Task WriteArtifactTextAsync(string projectRoot, string category, string cacheKey, string content, CancellationToken ct = default)
    {
        string path = ResolvePath(projectRoot, category, cacheKey);
        string dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);

        string tempPath = Path.Combine(dir, $"{cacheKey}_{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(tempPath, content, Encoding.UTF8, ct);
            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { }
            }
        }
    }

    public async Task<byte[]?> ReadArtifactBinaryAsync(string projectRoot, string category, string cacheKey, CancellationToken ct = default)
    {
        string path = ResolvePath(projectRoot, category, cacheKey);
        if (!File.Exists(path))
            return null;

        return await File.ReadAllBytesAsync(path, ct);
    }

    public async Task WriteArtifactBinaryAsync(string projectRoot, string category, string cacheKey, byte[] data, CancellationToken ct = default)
    {
        string path = ResolvePath(projectRoot, category, cacheKey);
        string dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);

        string tempPath = Path.Combine(dir, $"{cacheKey}_{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllBytesAsync(tempPath, data, ct);
            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { }
            }
        }
    }

    public Task InvalidateArtifactsAsync(string projectRoot, string cacheKeyPattern, CancellationToken ct = default)
    {
        string cacheBase = Path.Combine(projectRoot, "3-Trabalho", "cache");
        if (!Directory.Exists(cacheBase))
            return Task.CompletedTask;

        try
        {
            var files = Directory.EnumerateFiles(cacheBase, $"*{cacheKeyPattern}*", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                try { File.Delete(file); } catch { }
            }
        }
        catch { }

        return Task.CompletedTask;
    }

    private static string ResolvePath(string projectRoot, string category, string cacheKey)
    {
        // Higienização contra path traversal
        string safeCategory = string.Join("_", category.Split(Path.GetInvalidFileNameChars()));
        string safeKey = string.Join("_", cacheKey.Split(Path.GetInvalidFileNameChars()));

        string cacheBase = Path.Combine(Path.GetFullPath(projectRoot), "3-Trabalho", "cache", safeCategory);
        string resolved = Path.Combine(cacheBase, $"{safeKey}.dat");

        // Confirma que o caminho resultante está dentro de projectRoot
        if (!resolved.StartsWith(Path.GetFullPath(projectRoot), StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Tentativa de path traversal detectada no cache.");

        return resolved;
    }
}
