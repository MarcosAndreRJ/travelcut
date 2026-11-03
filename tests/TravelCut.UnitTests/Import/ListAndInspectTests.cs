using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using TravelCut.Application.Services;
using TravelCut.Application.UseCases;
using TravelCut.Contracts.Dtos;
using TravelCut.Domain.Entities;
using TravelCut.Domain.Enums;
using Xunit;

namespace TravelCut.Tests.Import;

public class ListAndInspectTests
{
    [Fact]
    public async Task ListDirectoryReturnsOnlyFoldersAndMediaFilesFoldersFirst()
    {
        using var tmp = new TempDirectory();
        Directory.CreateDirectory(Path.Combine(tmp.Path, "Sub"));
        File.WriteAllBytes(Path.Combine(tmp.Path, "b-clip.mp4"), new byte[10]);
        File.WriteAllBytes(Path.Combine(tmp.Path, "a-audio.wav"), new byte[20]);
        File.WriteAllBytes(Path.Combine(tmp.Path, "d-foto.JPG"), new byte[30]);
        File.WriteAllText(Path.Combine(tmp.Path, "notas.txt"), "não é mídia");
        File.WriteAllBytes(Path.Combine(tmp.Path, "oculto.mp4"), new byte[5]);
        File.SetAttributes(Path.Combine(tmp.Path, "oculto.mp4"), FileAttributes.Hidden);

        var useCase = new ListDirectoryUseCase(NullLogger<ListDirectoryUseCase>.Instance);
        var (ok, response, code, message) = await useCase.ExecuteAsync(tmp.Path);

        Assert.True(ok, message);
        Assert.Null(code);
        Assert.NotNull(response);
        Assert.Equal(Path.GetFullPath(tmp.Path), response!.Path);

        var names = response.Entries.Select(e => e.Name).ToList();
        Assert.Equal(new[] { "Sub", "a-audio.wav", "b-clip.mp4", "d-foto.JPG" }, names);

        var folder = response.Entries[0];
        Assert.True(folder.IsDirectory);
        Assert.Null(folder.Kind);
        Assert.Null(folder.ByteLength);

        Assert.Equal("Audio", response.Entries[1].Kind);
        Assert.Equal(20, response.Entries[1].ByteLength);
        Assert.Equal("Video", response.Entries[2].Kind);
        Assert.Equal("Image", response.Entries[3].Kind);
    }

    [Fact]
    public async Task ListDirectoryMissingFolderReturnsDirectoryNotFound()
    {
        var useCase = new ListDirectoryUseCase(NullLogger<ListDirectoryUseCase>.Instance);
        var (ok, response, code, _) = await useCase.ExecuteAsync(Path.Combine(Path.GetTempPath(), "tc_nao_existe_" + Guid.NewGuid().ToString("N")));

        Assert.False(ok);
        Assert.Null(response);
        Assert.Equal("DIRECTORY_NOT_FOUND", code);
    }

    [Fact]
    public async Task ListDirectoryWithNullPathReturnsDrivesOnly()
    {
        var useCase = new ListDirectoryUseCase(NullLogger<ListDirectoryUseCase>.Instance);
        var (ok, response, _, _) = await useCase.ExecuteAsync(null);

        Assert.True(ok);
        Assert.NotNull(response);
        Assert.Null(response!.Path);
        Assert.All(response.Entries, e => Assert.True(e.IsDirectory));
    }

    [Fact]
    public void ExpanderRecursesSkipsHiddenTcpartAndNonMedia()
    {
        using var tmp = new TempDirectory();
        string card = Path.Combine(tmp.Path, "cartao");
        Directory.CreateDirectory(Path.Combine(card, "DCIM", "100GOPRO"));
        File.WriteAllBytes(Path.Combine(card, "DCIM", "100GOPRO", "GOPR0001.MP4"), new byte[100]);
        File.WriteAllBytes(Path.Combine(card, "DCIM", "100GOPRO", "GOPR0002.mp4.tcpart"), new byte[100]);
        File.WriteAllBytes(Path.Combine(card, "DCIM", "100GOPRO", "thumb.thm"), new byte[100]);
        File.WriteAllBytes(Path.Combine(card, "raiz.wav"), new byte[50]);
        File.WriteAllBytes(Path.Combine(card, "escondido.mov"), new byte[50]);
        File.SetAttributes(Path.Combine(card, "escondido.mov"), FileAttributes.Hidden);

        var result = MediaFileExpander.Expand(new[] { card });

        Assert.False(result.Truncated);
        var names = result.Files.Select(f => f.FileName).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();
        Assert.Equal(new[] { "GOPR0001.MP4", "raiz.wav" }, names);
        Assert.Contains(result.Files, f => f.FileName == "GOPR0001.MP4" && f.Container == "MP4" && f.Kind == MediaKind.Video && f.OriginFolder == "100GOPRO");
        Assert.DoesNotContain(result.Files, f => f.FileName.EndsWith(".tcpart"));
        Assert.DoesNotContain(result.Files, f => f.FileName == "thumb.thm");
        Assert.DoesNotContain(result.Files, f => f.FileName == "escondido.mov");
    }

    [Fact]
    public void ExpanderStopsAtLimitAndReportsTruncation()
    {
        using var tmp = new TempDirectory();
        for (int i = 0; i < 5; i++)
        {
            File.WriteAllBytes(Path.Combine(tmp.Path, $"clip{i}.mp4"), new byte[8]);
        }

        var result = MediaFileExpander.Expand(new[] { tmp.Path }, limit: 3);

        Assert.True(result.Truncated);
        Assert.Equal(3, result.Files.Count);
    }

    [Fact]
    public async Task InspectWithoutFfprobeReturnsUnavailableStatusAndNoInventedValues()
    {
        using var tmp = new TempDirectory();
        string root = Path.Combine(tmp.Path, "Projeto");
        using var h = new ImportHarness();
        var project = h.CreateProject(root);

        string inside = Path.Combine(root, "1-Bruto", "dentro.mp4");
        TestFiles.WriteSource(inside, TestFiles.RandomBytes(256));
        string outside = TestFiles.WriteSource(Path.Combine(tmp.Path, "cartao", "fora.mov"), TestFiles.RandomBytes(256));
        h.Media.Assets.Add(new MediaAsset { ProjectId = project.ProjectId, RelativePath = "1-Bruto/dentro.mp4", ByteLength = 256, Kind = MediaKind.Video });

        var useCase = new InspectCandidatesUseCase(h.Projects, h.Media, h.Ffprobe, NullLogger<InspectCandidatesUseCase>.Instance);
        var (ok, response, code, _) = await useCase.ExecuteAsync(new InspectCandidatesRequest(
            project.ProjectId, new[] { Path.Combine(root, "1-Bruto"), outside, Path.Combine(tmp.Path, "nao-existe.mp4") }));

        Assert.True(ok, code);
        Assert.NotNull(response);
        Assert.False(response!.FfprobeAvailable);
        Assert.False(response.Truncated);
        Assert.Contains(response.Errors, e => e.Contains("nao-existe.mp4"));

        var inlib = Assert.Single(response.Items, i => i.FileName == "dentro.mp4");
        Assert.Equal("ffprobe_unavailable", inlib.MetadataStatus);
        Assert.Null(inlib.DurationSeconds);
        Assert.Null(inlib.Width);
        Assert.Null(inlib.Height);
        Assert.Null(inlib.Codec);
        Assert.True(inlib.InsideProject);
        Assert.True(inlib.AlreadyInLibrary);
        Assert.Equal("MP4", inlib.Container);
        Assert.Equal("Video", inlib.Kind);

        var external = Assert.Single(response.Items, i => i.FileName == "fora.mov");
        Assert.False(external.InsideProject);
        Assert.False(external.AlreadyInLibrary);
        Assert.Equal(0, h.Ffprobe.ExtractCalls);
    }

    [Fact]
    public async Task InspectWithFfprobeAvailableReportsStatusPerFileAndNullsNonOkValues()
    {
        using var tmp = new TempDirectory();
        string root = Path.Combine(tmp.Path, "Projeto");
        using var h = new ImportHarness();
        var project = h.CreateProject(root);
        h.Ffprobe.Available = true;

        string good = TestFiles.WriteSource(Path.Combine(tmp.Path, "cartao", "bom.mp4"), TestFiles.RandomBytes(128));
        string bad = TestFiles.WriteSource(Path.Combine(tmp.Path, "cartao", "ruim.mp4"), TestFiles.RandomBytes(128));
        h.Ffprobe.Responder = path => Path.GetFileName(path) == "bom.mp4"
            ? new ExtractedMediaInfo("h264", 3840, 2160, new(1, 30), 30, 42.5, 1275, 2, 48000, null, "creation_time_utc_tag",
                false, 0, null, null, MediaMetadataStatus.Ok)
            : ExtractedMediaInfo.Unavailable(path, MediaMetadataStatus.Failed);

        var useCase = new InspectCandidatesUseCase(h.Projects, h.Media, h.Ffprobe, NullLogger<InspectCandidatesUseCase>.Instance);
        var (ok, response, _, _) = await useCase.ExecuteAsync(new InspectCandidatesRequest(project.ProjectId, new[] { good, bad }));

        Assert.True(ok);
        Assert.True(response!.FfprobeAvailable);

        var okItem = Assert.Single(response.Items, i => i.FileName == "bom.mp4");
        Assert.Equal("ok", okItem.MetadataStatus);
        Assert.Equal(42.5, okItem.DurationSeconds);
        Assert.Equal(3840, okItem.Width);
        Assert.Equal(2160, okItem.Height);
        Assert.Equal("h264", okItem.Codec);

        var badItem = Assert.Single(response.Items, i => i.FileName == "ruim.mp4");
        Assert.Equal("failed", badItem.MetadataStatus);
        Assert.Null(badItem.DurationSeconds);
        Assert.Null(badItem.Width);
        Assert.Null(badItem.Codec);
    }
}
