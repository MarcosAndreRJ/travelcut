using Microsoft.Extensions.Logging.Abstractions;
using TravelCut.Application.Services;
using TravelCut.Application.UseCases;
using TravelCut.Domain.Entities;
using TravelCut.Domain.Enums;
using TravelCut.Tests;
using Xunit;

namespace TravelCut.Tests.Import;

public class PathSafetyTests
{
    [Theory]
    [InlineData("..")]
    [InlineData("../x.mp4")]
    [InlineData("a/../b.mp4")]
    [InlineData(@"a\..\b.mp4")]
    [InlineData("/absoluto.mp4")]
    [InlineData(@"\absoluto.mp4")]
    [InlineData(@"C:\fora\clip.mp4")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void UnsafeLibraryRelativePath_IsRejected(string? relative)
    {
        Assert.False(ImportPathRules.IsSafeLibraryRelativePath(relative));
    }

    [Theory]
    [InlineData("1-Bruto/clip.mp4")]
    [InlineData(@"1-Bruto\clip.mp4")]
    [InlineData("..clip.mp4")]
    [InlineData("2-Assets/sub/..x/clip.mp4")]
    public void SafeLibraryRelativePath_IsAccepted(string relative)
    {
        Assert.True(ImportPathRules.IsSafeLibraryRelativePath(relative));
    }

    [Fact]
    public async Task RegisterDiscovered_WithRelativePathDotDot_NeverReachesTheLibrary()
    {
        using var tmp = new TempDirectory();
        string root = Path.Combine(tmp.Path, "Projeto");
        Directory.CreateDirectory(Path.Combine(root, "1-Bruto"));
        string valid = Path.Combine(root, "1-Bruto", "ok.mp4");
        File.WriteAllBytes(valid, new byte[16]);

        var projects = new InMemoryProjectRepository();
        var media = new InMemoryMediaRepository();
        var project = new Project { ProjectId = Guid.NewGuid(), Name = "P", RootPath = root };
        projects.Projects.Add(project);

        var scanner = new FakeScanner
        {
            Discovered =
            {
                Described(root, "..", Path.Combine(tmp.Path, "fora.mp4")),
                Described(root, "1-Bruto/ok.mp4", valid)
            }
        };
        var scan = new ScanMediaLibraryUseCase(projects, media, scanner, new FakeFfprobeExtractor(),
            NullLogger<ScanMediaLibraryUseCase>.Instance);

        var (ok, result, _, _) = await scan.ExecuteAsync(project.ProjectId);

        Assert.True(ok);
        var asset = Assert.Single(media.Assets);
        Assert.Equal("1-Bruto/ok.mp4", asset.RelativePath);
        Assert.Equal(2, result!.TotalFilesFound);
    }

    [Fact]
    public void ScanDirectory_DoesNotFollowJunction_ToFilesOutsideTheProject()
    {
        using var tmp = new TempDirectory();
        string root = Path.Combine(tmp.Path, "Projeto");
        string external = Path.Combine(tmp.Path, "Externo");
        string junction = Path.Combine(root, "1-Bruto", "link");
        Directory.CreateDirectory(Path.Combine(root, "1-Bruto"));
        Directory.CreateDirectory(external);
        File.WriteAllBytes(Path.Combine(external, "fora.mp4"), new byte[8]);
        File.WriteAllBytes(Path.Combine(root, "1-Bruto", "dentro.mp4"), new byte[8]);
        TestJunction.Create(junction, external);
        try
        {
            var found = new MediaScannerService().ScanDirectory(root);

            var only = Assert.Single(found);
            Assert.Equal("1-Bruto/dentro.mp4", only.RelativePath);
            Assert.DoesNotContain(found, f => f.FullPath.Contains("fora.mp4", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            TestJunction.Remove(junction);
        }
    }

    [Fact]
    public void ScanDirectory_IgnoresPartialImportFiles_ButKeepsRegularMedia()
    {
        using var tmp = new TempDirectory();
        string root = Path.Combine(tmp.Path, "Projeto");
        Directory.CreateDirectory(Path.Combine(root, "2-Assets"));
        File.WriteAllBytes(Path.Combine(root, "2-Assets", "musica.mp3"), new byte[8]);
        File.WriteAllBytes(Path.Combine(root, "2-Assets", "parcial.mp4.tcpart"), new byte[8]);

        var found = new MediaScannerService().ScanDirectory(root);

        var only = Assert.Single(found);
        Assert.Equal("2-Assets/musica.mp3", only.RelativePath);
        Assert.Equal(MediaKind.Audio, only.Kind);
    }

    private static DiscoveredMediaFile Described(string root, string relative, string full) => new(
        FullPath: full,
        RelativePath: relative,
        ByteLength: 16,
        LastWriteTimeUtc: DateTime.UtcNow,
        QuickFingerprint: "q1_test",
        Kind: MediaKind.Video,
        Extension: ".mp4",
        Subfolder: "1-Bruto");

    private sealed class FakeScanner : IMediaScannerService
    {
        public List<DiscoveredMediaFile> Discovered { get; } = new();

        public IReadOnlyList<DiscoveredMediaFile> ScanDirectory(string rootPath, IEnumerable<string>? allowedSubfolders = null) => Discovered;

        public Task<string> ComputeSha256StreamingAsync(string filePath, CancellationToken ct = default) => Task.FromResult("sha");

        public bool IsPathSafe(string rootPath, string candidatePath) => true;

        public DiscoveredMediaFile? DescribeFile(string rootPath, string filePath) => null;
    }
}
