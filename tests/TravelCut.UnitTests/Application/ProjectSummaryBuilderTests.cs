using System.Text.Json;
using TravelCut.Application.UseCases;
using TravelCut.Domain.Entities;
using TravelCut.Domain.Enums;
using Xunit;

namespace TravelCut.Tests.Application;

public class ProjectSummaryBuilderTests
{
    private static Project NewProject(string root) => new()
    {
        ProjectId = Guid.NewGuid(),
        Name = "Resumo",
        RootPath = root,
        LastModifiedAtUtc = new DateTime(2026, 5, 1, 12, 0, 0, DateTimeKind.Utc)
    };

    private static MediaAsset Asset(string status, double duration) => new()
    {
        MediaId = Guid.NewGuid(),
        RelativePath = $"1-Bruto/{Guid.NewGuid():N}.mp4",
        Kind = MediaKind.Video,
        Metadata = new MediaMetadata { MetadataStatus = status, DurationSeconds = duration }
    };

    [Fact]
    public void EstimatedCutDuration_IsNullAndNeverDerivedFromOriginalDuration()
    {
        var project = NewProject(Path.Combine(Path.GetTempPath(), "tc_summary_none_" + Guid.NewGuid().ToString("N")));
        var media = new List<MediaAsset> { Asset(MediaMetadataStatus.Ok, 100.0) };

        var summary = ProjectSummaryBuilder.Build(project, media, [], []);

        Assert.Null(summary.EstimatedCutDurationSeconds);
        Assert.Equal(100.0, summary.TotalOriginalDurationSeconds);
        Assert.Contains("\"estimatedCutDurationSeconds\":null", JsonSerializer.Serialize(summary));
    }

    [Fact]
    public void TotalDuration_SumsOnlyOkMetadata_AndCountsTheRest()
    {
        var project = NewProject(Path.Combine(Path.GetTempPath(), "tc_summary_sum_" + Guid.NewGuid().ToString("N")));
        var media = new List<MediaAsset>
        {
            Asset(MediaMetadataStatus.Ok, 10.0),
            Asset(MediaMetadataStatus.Ok, 5.5),
            Asset(MediaMetadataStatus.NotRequested, 0.0),
            Asset(MediaMetadataStatus.Failed, 999.0),
            Asset(MediaMetadataStatus.FfprobeUnavailable, 888.0)
        };

        var summary = ProjectSummaryBuilder.Build(project, media, [], []);

        Assert.Equal(5, summary.MediaCount);
        Assert.Equal(15.5, summary.TotalOriginalDurationSeconds, precision: 6);
        Assert.Equal(3, summary.MediaWithoutMetadataCount);
    }

    [Fact]
    public void MediaWithoutMetadataRecord_CountsAsWithoutMetadata()
    {
        var project = NewProject(Path.Combine(Path.GetTempPath(), "tc_summary_null_" + Guid.NewGuid().ToString("N")));
        var noMetadata = new MediaAsset { MediaId = Guid.NewGuid(), RelativePath = "1-Bruto/x.mp4", Kind = MediaKind.Video, Metadata = null };

        var summary = ProjectSummaryBuilder.Build(project, new List<MediaAsset> { noMetadata }, [], []);

        Assert.Equal(0.0, summary.TotalOriginalDurationSeconds);
        Assert.Equal(1, summary.MediaWithoutMetadataCount);
    }

    [Fact]
    public void ApprovedCuts_CountsSuggestionsWhoseEffectiveStatusIsApproved_CaseInsensitive()
    {
        // A5/M3: conta sugestões com estado efetivo Approved (não decisões brutas).
        var project = NewProject(Path.Combine(Path.GetTempPath(), "tc_summary_dec_" + Guid.NewGuid().ToString("N")));
        var s1 = Guid.NewGuid();
        var s2 = Guid.NewGuid();
        var suggestions = new List<EditSuggestion>
        {
            new() { SuggestionId = s1, Status = SuggestionStatus.Proposed },
            new() { SuggestionId = s2, Status = SuggestionStatus.Proposed },
        };
        var decisions = new List<EditDecision>
        {
            new() { DecisionId = Guid.NewGuid(), SuggestionId = s1, Action = "Approve", Version = 1 },
            new() { DecisionId = Guid.NewGuid(), SuggestionId = s2, Action = "reject", Version = 1 },
        };

        var summary = ProjectSummaryBuilder.Build(project, new List<MediaAsset>(), suggestions, decisions);

        Assert.Equal(1, summary.ApprovedCutsCount);
        Assert.Equal(2, summary.SuggestionsCount);
    }

    [Fact]
    public void ApprovedCuts_ApproveThenRejectOrUndo_DoesNotCount()
    {
        var project = NewProject(Path.Combine(Path.GetTempPath(), "tc_summary_rej_" + Guid.NewGuid().ToString("N")));
        var s = Guid.NewGuid();
        var suggestions = new List<EditSuggestion> { new() { SuggestionId = s, Status = SuggestionStatus.Proposed } };
        var rejected = new List<EditDecision>
        {
            new() { DecisionId = Guid.NewGuid(), SuggestionId = s, Action = "approve", Version = 1 },
            new() { DecisionId = Guid.NewGuid(), SuggestionId = s, Action = "reject", Version = 2 },
        };
        var undone = new List<EditDecision>
        {
            new() { DecisionId = Guid.NewGuid(), SuggestionId = s, Action = "approve", Version = 1 },
            new() { DecisionId = Guid.NewGuid(), SuggestionId = s, Action = "undo", Version = 2 },
        };

        Assert.Equal(0, ProjectSummaryBuilder.Build(project, new List<MediaAsset>(), suggestions, rejected).ApprovedCutsCount);
        Assert.Equal(0, ProjectSummaryBuilder.Build(project, new List<MediaAsset>(), suggestions, undone).ApprovedCutsCount);
    }

    [Fact]
    public void FourFoldersMissing_ReportsMissingFolderOnlyWhenInvalid()
    {
        string root = Path.Combine(Path.GetTempPath(), "tc_summary_folders_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "1-Bruto"));
        try
        {
            var summary = ProjectSummaryBuilder.Build(NewProject(root), new List<MediaAsset>(), [], []);

            Assert.False(summary.FourFoldersValid);
            Assert.Equal("2-Assets", summary.MissingFolder);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
