using TravelCut.Application.Ipc;
using TravelCut.Application.UseCases;
using Xunit;

namespace TravelCut.UnitTests.Preview;

public class PreviewPathsTests
{
    [Fact]
    public void BuildMediaUrl_EscapesEachSegmentAndKeepsSeparators()
    {
        string url = PreviewPaths.BuildMediaUrl("1-Bruto/Viagem Açores/cena 1.mp4");

        Assert.Equal("https://media.travelcut.studio/1-Bruto/Viagem%20A%C3%A7ores/cena%201.mp4", url);
    }

    [Fact]
    public void BuildMediaUrl_PreservesSeparatorsAndEscapesPercent()
    {
        string url = PreviewPaths.BuildMediaUrl("2-Assets/a b/c%d.mov");

        Assert.Equal("https://media.travelcut.studio/2-Assets/a%20b/c%25d.mov", url);
        // "https:", "", host, "2-Assets", "a%20b", "c%25d.mov": a "/" dentro de um nome vira separador, não conteúdo.
        Assert.Equal(6, url.Split('/').Length);
    }

    [Theory]
    [InlineData("../fora.mp4")]
    [InlineData("a/../b.mp4")]
    [InlineData("/abs/x.mp4")]
    [InlineData("C:/x.mp4")]
    [InlineData("a//b.mp4")]
    [InlineData("./x.mp4")]
    [InlineData("")]
    public void IsSafeRelativePath_RejectsUnsafeShapes(string path)
    {
        Assert.False(PreviewPaths.IsSafeRelativePath(path));
        Assert.False(PreviewPaths.TryResolveInsideRoot(@"K:\projeto", path, out _));
    }

    [Fact]
    public void IsSafeRelativePath_AcceptsDotDotPrefixedNames()
    {
        Assert.True(PreviewPaths.IsSafeRelativePath("1-Bruto/..clip.mp4"));
        Assert.True(PreviewPaths.IsSafeRelativePath("1-Bruto/Viagem Açores/cena 1.mp4"));
    }

    [Fact]
    public void TryResolveInsideRoot_KeepsResultUnderRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "tc-root-check");

        Assert.True(PreviewPaths.TryResolveInsideRoot(root, "1-Bruto/a.mp4", out string full));
        Assert.StartsWith(Path.GetFullPath(root), full, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("h264", "mp4", true)]
    [InlineData("avc1", "mov", true)]
    [InlineData("aac", "m4a", true)]
    [InlineData("vp9", "webm", true)]
    [InlineData("av1", "mp4", true)]
    [InlineData("opus", "webm", true)]
    [InlineData("mp3", "mp3", true)]
    [InlineData("h264", "mkv", null)]
    [InlineData("prores", "mov", false)]
    [InlineData("dnxhd", "mxf", false)]
    [InlineData("dnxhr", "mov", false)]
    [InlineData("braw", "braw", false)]
    [InlineData("insv", "insv", false)]
    [InlineData("cfhd", "mov", false)]
    [InlineData("mxf", "mxf", false)]
    [InlineData("hevc", "mp4", null)]
    [InlineData("h265", "mov", null)]
    [InlineData("xyzcodec", "mp4", null)]
    [InlineData("", "mp4", null)]
    public void ClassifyPlayable_FollowsCodecAndContainerGroups(string codec, string ext, bool? expected)
    {
        Assert.Equal(expected, PreviewPaths.ClassifyPlayable(codec, ext));
    }

    [Fact]
    public void ClassifyPlayable_NullCodecIsUnknown()
    {
        Assert.Null(PreviewPaths.ClassifyPlayable(null, "mp4"));
    }

    [Fact]
    public void ProxyFileName_IsStableAndSafeForAnyFingerprint()
    {
        var id = Guid.Parse("11111111-2222-3333-4444-555555555555");

        string name = PreviewPaths.ProxyFileName(id, "10:00/2026 ação?*");

        Assert.Equal(PreviewPaths.ProxyFileName(id, "10:00/2026 ação?*"), name);
        Assert.Matches(@"^11111111-2222-3333-4444-555555555555-[0-9a-f]{12}\.mp4$", name);
        Assert.Equal(PreviewPaths.ProxyFileName(id, "outro"), PreviewPaths.ProxyFileName(id, "outro"));
        Assert.NotEqual(name, PreviewPaths.ProxyFileName(id, "outro"));
    }

    [Theory]
    [InlineData("clip.mp4", "video/mp4")]
    [InlineData("clip.MOV", "video/quicktime")]
    [InlineData("clip.webm", "video/webm")]
    [InlineData("arquivo.xyz", "application/octet-stream")]
    public void MimeTypeFor_UsesExtension(string name, string expected)
    {
        Assert.Equal(expected, PreviewPaths.MimeTypeFor(name));
    }

    [Fact]
    public void WaveformCacheKey_ChangesWithAnyInput()
    {
        string a = PreviewPaths.WaveformCacheKey("fp", 0, 1, 100);

        Assert.Equal(64, a.Length);
        Assert.NotEqual(a, PreviewPaths.WaveformCacheKey("fp", 0, 1, 101));
        Assert.NotEqual(a, PreviewPaths.WaveformCacheKey("fp", 0, 2, 100));
        Assert.NotEqual(a, PreviewPaths.WaveformCacheKey("fp2", 0, 1, 100));
    }

    [Fact]
    public void MediaHost_IsNeverAnIpcOrigin()
    {
        // O host de mídia serve arquivos do projeto; nunca é origem de mensagens IPC nem de navegação de topo.
        Assert.False(IpcOriginPolicy.IsTrustedOrigin("https://media.travelcut.studio/1-Bruto/a.mp4", allowDevServer: false));
        Assert.False(IpcOriginPolicy.IsTrustedOrigin("https://media.travelcut.studio/", allowDevServer: true));
        Assert.True(IpcOriginPolicy.IsTrustedOrigin("https://travelcut.studio/index.html", allowDevServer: false));
    }
}
