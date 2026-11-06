using System.Net;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using TravelCut.Application.Ports;
using TravelCut.Infrastructure.Tooling;
using Xunit;

namespace TravelCut.UnitTests.Infrastructure;

public class HttpModelDownloaderTests : IDisposable
{
    private readonly string _tempDir;

    public HttpModelDownloaderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "tc-dl-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, true);
        }
        catch { }
    }

    [Fact]
    public async Task DownloadFileAsync_WhenDestinationAlreadyExistsWithValidHash_DoesNotDownload()
    {
        var dest = Path.Combine(_tempDir, "existing.bin");
        var content = "valid-model-content"u8.ToArray();
        await File.WriteAllBytesAsync(dest, content);
        var expectedHash = Convert.ToHexStringLower(SHA256.HashData(content));

        var handler = new TrackingHandler(content);
        using var client = new HttpClient(handler);
        var downloader = new HttpModelDownloader(client, NullLogger<HttpModelDownloader>.Instance);

        await downloader.DownloadFileAsync("https://example.com/model.bin", dest, expectedHash);

        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task DownloadFileAsync_WhenHashMatches_DownloadsAndReplacesDestinationAtomically()
    {
        var dest = Path.Combine(_tempDir, "new-model.bin");
        var content = "brand-new-model-bytes"u8.ToArray();
        var expectedHash = Convert.ToHexStringLower(SHA256.HashData(content));

        var handler = new TrackingHandler(content);
        using var client = new HttpClient(handler);
        var downloader = new HttpModelDownloader(client, NullLogger<HttpModelDownloader>.Instance);

        await downloader.DownloadFileAsync("https://example.com/new-model.bin", dest, expectedHash);

        Assert.Equal(1, handler.RequestCount);
        Assert.True(File.Exists(dest));
        var onDisk = await File.ReadAllBytesAsync(dest);
        Assert.Equal(content, onDisk);
    }

    [Fact]
    public async Task DownloadFileAsync_WhenHashMismatch_ThrowsAndDeletesTempFile()
    {
        var dest = Path.Combine(_tempDir, "corrupted.bin");
        var content = "actual-bytes"u8.ToArray();
        var wrongHash = "0000000000000000000000000000000000000000000000000000000000000000";

        var handler = new TrackingHandler(content);
        using var client = new HttpClient(handler);
        var downloader = new HttpModelDownloader(client, NullLogger<HttpModelDownloader>.Instance);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            downloader.DownloadFileAsync("https://example.com/corrupted.bin", dest, wrongHash));

        Assert.Contains("Hash SHA-256", ex.Message);
        Assert.False(File.Exists(dest));
        var tempFiles = Directory.GetFiles(_tempDir, "*.tmp.*");
        Assert.Empty(tempFiles);
    }

    [Fact]
    public async Task DownloadFileAsync_WhenCancelled_CleansUpTempFileAndThrows()
    {
        var payload = new byte[1024 * 1024];
        new Random(42).NextBytes(payload);
        using var sha = SHA256.Create();
        var expectedHash = Convert.ToHexString(sha.ComputeHash(payload));

        var dest = Path.Combine(_tempDir, "cancelled_model.bin");
        using var client = new HttpClient(new DelayingTrackingHandler(payload, delayMs: 500));
        var downloader = new HttpModelDownloader(client, NullLogger<HttpModelDownloader>.Instance);

        using var cts = new CancellationTokenSource(50);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            downloader.DownloadFileAsync("https://example.com/large.bin", dest, expectedHash, ct: cts.Token));

        Assert.False(File.Exists(dest));
        var tempFiles = Directory.GetFiles(_tempDir, "*.tmp.*");
        Assert.Empty(tempFiles);
    }

    [Fact]
    public void OfficialWhisperLargeV3Sha256_MatchesOpenAiOfficialDefinition()
    {
        const string expectedOfficialHash = "e5b1a55b89c1367dacf97e3e19bfd829a01529dbfdeefa8caeb59b3f1b81dadb";
        Assert.Equal(expectedOfficialHash, ToolPathResolver.OfficialWhisperLargeV3Sha256);
        Assert.Contains(expectedOfficialHash, ToolPathResolver.OfficialWhisperLargeV3Url);
    }

    private sealed class DelayingTrackingHandler : HttpMessageHandler
    {
        private readonly byte[] _payload;
        private readonly int _delayMs;

        public DelayingTrackingHandler(byte[] payload, int delayMs)
        {
            _payload = payload;
            _delayMs = delayMs;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(_delayMs, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(_payload)
            };
        }
    }

    private sealed class TrackingHandler : HttpMessageHandler
    {
        private readonly byte[] _payload;
        public int RequestCount { get; private set; }

        public TrackingHandler(byte[] payload)
        {
            _payload = payload;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(_payload)
            };
            return Task.FromResult(response);
        }
    }
}
