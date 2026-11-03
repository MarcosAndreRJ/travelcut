using TravelCut.Application.Services;
using TravelCut.Domain.Enums;
using Xunit;

namespace TravelCut.UnitTests.Application;

public class MediaScannerServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly MediaScannerService _scanner;

    public MediaScannerServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "TravelCutScannerTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _scanner = new MediaScannerService();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    [Fact]
    public void IsPathSafe_ValidSubPath_ReturnsTrue()
    {
        string subPath = Path.Combine(_tempDir, "1-Bruto", "clip1.mp4");
        bool safe = _scanner.IsPathSafe(_tempDir, subPath);
        Assert.True(safe);
    }

    [Fact]
    public void IsPathSafe_PathTraversalAttempt_ReturnsFalse()
    {
        string maliciousPath = Path.Combine(_tempDir, "..", "outside.txt");
        bool safe = _scanner.IsPathSafe(_tempDir, maliciousPath);
        Assert.False(safe);
    }

    [Fact]
    public void IsPathSafe_NullOrEmpty_ReturnsFalse()
    {
        Assert.False(_scanner.IsPathSafe("", "some/path"));
        Assert.False(_scanner.IsPathSafe(_tempDir, ""));
    }

    [Fact]
    public void ScanDirectory_DiscoversMediaInStandardFolders()
    {
        string rawDir = Path.Combine(_tempDir, "1-Bruto");
        string supportDir = Path.Combine(_tempDir, "2-Assets");
        Directory.CreateDirectory(rawDir);
        Directory.CreateDirectory(supportDir);

        File.WriteAllText(Path.Combine(rawDir, "video1.mp4"), "fake video 1");
        File.WriteAllText(Path.Combine(rawDir, "audio1.wav"), "fake audio 1");
        File.WriteAllText(Path.Combine(rawDir, "notes.txt"), "ignored text");
        File.WriteAllText(Path.Combine(supportDir, "cover.jpg"), "fake image");

        var results = _scanner.ScanDirectory(_tempDir);

        Assert.Equal(3, results.Count);
        Assert.Contains(results, r => r.RelativePath.Contains("video1.mp4") && r.Kind == MediaKind.Video);
        Assert.Contains(results, r => r.RelativePath.Contains("audio1.wav") && r.Kind == MediaKind.Audio);
        Assert.Contains(results, r => r.RelativePath.Contains("cover.jpg") && r.Kind == MediaKind.Image);
    }

    [Fact]
    public void ScanDirectory_WhenNoStandardSubfolders_ScansRoot()
    {
        File.WriteAllText(Path.Combine(_tempDir, "direct.mov"), "direct file");

        var results = _scanner.ScanDirectory(_tempDir);

        Assert.Single(results);
        Assert.Equal("direct.mov", results[0].RelativePath);
        Assert.Equal(MediaKind.Video, results[0].Kind);
    }

    [Fact]
    public async Task ComputeSha256StreamingAsync_ComputesCorrectHash()
    {
        string filePath = Path.Combine(_tempDir, "sample.bin");
        byte[] content = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        await File.WriteAllBytesAsync(filePath, content);

        string hash = await _scanner.ComputeSha256StreamingAsync(filePath);

        Assert.NotNull(hash);
        Assert.Equal(64, hash.Length); // 256 bits = 64 hex characters
        // Hash de 1,2,3,4,5,6,7,8 conhecido
        using var sha = System.Security.Cryptography.SHA256.Create();
        string expected = Convert.ToHexString(sha.ComputeHash(content)).ToLowerInvariant();
        Assert.Equal(expected, hash);
    }

    [Fact]
    public async Task ComputeSha256StreamingAsync_FileNotFound_Throws()
    {
        string missing = Path.Combine(_tempDir, "missing.bin");
        await Assert.ThrowsAsync<FileNotFoundException>(() => _scanner.ComputeSha256StreamingAsync(missing));
    }
}
