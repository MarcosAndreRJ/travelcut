using TravelCut.Application.Planning;
using TravelCut.Contracts.Dtos;
using TravelCut.Domain.Entities;
using TravelCut.Domain.Enums;
using TravelCut.Domain.ValueObjects;
using Xunit;

namespace TravelCut.UnitTests.Planning;

/// <summary>Invariantes de tempo do planejador (B1: nada se perde entre fonte, cortes e clipes) e fuso de exibição (M7).</summary>
public class EditPlannerInvariantTests
{
    private static Guid M(int n) => TravelFixture.M(n);
    private static Guid S(int n) => TravelFixture.S(n);

    /// <summary>Soma em µs da fonte que continua nos clipes: V1 e A1 sem vínculo (o A1 vinculado repete o V1).</summary>
    private static long KeptSourceUs(EditPlanDto plan) =>
        plan.Clips
            .Where(c => c.TrackId == "V1" || (c.TrackId == "A1" && c.LinkedClipId is null))
            .Sum(c => c.SourceOutUs - c.SourceInUs);

    [Fact]
    public void SourceTime_IsFullyAccounted_InFixture()
    {
        // |sourceDurationUs − removedSourceUs − Σ(clipes V1 + A1 sem vínculo)| == 0, em µs da fonte (sem velocidade).
        var plan = TravelFixture.BuildPlan(TravelFixture.Input());

        long diff = plan.Summary.SourceDurationUs - plan.Summary.RemovedSourceUs - KeptSourceUs(plan);
        Assert.Equal(0, diff);
        Assert.Equal(19_800_000, plan.Summary.RemovedSourceUs);
    }

    [Fact]
    public void SubFrameGap_IsRemovedAndRecorded_AndInvariantHolds()
    {
        // Duas tomadas aprovadas com 10 ms de intervalo: esse trecho (0,3 quadro) arredonda para 0 quadro na sequência.
        // Esperado: sai da timeline, entra em appliedCuts com origin residual_sub_frame, e a conta fecha.
        var media = Video(20, seconds: 10.0);
        var suggestions = new[]
        {
            Suggestion(101, 20, 1000, 2000),   // corte A: 1,00–2,00 s
            Suggestion(102, 20, 2010, 3000),   // corte B: 2,01–3,00 s (lacuna de 10 ms em 2,00–2,01)
        };
        var decisions = new[] { Approve(101), Approve(102) };

        var input = TravelFixture.Input(
            media: [media],
            suggestions: suggestions,
            decisions: decisions,
            analyses: new Dictionary<Guid, MediaAnalysisDto>(),
            timeBase: new TimelineTimeBaseDto(1, 30, "30 fps"));
        var plan = TravelFixture.BuildPlan(input);

        var v1 = plan.Clips.Where(c => c.TrackId == "V1").OrderBy(c => c.TimelineStartFrame).ToList();
        Assert.Equal(2, v1.Count);
        Assert.Equal((0L, 1_000_000L, 30L), (v1[0].SourceInUs, v1[0].SourceOutUs, v1[0].DurationFrames));
        Assert.Equal((3_000_000L, 10_000_000L, 210L), (v1[1].SourceInUs, v1[1].SourceOutUs, v1[1].DurationFrames));
        Assert.Equal(30, v1[1].TimelineStartFrame);
        Assert.All(plan.Clips, c => Assert.True(c.DurationFrames >= 1));

        var residual = Assert.Single(plan.AppliedCuts, c => c.Origin == EditPlanner.OriginResidualSubFrame);
        Assert.Equal(S(101), residual.SuggestionId);
        Assert.Equal((2_000_000L, 2_010_000L), (residual.SourceInUs, residual.SourceOutUs));

        Assert.Equal(2_000_000, plan.Summary.RemovedSourceUs);
        Assert.Equal(2, plan.Summary.ApprovedCutsApplied);
        Assert.Equal(0, plan.Summary.AutoCutsApplied);
        Assert.Equal(10_000_000, plan.Summary.SourceDurationUs);
        Assert.Equal(0, plan.Summary.SourceDurationUs - plan.Summary.RemovedSourceUs - KeptSourceUs(plan));
        Assert.True(plan.Validation.IsValid);
    }

    [Fact]
    public void TimeZone_IsUsedForTitles_EvidenceAndIsPreserved()
    {
        // Fuso fixo UTC−3 (custom, independe do tzdata da máquina): 09:00 UTC vira 06:00.
        var zone = TimeZoneInfo.CreateCustomTimeZone("UTC-3", TimeSpan.FromHours(-3), "UTC-3", "UTC-3");
        var input = TravelFixture.Input() with { DisplayZone = zone, TimeZoneId = "UTC-3" };

        var plan = TravelFixture.BuildPlan(input);

        Assert.Equal("UTC-3", plan.TimeZoneId);
        Assert.Equal("Sessão 1 — 14/10 06:00", plan.Sections[0].Title);
        Assert.Contains("Cronologia: 14/10 06:00 (creation_time)", plan.Clips.First(c => c.MediaId == M(1)).Evidence);
    }

    [Fact]
    public void TimeZone_ChangesFingerprint_OnlyThroughTimeZoneField()
    {
        var utc = TravelFixture.BuildPlan(TravelFixture.Input());
        var zone = TimeZoneInfo.CreateCustomTimeZone("UTC-3", TimeSpan.FromHours(-3), "UTC-3", "UTC-3");
        var other = TravelFixture.BuildPlan(TravelFixture.Input() with { DisplayZone = zone, TimeZoneId = "UTC-3" });

        Assert.NotEqual(utc.PlanFingerprint, other.PlanFingerprint);
    }

    // ------------------------------------------------------------------ Auxiliares

    private static MediaAsset Video(int n, double seconds) => new()
    {
        MediaId = M(n),
        ProjectId = TravelFixture.ProjectId,
        RelativePath = $"dia1/sub-{n:D2}.mp4",
        ByteLength = 1000,
        LastWriteUtc = new DateTime(2026, 10, 14, 9, 1, 0, DateTimeKind.Utc),
        Fingerprint = $"fp-{n}",
        Kind = MediaKind.Video,
        Metadata = new MediaMetadata
        {
            MediaId = M(n),
            Codec = "h264",
            Width = 1920,
            Height = 1080,
            FrameRate = 30.0,
            DurationSeconds = seconds,
            DurationTicks = (long)Math.Round(seconds * 1000),
            AudioChannels = 0,
            AudioSampleRate = 0,
            CreationTimeUtc = new DateTime(2026, 10, 14, 9, 0, 0, DateTimeKind.Utc),
            SourceDateEvidence = "creation_time",
            MetadataStatus = MediaMetadataStatus.Ok,
        },
    };

    /// <summary>Intervalo em milissegundos (ticks de SuggestionStorageMapper).</summary>
    private static EditSuggestion Suggestion(int n, int mediaN, long startMs, long endMs) => new()
    {
        SuggestionId = S(n),
        MediaId = M(mediaN),
        SourceInterval = new TimeRange(startMs, endMs),
        EvidenceRefs = "{\"kind\":\"silence\"}",
        ConfidenceBand = "high",
        NeedsHumanReview = false,
        SuggestedAction = "cut",
        Status = SuggestionStatus.Proposed,
        Author = "test",
        CreatedAtUtc = new DateTime(2026, 10, 14, 9, 0, 0, DateTimeKind.Utc).AddSeconds(n),
    };

    private static EditDecision Approve(int suggestionN) => new()
    {
        DecisionId = TravelFixture.D(200 + suggestionN),
        SuggestionId = S(suggestionN),
        Action = "approve",
        Reviewer = "user",
        TimestampUtc = new DateTime(2026, 10, 14, 10, 0, 0, DateTimeKind.Utc),
    };
}
