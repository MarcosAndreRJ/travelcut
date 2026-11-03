using Microsoft.Extensions.Logging.Abstractions;
using TravelCut.Application.Services;
using TravelCut.Application.UseCases;
using TravelCut.Domain.Entities;
using TravelCut.Domain.Enums;
using TravelCut.Domain.ValueObjects;
using Xunit;

namespace TravelCut.Tests.Import;

public class ScanRegistrationTests
{
    private static ExtractedMediaInfo OkInfo() => new(
        "h264", 1920, 1080, new(1, 30), 30, 9.0, 270, 2, 48000, null, "creation_time_utc_tag",
        false, 0, "Sony A7S III", null, MediaMetadataStatus.Ok);

    [Fact]
    public async Task RegisterWithoutAnalysisStoresNotRequestedAndNeverCallsFfprobe()
    {
        using var tmp = new TempDirectory();
        string root = Path.Combine(tmp.Path, "Projeto");
        using var h = new ImportHarness();
        var project = h.CreateProject(root);
        string file = TestFiles.WriteSource(Path.Combine(root, "1-Bruto", "a.mp4"), TestFiles.RandomBytes(512));

        var scan = new ScanMediaLibraryUseCase(
            h.Projects, h.Media, new MediaScannerService(), h.Ffprobe,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ScanMediaLibraryUseCase>.Instance);

        var (ok, result, _, _) = await scan.RegisterFilesAsync(project.ProjectId, new[] { file }, analyzeMetadata: false);

        Assert.True(ok);
        Assert.Equal(1, result!.NewAssetsAdded);
        var asset = Assert.Single(h.Media.Assets);
        Assert.Equal(MediaMetadataStatus.NotRequested, asset.Metadata!.MetadataStatus);
        Assert.Equal(0, asset.Metadata.DurationSeconds);
        Assert.Equal(0, asset.Metadata.Width);
        Assert.Equal("unknown", asset.Metadata.Codec);
        Assert.Null(asset.Metadata.CameraModel);
        Assert.Equal(0, h.Ffprobe.ExtractCalls);
    }

    [Fact]
    public async Task NotRequestedAssetsAreReanalyzedWhenAnalysisIsRequestedAndTagsArePreserved()
    {
        using var tmp = new TempDirectory();
        string root = Path.Combine(tmp.Path, "Projeto");
        using var h = new ImportHarness();
        var project = h.CreateProject(root);
        string file = TestFiles.WriteSource(Path.Combine(root, "1-Bruto", "b.mp4"), TestFiles.RandomBytes(512));

        var scan = new ScanMediaLibraryUseCase(
            h.Projects, h.Media, new MediaScannerService(), h.Ffprobe,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ScanMediaLibraryUseCase>.Instance);

        await scan.RegisterFilesAsync(project.ProjectId, new[] { file }, analyzeMetadata: false);
        h.Media.Assets[0].Tags = new List<string> { "viagem" };

        h.Ffprobe.Available = true;
        h.Ffprobe.Responder = _ => OkInfo();
        var (ok, result, _, _) = await scan.RegisterFilesAsync(project.ProjectId, new[] { file }, analyzeMetadata: true);

        Assert.True(ok);
        Assert.Equal(1, result!.UpdatedAssets);
        Assert.Equal(1, h.Ffprobe.ExtractCalls);
        var asset = Assert.Single(h.Media.Assets);
        Assert.Equal(MediaMetadataStatus.Ok, asset.Metadata!.MetadataStatus);
        Assert.Equal(9.0, asset.Metadata.DurationSeconds);
        Assert.Equal(new[] { "viagem" }, asset.Tags);
    }

    [Fact]
    public async Task OkAssetsAreServedFromCacheWhenFileIsUnchanged()
    {
        using var tmp = new TempDirectory();
        string root = Path.Combine(tmp.Path, "Projeto");
        using var h = new ImportHarness();
        var project = h.CreateProject(root);
        string file = TestFiles.WriteSource(Path.Combine(root, "1-Bruto", "c.mp4"), TestFiles.RandomBytes(512));

        h.Ffprobe.Available = true;
        h.Ffprobe.Responder = _ => OkInfo();
        var scan = new ScanMediaLibraryUseCase(
            h.Projects, h.Media, new MediaScannerService(), h.Ffprobe,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ScanMediaLibraryUseCase>.Instance);

        await scan.RegisterFilesAsync(project.ProjectId, new[] { file }, analyzeMetadata: true);
        var (ok, result, _, _) = await scan.RegisterFilesAsync(project.ProjectId, new[] { file }, analyzeMetadata: true);

        Assert.True(ok);
        Assert.Equal(1, result!.UnchangedAssets);
        Assert.Equal(1, h.Ffprobe.ExtractCalls);
    }

    [Fact]
    public async Task FfprobeFailureIsRecordedAsFailedWithoutInventedValues()
    {
        using var tmp = new TempDirectory();
        string root = Path.Combine(tmp.Path, "Projeto");
        using var h = new ImportHarness();
        var project = h.CreateProject(root);
        string file = TestFiles.WriteSource(Path.Combine(root, "1-Bruto", "d.mp4"), TestFiles.RandomBytes(512));

        h.Ffprobe.Available = true;
        h.Ffprobe.Responder = null; // devolve Unavailable(Failed)
        var scan = new ScanMediaLibraryUseCase(
            h.Projects, h.Media, new MediaScannerService(), h.Ffprobe,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ScanMediaLibraryUseCase>.Instance);

        var (ok, _, _, _) = await scan.RegisterFilesAsync(project.ProjectId, new[] { file }, analyzeMetadata: true);

        Assert.True(ok);
        var asset = Assert.Single(h.Media.Assets);
        Assert.Equal(MediaMetadataStatus.Failed, asset.Metadata!.MetadataStatus);
        Assert.Equal(0, asset.Metadata.DurationSeconds);
        Assert.Equal(0, asset.Metadata.Width);
    }

    [Fact]
    public void UnavailableMetadataUsesZerosAndNullsOnly()
    {
        string file = Path.Combine(Path.GetTempPath(), "tc_unavailable_" + Guid.NewGuid().ToString("N") + ".mp4");
        File.WriteAllBytes(file, new byte[4096]);
        try
        {
            var info = ExtractedMediaInfo.Unavailable(file, MediaMetadataStatus.FfprobeUnavailable);

            Assert.Equal("unknown", info.Codec);
            Assert.Equal(0, info.Width);
            Assert.Equal(0, info.Height);
            Assert.Equal(0.0, info.FrameRate);
            Assert.Equal(0.0, info.DurationSeconds);
            Assert.Equal(0L, info.DurationTicks);
            Assert.Equal(0, info.AudioChannels);
            Assert.Equal(0, info.AudioSampleRate);
            Assert.Null(info.CameraModel);
            Assert.Null(info.GpsLocation);
            Assert.Equal(0, info.RotationDegrees);
            Assert.Equal("ffprobe_unavailable", info.MetadataStatus);
            Assert.Equal("file_system_last_write_fallback", info.SourceDateEvidence);
            Assert.Equal(new RationalTimeBase(0, 0), info.TimeBase);
        }
        finally
        {
            File.Delete(file);
        }
    }
}
