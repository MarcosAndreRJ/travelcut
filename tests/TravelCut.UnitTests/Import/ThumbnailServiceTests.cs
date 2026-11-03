using Microsoft.Extensions.Logging.Abstractions;
using TravelCut.Application.Ports;
using TravelCut.Application.UseCases;
using TravelCut.Contracts.Dtos;
using TravelCut.Domain.Entities;
using TravelCut.Infrastructure.Media.Thumbnails;
using Xunit;

namespace TravelCut.Tests.Import;

public class ThumbnailServiceTests
{
    [Fact]
    public async Task WithoutFfmpegReturnsFfmpegUnavailableWithReasonAndNoImage()
    {
        using var tmp = new TempDirectory();
        var service = new FfmpegThumbnailService(NullLogger<FfmpegThumbnailService>.Instance, () => null);
        string media = TestFiles.WriteSource(Path.Combine(tmp.Path, "clip.mp4"), TestFiles.RandomBytes(256));

        var result = await service.GenerateAsync(tmp.Path, media, 1.0);

        Assert.Equal(ThumbnailStatus.FfmpegUnavailable, result.Status);
        Assert.Null(result.DataUrl);
        Assert.Contains("FFmpeg", result.Reason);
        Assert.False(Directory.Exists(Path.Combine(tmp.Path, "3-Trabalho", "cache", "thumbs")));
    }

    [Fact]
    public async Task MissingMediaFileFailsWithoutStartingProcess()
    {
        using var tmp = new TempDirectory();
        var service = new FfmpegThumbnailService(
            NullLogger<FfmpegThumbnailService>.Instance,
            () => Path.Combine(tmp.Path, "ffmpeg-inexistente.exe"));

        var result = await service.GenerateAsync(tmp.Path, Path.Combine(tmp.Path, "nao-existe.mp4"), 1.0);

        Assert.Equal(ThumbnailStatus.Failed, result.Status);
        Assert.Null(result.DataUrl);
    }

    [Fact]
    public async Task GetThumbnailForAudioIsNotSupportedAndNeverCallsService()
    {
        using var tmp = new TempDirectory();
        var projects = new InMemoryProjectRepository();
        var media = new InMemoryMediaRepository();
        var thumbs = new FakeThumbnailService();
        var project = new Project { ProjectId = Guid.NewGuid(), RootPath = tmp.Path, Name = "P" };
        projects.Projects.Add(project);
        string audio = TestFiles.WriteSource(Path.Combine(tmp.Path, "trilha.wav"), TestFiles.RandomBytes(64));

        var useCase = new GetThumbnailUseCase(projects, media, thumbs, NullLogger<GetThumbnailUseCase>.Instance);
        var (ok, response, _, _) = await useCase.ExecuteAsync(new GetThumbnailRequest(project.ProjectId, audio, null));

        Assert.True(ok);
        Assert.Equal("not_supported", response!.Status);
        Assert.Empty(thumbs.Requested);
    }

    [Fact]
    public async Task GetThumbnailUsesTenPercentOfKnownDurationAsDefaultInstant()
    {
        using var tmp = new TempDirectory();
        var projects = new InMemoryProjectRepository();
        var media = new InMemoryMediaRepository();
        var captured = new CapturingThumbnailService();
        var project = new Project { ProjectId = Guid.NewGuid(), RootPath = tmp.Path, Name = "P" };
        projects.Projects.Add(project);
        Directory.CreateDirectory(Path.Combine(tmp.Path, "1-Bruto"));
        string clip = TestFiles.WriteSource(Path.Combine(tmp.Path, "1-Bruto", "longo.mp4"), TestFiles.RandomBytes(64));
        media.Assets.Add(new MediaAsset
        {
            ProjectId = project.ProjectId,
            RelativePath = "1-Bruto/longo.mp4",
            Metadata = new MediaMetadata { MetadataStatus = MediaMetadataStatus.Ok, DurationSeconds = 50.0 }
        });

        var useCase = new GetThumbnailUseCase(projects, media, captured, NullLogger<GetThumbnailUseCase>.Instance);
        await useCase.ExecuteAsync(new GetThumbnailRequest(project.ProjectId, clip, null));

        Assert.Equal(5.0, captured.LastAtSeconds, 3);
    }

    [Fact]
    public async Task GetThumbnailFallsBackToOneSecondWhenDurationIsUnknown()
    {
        using var tmp = new TempDirectory();
        var projects = new InMemoryProjectRepository();
        var media = new InMemoryMediaRepository();
        var captured = new CapturingThumbnailService();
        var project = new Project { ProjectId = Guid.NewGuid(), RootPath = tmp.Path, Name = "P" };
        projects.Projects.Add(project);
        string clip = TestFiles.WriteSource(Path.Combine(tmp.Path, "solto.mp4"), TestFiles.RandomBytes(64));

        var useCase = new GetThumbnailUseCase(projects, media, captured, NullLogger<GetThumbnailUseCase>.Instance);
        await useCase.ExecuteAsync(new GetThumbnailRequest(project.ProjectId, clip, null));

        Assert.Equal(GetThumbnailUseCase.DefaultSeconds, captured.LastAtSeconds, 3);
    }

    private sealed class CapturingThumbnailService : IThumbnailService
    {
        public double LastAtSeconds { get; private set; } = double.NaN;

        public Task<ThumbnailResult> GenerateAsync(string projectRoot, string mediaPath, double atSeconds, CancellationToken ct = default)
        {
            LastAtSeconds = atSeconds;
            return Task.FromResult(new ThumbnailResult(ThumbnailStatus.FfmpegUnavailable, null, "fake"));
        }
    }
}
