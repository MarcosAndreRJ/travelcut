using System.Globalization;
using TravelCut.Application.Ports;
using TravelCut.Contracts.Dtos;
using TravelCut.Domain.Planning;
using TravelCut.Domain.ValueObjects;

namespace TravelCut.Application.Planning;

/// <summary>Conversões entre o modelo de domínio (PlanRevision) e o contrato (EditPlanDto).</summary>
public static class PlanMapping
{
    public static EditPlanDto ToDto(PlanRevision plan, PlanSummaryDto summary, PlanValidationDto validation, string planFingerprint) => new(
            SchemaVersion: EditPlanner.SchemaVersion,
            PlanId: plan.PlanId,
            ProjectId: plan.ProjectId,
            Revision: plan.Revision,
            BaseRevision: plan.BaseRevision,
            CreatedAtUtc: plan.CreatedAtUtc,
            Author: plan.Author,
            Profile: ToProfileDto(plan.Profile),
            TimeBase: ToTimeBaseDto(plan.TimeBase),
            TimeZoneId: plan.TimeZoneId,
            MediaSetFingerprint: plan.MediaSetFingerprint,
            PlanFingerprint: planFingerprint,
            Tracks: plan.Tracks.Select(t => new PlanTrackDto(t.TrackId, t.Kind, t.Label)).ToList(),
            Sections: plan.Sections.Select(s => new PlanSectionDto(
                s.SectionId, s.Order, s.Role, s.Title, s.Rationale, s.IsHypothesis, s.StartFrame, s.EndFrame)).ToList(),
            Clips: plan.Clips.Select(c => new PlanClipDto(
                c.ClipId, c.TrackId, c.MediaId, c.RelativePath, c.SectionId, c.SourceInUs, c.SourceOutUs,
                c.TimelineStartFrame, c.DurationFrames, c.SpeedNum, c.SpeedDen, c.LinkedClipId, c.Zoom,
                c.Rationale, c.Evidence.ToList(), c.SuggestionIds.ToList(), c.ConfidenceBand, c.NeedsHumanReview,
                c.ZoomFocalPointX, c.ZoomFocalPointY, c.ZoomDurationSeconds, c.ZoomCurve)).ToList(),
            Transitions: plan.Transitions.Select(t => new PlanTransitionDto(
                t.FromClipId, t.ToClipId, t.Kind, t.DurationFrames, t.NeedsHumanReview,
                t.Origin, t.SuggestionId, t.PolicyVersion, t.Reason)).ToList(),
            AppliedCuts: plan.AppliedCuts.Select(c => new PlanAppliedCutDto(
                c.SuggestionId, c.MediaId, c.SourceInUs, c.SourceOutUs, c.Kind, c.Origin)).ToList(),
            Pending: plan.Pending.Select(p => new PlanPendingItemDto(p.SuggestionId, p.MediaId, p.Kind, p.Message, p.ClipId)).ToList(),
            Summary: summary,
            Validation: validation,
            AppliedEffects: plan.AppliedEffects?.Select(e => new PlanAppliedEffectDto(
                e.SuggestionId, e.MediaId, e.Kind, e.Origin, e.PolicyVersion, e.Reason, e.Scale, e.FocalPointX, e.FocalPointY,
                e.DurationSeconds, e.ZoomCurve)).ToList(),
            ManualEdits: plan.ManualEdits?.Select(ToDto).ToList());

    public static PlanManualEditDto ToDto(PlanManualEdit e) => new(
        e.EditId, e.Seq, e.Kind, e.MediaId, e.SourceInUs, e.SourceOutUs, e.AfterMediaId, e.AfterSourceInUs,
        e.Zoom, e.SpeedNum, e.SpeedDen, e.IntroducedInRevision);

    public static PlanManualEdit ToDomain(PlanManualEditDto e) => new(
        e.EditId, e.Seq, e.Kind, e.MediaId, e.SourceInUs, e.SourceOutUs, e.AfterMediaId, e.AfterSourceInUs,
        e.Zoom, e.SpeedNum, e.SpeedDen, e.IntroducedInRevision);

    /// <summary>Reconstrói o modelo de domínio a partir do contrato (usado por plan.validate, revert e verificação de fingerprint).</summary>
    public static PlanRevision ToDomain(EditPlanDto dto) => new(
        PlanId: dto.PlanId,
        ProjectId: dto.ProjectId,
        Revision: dto.Revision,
        BaseRevision: dto.BaseRevision,
        CreatedAtUtc: dto.CreatedAtUtc,
        Author: dto.Author,
        Profile: ToProfileSnapshot(dto.Profile),
        TimeBase: new RationalTimeBase(dto.TimeBase.FrameDurationNum, dto.TimeBase.FrameDurationDen),
        TimeZoneId: dto.TimeZoneId,
        MediaSetFingerprint: dto.MediaSetFingerprint,
        Tracks: dto.Tracks.Select(t => new PlanTrack(t.TrackId, t.Kind, t.Label)).ToList(),
        Sections: dto.Sections.Select(s => new PlanSection(
            s.SectionId, s.Order, s.Role, s.Title, s.Rationale, s.IsHypothesis, s.StartFrame, s.EndFrame)).ToList(),
        Clips: dto.Clips.Select(c => new PlanClip(
            c.ClipId, c.TrackId, c.MediaId, c.RelativePath, c.SectionId, c.SourceInUs, c.SourceOutUs,
            c.TimelineStartFrame, c.DurationFrames, c.SpeedNum, c.SpeedDen, c.LinkedClipId, c.Zoom,
            c.Rationale, c.Evidence, c.SuggestionIds, c.ConfidenceBand, c.NeedsHumanReview,
            c.ZoomFocalPointX, c.ZoomFocalPointY, c.ZoomDurationSeconds, c.ZoomCurve)).ToList(),
        Transitions: dto.Transitions.Select(t => new PlanTransition(
            t.FromClipId, t.ToClipId, t.Kind, t.DurationFrames, t.NeedsHumanReview,
            t.Origin, t.SuggestionId, t.PolicyVersion, t.Reason)).ToList(),
        AppliedCuts: dto.AppliedCuts.Select(c => new PlanAppliedCut(
            c.SuggestionId, c.MediaId, c.SourceInUs, c.SourceOutUs, c.Kind, c.Origin)).ToList(),
        Pending: dto.Pending.Select(p => new PlanPendingItem(p.SuggestionId, p.MediaId, p.Kind, p.Message, p.ClipId)).ToList(),
        AppliedEffects: dto.AppliedEffects?.Select(e => new PlanAppliedEffect(
            e.SuggestionId, e.MediaId, e.Kind, e.Origin, e.PolicyVersion, e.Reason, e.Scale, e.FocalPointX, e.FocalPointY,
            e.DurationSeconds, e.ZoomCurve)).ToList(),
        ManualEdits: dto.ManualEdits?.Select(ToDomain).ToList());

    public static PlanProfileSnapshot ToProfileSnapshot(EditorialProfileDto p) => new(
        p.ProfileId, p.Version, p.SilencePolicy, p.Pace, p.JokePreservation, p.ZoomStrength,
        p.SpeedBroll, p.MusicPolicy, p.ReviewAmbiguous, p.AutoApplyTrivialCuts);

    public static EditorialProfileDto ToProfileDto(PlanProfileSnapshot p) => new(
        ProfileId: p.ProfileId,
        Version: p.Version,
        SilencePolicy: p.SilencePolicy,
        Pace: p.Pace,
        JokePreservation: p.JokePreservation,
        ZoomStrength: p.ZoomStrength,
        SpeedBroll: p.SpeedBroll,
        MusicPolicy: p.MusicPolicy,
        ReviewAmbiguous: p.ReviewAmbiguous,
        AutoApplyTrivialCuts: p.AutoApplyTrivialCuts);

    public static TimelineTimeBaseDto ToTimeBaseDto(RationalTimeBase tb) =>
        new(tb.Numerator, tb.Denominator, TimeBaseLabel(tb));

    /// <summary>Rótulo PT-BR, ex.: "29,97 fps".</summary>
    public static string TimeBaseLabel(RationalTimeBase tb) =>
        tb.FramesPerSecond.ToString("0.###", CultureInfo.InvariantCulture).Replace('.', ',') + " fps";

    public static PlanValidationDto ToValidationDto(IReadOnlyList<PlanIssue> issues, DateTime validatedAtUtc) => new(
        IsValid: !issues.Any(i => i.Severity == PlanIssueSeverity.Error),
        Issues: issues.Select(i => new PlanIssueDto(
            i.Severity == PlanIssueSeverity.Error ? "error" : "warning", i.Code, i.Message, i.ClipId)).ToList(),
        ValidatedAtUtc: validatedAtUtc);

    public static PlanRevisionIndexEntry ToIndexEntry(EditPlanDto plan) => new(
        ProjectId: plan.ProjectId,
        PlanId: plan.PlanId,
        Revision: plan.Revision,
        BaseRevision: plan.BaseRevision,
        CreatedAtUtc: plan.CreatedAtUtc,
        Author: plan.Author,
        PlanFingerprint: plan.PlanFingerprint,
        IsValid: plan.Validation.IsValid,
        ClipCount: plan.Clips.Count,
        TimelineDurationFrames: plan.Summary.TimelineDurationFrames,
        RelativeJsonPath: PlanStoragePaths.RelativeJsonPath(plan.PlanId, plan.Revision));

    public static PlanRevisionInfoDto ToInfo(PlanRevisionIndexEntry entry) => new(
        PlanId: entry.PlanId,
        Revision: entry.Revision,
        BaseRevision: entry.BaseRevision,
        CreatedAtUtc: entry.CreatedAtUtc,
        Author: entry.Author,
        PlanFingerprint: entry.PlanFingerprint,
        IsValid: entry.IsValid,
        ClipCount: entry.ClipCount,
        TimelineDurationFrames: entry.TimelineDurationFrames);
}
