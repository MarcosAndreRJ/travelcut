using TravelCut.Application.Editor;
using TravelCut.Application.Planning;
using TravelCut.Contracts.Dtos;

namespace TravelCut.UnitTests.Editor;

/// <summary>Constrói planos e mídias sintéticos para os testes do builder (sem I/O).</summary>
public static class EditorTestData
{
    public static readonly Guid PlanId = Guid.Parse("7a7a7a7a-0000-4000-8000-00000000abcd");
    public static readonly Guid ProjectId = Guid.Parse("6f1c2b3a-1111-4222-8333-444455556666");
    public static readonly DateTime NowUtc = new(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc);

    public static Guid ClipId(int n) => Guid.Parse($"30000000-0000-4000-8000-{n:D12}");
    public static Guid MediaId(int n) => Guid.Parse($"00000000-0000-4000-8000-{n:D12}");

    public static TimelineTimeBaseDto Timeline2997 => new(1001, 30000, "29,97 fps");
    public static TimelineTimeBaseDto Timeline25 => new(1, 25, "25 fps");

    /// <summary>Clipe do plano. Fonte em µs [in,out); timeline em frames.</summary>
    public static PlanClipDto Clip(
        int n,
        string track,
        int mediaN,
        long inUs,
        long outUs,
        long start,
        long duration,
        int speedNum = 1,
        int speedDen = 1,
        Guid? linked = null) =>
        new(
            ClipId: ClipId(n),
            TrackId: track,
            MediaId: MediaId(mediaN),
            RelativePath: $"1-Bruto/media-{mediaN}.mp4",
            SectionId: null,
            SourceInUs: inUs,
            SourceOutUs: outUs,
            TimelineStartFrame: start,
            DurationFrames: duration,
            SpeedNum: speedNum,
            SpeedDen: speedDen,
            LinkedClipId: linked,
            Zoom: null,
            Rationale: "teste",
            Evidence: [],
            SuggestionIds: [],
            ConfidenceBand: "alta",
            NeedsHumanReview: false);

    public static PlanTransitionDto Transition(int fromN, int toN, string kind) =>
        new(ClipId(fromN), ClipId(toN), kind, 0, false);

    public static EditPlanDto Plan(TimelineTimeBaseDto timeBase, PlanClipDto[] clips, PlanTransitionDto[]? transitions = null) => new(
        SchemaVersion: "editplan/1",
        PlanId: PlanId,
        ProjectId: ProjectId,
        Revision: 1,
        BaseRevision: null,
        CreatedAtUtc: NowUtc,
        Author: "planner-rules/1",
        Profile: EditorialProfiles.Default(),
        TimeBase: timeBase,
        TimeZoneId: "UTC",
        MediaSetFingerprint: "media-set",
        PlanFingerprint: "plan-fp-1",
        Tracks: [],
        Sections: [],
        Clips: clips.ToList(),
        Transitions: transitions?.ToList() ?? [],
        AppliedCuts: [],
        Pending: [],
        Summary: new PlanSummaryDto(0, 0, 0, 0, 0, 0, 0),
        Validation: new PlanValidationDto(true, [], NowUtc));

    /// <summary>Mídia do plano já resolvida (caminho absoluto fictício; o builder não acessa disco).</summary>
    public static EditorMediaSource Src(int n, long fpsNum = 30000, long fpsDen = 1001, bool vfr = false, long durationUs = 60_000_000) =>
        new(MediaId(n), $"1-Bruto/media-{n}.mp4", $"C:/projeto/1-Bruto/media-{n}.mp4", fpsNum, fpsDen, vfr, durationUs);

    public static Dictionary<Guid, EditorMediaSource> Sources(params EditorMediaSource[] sources) =>
        sources.ToDictionary(s => s.MediaId);
}
