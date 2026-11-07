using System.Text;
using TravelCut.Application.Services;
using Xunit;

namespace TravelCut.Tests.Services;

public class MediaStreamServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _projectRoot;
    private readonly string _testMediaFile;
    private readonly byte[] _testData;

    public MediaStreamServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "tc_media_stream_test_" + Guid.NewGuid().ToString("N")[..8]);
        _projectRoot = Path.Combine(_tempDir, "MeuProjeto");
        Directory.CreateDirectory(Path.Combine(_projectRoot, "1-Bruto"));
        Directory.CreateDirectory(Path.Combine(_projectRoot, "3-Trabalho", "cache", "proxies"));

        _testData = Encoding.UTF8.GetBytes("0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz"); // 62 bytes
        _testMediaFile = Path.Combine(_projectRoot, "3-Trabalho", "cache", "proxies", "proxy_video.mp4");
        File.WriteAllBytes(_testMediaFile, _testData);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    [Fact]
    public void BuildStreamSpec_FullContent_Returns200WithCorrectHeaders()
    {
        var spec = MediaStreamService.BuildStreamSpec(null, 1000, "video/mp4", "https://travelcut.studio");

        Assert.Equal(200, spec.StatusCode);
        Assert.Equal("OK", spec.ReasonPhrase);
        Assert.Equal("video/mp4", spec.ContentType);
        Assert.Equal(1000, spec.ContentLength);
        Assert.False(spec.IsRange);
        Assert.Contains("Accept-Ranges: bytes", spec.HeadersString);
        Assert.Contains("Content-Length: 1000", spec.HeadersString);
        Assert.Contains("Access-Control-Allow-Origin: https://travelcut.studio", spec.HeadersString);
    }

    [Fact]
    public void BuildStreamSpec_RangeStartEnd_Returns206PartialContent()
    {
        var spec = MediaStreamService.BuildStreamSpec("bytes=0-9", 62, "video/mp4", "https://travelcut.studio");

        Assert.Equal(206, spec.StatusCode);
        Assert.Equal("Partial Content", spec.ReasonPhrase);
        Assert.True(spec.IsRange);
        Assert.Equal(0, spec.Start);
        Assert.Equal(9, spec.End);
        Assert.Equal(10, spec.ContentLength);
        Assert.Contains("Content-Range: bytes 0-9/62", spec.HeadersString);
        Assert.Contains("Content-Length: 10", spec.HeadersString);
    }

    [Fact]
    public void BuildStreamSpec_RangeStartOnly_Returns206TillEndOfFile()
    {
        var spec = MediaStreamService.BuildStreamSpec("bytes=10-", 62, "video/mp4", "https://travelcut.studio");

        Assert.Equal(206, spec.StatusCode);
        Assert.Equal(10, spec.Start);
        Assert.Equal(61, spec.End);
        Assert.Equal(52, spec.ContentLength);
        Assert.Contains("Content-Range: bytes 10-61/62", spec.HeadersString);
    }

    [Fact]
    public void BuildStreamSpec_SuffixRange_ReturnsLastNBytes()
    {
        var spec = MediaStreamService.BuildStreamSpec("bytes=-12", 62, "video/mp4", "https://travelcut.studio");

        Assert.Equal(206, spec.StatusCode);
        Assert.Equal(50, spec.Start);
        Assert.Equal(61, spec.End);
        Assert.Equal(12, spec.ContentLength);
        Assert.Contains("Content-Range: bytes 50-61/62", spec.HeadersString);
    }

    [Fact]
    public void BuildStreamSpec_InvalidRange_Returns416RangeNotSatisfiable()
    {
        var spec = MediaStreamService.BuildStreamSpec("bytes=100-200", 62, "video/mp4", "https://travelcut.studio");

        Assert.Equal(416, spec.StatusCode);
        Assert.Equal("Range Not Satisfiable", spec.ReasonPhrase);
        Assert.Contains("Content-Range: bytes */62", spec.HeadersString);
    }

    [Fact]
    public void ValidateAndResolvePath_ValidProxy_ReturnsTrueAndCorrectPath()
    {
        var (ok, fullPath, reason) = MediaStreamService.ValidateAndResolvePath(_projectRoot, "/3-Trabalho/cache/proxies/proxy_video.mp4");

        Assert.True(ok);
        Assert.Equal(_testMediaFile, fullPath);
        Assert.Null(reason);
    }

    [Fact]
    public void ValidateAndResolvePath_NoProjectRoot_ReturnsFalse()
    {
        var (ok, fullPath, reason) = MediaStreamService.ValidateAndResolvePath(null, "/3-Trabalho/cache/proxies/proxy_video.mp4");

        Assert.False(ok);
        Assert.Null(fullPath);
        Assert.Equal("Nenhum projeto aberto.", reason);
    }

    [Fact]
    public void ValidateAndResolvePath_PathTraversal_IsBlocked()
    {
        var (ok, fullPath, reason) = MediaStreamService.ValidateAndResolvePath(_projectRoot, "/../Windows/System32/calc.exe");

        Assert.False(ok);
        Assert.Null(fullPath);
        Assert.Contains("Acesso restrito", reason);
    }

    [Fact]
    public void ValidateAndResolvePath_DisallowedExtension_IsBlocked()
    {
        string secretFile = Path.Combine(_projectRoot, "3-Trabalho", "cache", "secret.json");
        File.WriteAllText(secretFile, "{\"apiKey\": \"12345\"}");

        var (ok, fullPath, reason) = MediaStreamService.ValidateAndResolvePath(_projectRoot, "/3-Trabalho/cache/secret.json");

        Assert.False(ok);
        Assert.Null(fullPath);
        Assert.Contains("não autorizada", reason);
    }

    [Fact]
    public void ValidateAndResolvePath_DisallowedFolder_IsBlocked()
    {
        string outsideFolder = Path.Combine(_projectRoot, "4-Exportados");
        Directory.CreateDirectory(outsideFolder);
        string exported = Path.Combine(outsideFolder, "video.mp4");
        File.WriteAllBytes(exported, _testData);

        var (ok, fullPath, reason) = MediaStreamService.ValidateAndResolvePath(_projectRoot, "/4-Exportados/video.mp4");

        Assert.False(ok);
        Assert.Null(fullPath);
        Assert.Contains("Acesso restrito", reason);
    }

    [Fact]
    public void BoundedReadStream_ReadsOnlySpecifiedLengthAndClosesUnderlyingStream()
    {
        var ms = new MemoryStream(_testData);
        ms.Seek(10, SeekOrigin.Begin);

        using (var bounded = new BoundedReadStream(ms, 5))
        {
            byte[] buf = new byte[10];
            int read = bounded.Read(buf, 0, buf.Length);

            Assert.Equal(5, read);
            string readText = Encoding.UTF8.GetString(buf, 0, read);
            Assert.Equal("ABCDE", readText);

            int readAgain = bounded.Read(buf, 0, buf.Length);
            Assert.Equal(0, readAgain);
        }

        // MemoryStream foi descartado pelo BoundedReadStream
        Assert.Throws<ObjectDisposedException>(() => ms.ReadByte());
    }
}
