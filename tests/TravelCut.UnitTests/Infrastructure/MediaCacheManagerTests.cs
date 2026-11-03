using TravelCut.Infrastructure.Cache;
using Xunit;

namespace TravelCut.UnitTests.Infrastructure;

public class MediaCacheManagerTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly MediaCacheManager _cacheManager;

    public MediaCacheManagerTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "TravelCutCacheTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        _cacheManager = new MediaCacheManager();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
                Directory.Delete(_tempRoot, recursive: true);
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    [Fact]
    public void ComputeCacheKey_DeterministicAndUnique()
    {
        string key1 = _cacheManager.ComputeCacheKey("fp_12345", "whisper", "v1.0");
        string key2 = _cacheManager.ComputeCacheKey("fp_12345", "whisper", "v1.0");
        string key3 = _cacheManager.ComputeCacheKey("fp_12345", "silero_vad", "v1.0");

        Assert.Equal(key1, key2);
        Assert.NotEqual(key1, key3);
        Assert.Equal(64, key1.Length); // SHA-256 hex string
    }

    [Fact]
    public async Task WriteAndReadArtifactText_SavesInThreeTrabalhoCache()
    {
        string key = _cacheManager.ComputeCacheKey("fp_abc", "whisper", "v1");
        string content = "{\"text\": \"Olá Tóquio\", \"words\": []}";

        await _cacheManager.WriteArtifactTextAsync(_tempRoot, "transcripts", key, content);

        bool exists = await _cacheManager.HasArtifactAsync(_tempRoot, "transcripts", key);
        Assert.True(exists);

        string? read = await _cacheManager.ReadArtifactTextAsync(_tempRoot, "transcripts", key);
        Assert.Equal(content, read);

        // Verifica caminho físico esperado em 3-Trabalho/cache/transcripts
        string expectedPath = Path.Combine(_tempRoot, "3-Trabalho", "cache", "transcripts", $"{key}.dat");
        Assert.True(File.Exists(expectedPath));
    }

    [Fact]
    public async Task WriteAndReadArtifactBinary_SavesAndRetrievesBytes()
    {
        string key = _cacheManager.ComputeCacheKey("fp_bin", "waveform", "v1");
        byte[] data = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };

        await _cacheManager.WriteArtifactBinaryAsync(_tempRoot, "waveforms", key, data);

        bool exists = await _cacheManager.HasArtifactAsync(_tempRoot, "waveforms", key);
        Assert.True(exists);

        byte[]? read = await _cacheManager.ReadArtifactBinaryAsync(_tempRoot, "waveforms", key);
        Assert.NotNull(read);
        Assert.Equal(data, read);
    }

    [Fact]
    public async Task InvalidateArtifactsAsync_RemovesMatchingFiles()
    {
        string key1 = _cacheManager.ComputeCacheKey("fp_inv1", "tool", "v1");
        string key2 = _cacheManager.ComputeCacheKey("fp_inv2", "tool", "v1");

        await _cacheManager.WriteArtifactTextAsync(_tempRoot, "transcripts", key1, "test1");
        await _cacheManager.WriteArtifactTextAsync(_tempRoot, "transcripts", key2, "test2");

        await _cacheManager.InvalidateArtifactsAsync(_tempRoot, key1);

        bool exists1 = await _cacheManager.HasArtifactAsync(_tempRoot, "transcripts", key1);
        bool exists2 = await _cacheManager.HasArtifactAsync(_tempRoot, "transcripts", key2);

        Assert.False(exists1);
        Assert.True(exists2);
    }
}
