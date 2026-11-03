using TravelCut.Application.Services;
using TravelCut.Domain.Entities;
using TravelCut.Domain.Enums;
using TravelCut.Domain.ValueObjects;
using Xunit;

namespace TravelCut.UnitTests.Application;

public class ChronologyServiceTests
{
    private readonly ChronologyService _service = new();

    [Fact]
    public void BuildChronology_EmptyList_ReturnsEmptyReport()
    {
        var report = _service.BuildChronology(Enumerable.Empty<(MediaAsset, MediaMetadata)>());

        Assert.Equal(0, report.TotalClips);
        Assert.Empty(report.Sessions);
        Assert.Equal(0, report.TotalDurationSeconds);
        Assert.Null(report.EarliestUtc);
        Assert.Null(report.LatestUtc);
    }

    [Fact]
    public void BuildChronology_MultipleClipsWithinSession_GroupsIntoSingleSession()
    {
        var projectId = Guid.NewGuid();
        var t0 = new DateTime(2026, 7, 10, 10, 0, 0, DateTimeKind.Utc);

        var item1 = (
            new MediaAsset
            {
                MediaId = Guid.NewGuid(),
                ProjectId = projectId,
                RelativePath = "1-Bruto/clip01.mp4",
                Kind = MediaKind.Video
            },
            new MediaMetadata
            {
                DurationSeconds = 30.0,
                CreationTimeUtc = t0,
                SourceDateEvidence = "quicktime_creationdate_tag",
                CameraModel = "Sony FX3"
            }
        );

        var item2 = (
            new MediaAsset
            {
                MediaId = Guid.NewGuid(),
                ProjectId = projectId,
                RelativePath = "1-Bruto/clip02.mp4",
                Kind = MediaKind.Video
            },
            new MediaMetadata
            {
                DurationSeconds = 45.0,
                CreationTimeUtc = t0.AddSeconds(40), // 10s gap after clip 1
                SourceDateEvidence = "quicktime_creationdate_tag",
                CameraModel = "Sony FX3"
            }
        );

        var report = _service.BuildChronology(new[] { item1, item2 });

        Assert.Equal(2, report.TotalClips);
        Assert.Single(report.Sessions);
        Assert.Equal(75.0, report.TotalDurationSeconds);
        Assert.Equal(t0, report.EarliestUtc);
        Assert.Equal(t0.AddSeconds(85), report.LatestUtc);
        Assert.Equal(0, report.AnomaliesDetected);
    }

    [Fact]
    public void BuildChronology_ClipsSeparatedByLargeGap_SplitsIntoMultipleSessions()
    {
        var projectId = Guid.NewGuid();
        var tMorning = new DateTime(2026, 7, 10, 9, 0, 0, DateTimeKind.Utc);
        var tAfternoon = new DateTime(2026, 7, 10, 15, 0, 0, DateTimeKind.Utc); // 6 hours gap

        var morningClip = (
            new MediaAsset
            {
                MediaId = Guid.NewGuid(),
                ProjectId = projectId,
                RelativePath = "1-Bruto/morning.mp4",
                Kind = MediaKind.Video
            },
            new MediaMetadata
            {
                DurationSeconds = 60.0,
                CreationTimeUtc = tMorning,
                CameraModel = "GoPro Hero 12"
            }
        );

        var afternoonClip = (
            new MediaAsset
            {
                MediaId = Guid.NewGuid(),
                ProjectId = projectId,
                RelativePath = "1-Bruto/afternoon.mp4",
                Kind = MediaKind.Video
            },
            new MediaMetadata
            {
                DurationSeconds = 90.0,
                CreationTimeUtc = tAfternoon,
                CameraModel = "GoPro Hero 12"
            }
        );

        var report = _service.BuildChronology(new[] { morningClip, afternoonClip }, sessionGapThreshold: TimeSpan.FromMinutes(30));

        Assert.Equal(2, report.TotalClips);
        Assert.Equal(2, report.Sessions.Count);
        Assert.Equal(1, report.Sessions[0].TotalClips);
        Assert.Equal(1, report.Sessions[1].TotalClips);
    }

    [Fact]
    public void BuildChronology_OverlappingClipsFromSameCamera_FlagsAnomaly()
    {
        var projectId = Guid.NewGuid();
        var t0 = new DateTime(2026, 7, 10, 11, 0, 0, DateTimeKind.Utc);

        var clipA = (
            new MediaAsset
            {
                MediaId = Guid.NewGuid(),
                ProjectId = projectId,
                RelativePath = "1-Bruto/cam1_take1.mp4",
                Kind = MediaKind.Video
            },
            new MediaMetadata
            {
                DurationSeconds = 120.0,
                CreationTimeUtc = t0,
                CameraModel = "DJI Pocket 3"
            }
        );

        var clipB = (
            new MediaAsset
            {
                MediaId = Guid.NewGuid(),
                ProjectId = projectId,
                RelativePath = "1-Bruto/cam1_take2.mp4",
                Kind = MediaKind.Video
            },
            new MediaMetadata
            {
                DurationSeconds = 60.0,
                CreationTimeUtc = t0.AddSeconds(30), // Recorded while clipA was still rolling
                CameraModel = "DJI Pocket 3"
            }
        );

        var report = _service.BuildChronology(new[] { clipA, clipB });

        Assert.True(report.AnomaliesDetected > 0);
        Assert.Contains(report.Sessions[0].Clips, c => c.Anomaly != null && c.Anomaly.Contains("sobreposição", StringComparison.OrdinalIgnoreCase));
    }
}
