using System.Text.Json.Serialization;

namespace TravelCut.Contracts.Dtos;

public static class SceneClassificationConstants
{
    public const string EnvInterior = "interior";
    public const string EnvExterior = "exterior";
    public const string EnvMixed = "mixed";
    public const string EnvUnclassified = "unclassified";

    public const string InteractionDirectToCamera = "direct_to_camera";
    public const string InteractionDialogue = "dialogue";
    public const string InteractionInterview = "interview";
    public const string InteractionNarration = "narration";
    public const string InteractionBroll = "broll";
    public const string InteractionLandscape = "landscape";
    public const string InteractionOther = "other";

    public const string FramingWide = "wide";
    public const string FramingMedium = "medium";
    public const string FramingCloseUp = "close_up";
    public const string FramingUnspecified = "unspecified";

    public const string MovementStatic = "static";
    public const string MovementMoving = "moving";
    public const string MovementUnspecified = "unspecified";

    public const string ProfileNatural = "natural";
    public const string ProfileDynamic = "dynamic";
    public const string ProfileCinematic = "cinematic";
}

public sealed record EditorialSceneClassificationDto(
    [property: JsonPropertyName("entryId")] string EntryId,
    [property: JsonPropertyName("projectId")] string ProjectId,
    [property: JsonPropertyName("mediaId")] string MediaId,
    [property: JsonPropertyName("startSeconds")] double StartSeconds,
    [property: JsonPropertyName("endSeconds")] double EndSeconds,
    [property: JsonPropertyName("isFullVideo")] bool IsFullVideo,
    [property: JsonPropertyName("environment")] string Environment,
    [property: JsonPropertyName("interaction")] string Interaction,
    [property: JsonPropertyName("framing")] string Framing,
    [property: JsonPropertyName("movement")] string Movement,
    [property: JsonPropertyName("editorialProfile")] string EditorialProfile,
    [property: JsonPropertyName("notes")] string Notes,
    [property: JsonPropertyName("createdAtUtc")] string CreatedAtUtc,
    [property: JsonPropertyName("updatedAtUtc")] string UpdatedAtUtc
);

public sealed record UpsertSceneClassificationRequest(
    [property: JsonPropertyName("entryId")] string? EntryId,
    [property: JsonPropertyName("projectId")] string ProjectId,
    [property: JsonPropertyName("mediaId")] string MediaId,
    [property: JsonPropertyName("startSeconds")] double? StartSeconds,
    [property: JsonPropertyName("endSeconds")] double? EndSeconds,
    [property: JsonPropertyName("isFullVideo")] bool IsFullVideo,
    [property: JsonPropertyName("environment")] string Environment,
    [property: JsonPropertyName("interaction")] string Interaction,
    [property: JsonPropertyName("framing")] string Framing,
    [property: JsonPropertyName("movement")] string Movement,
    [property: JsonPropertyName("editorialProfile")] string EditorialProfile,
    [property: JsonPropertyName("notes")] string? Notes
);

public sealed record BatchClassifyRequest(
    [property: JsonPropertyName("projectId")] string ProjectId,
    [property: JsonPropertyName("mediaIds")] IReadOnlyList<string> MediaIds,
    [property: JsonPropertyName("environment")] string Environment,
    [property: JsonPropertyName("interaction")] string Interaction,
    [property: JsonPropertyName("framing")] string Framing,
    [property: JsonPropertyName("movement")] string Movement,
    [property: JsonPropertyName("editorialProfile")] string EditorialProfile,
    [property: JsonPropertyName("notes")] string? Notes
);

public static class SmartZoomConstants
{
    public const string TypePunchIn = "punch_in";
    public const string TypePushIn = "push_in";
}

public sealed record SmartZoomParametersDto(
    [property: JsonPropertyName("zoomType")] string ZoomType,
    [property: JsonPropertyName("scale")] double Scale,
    [property: JsonPropertyName("focalPointX")] double FocalPointX,
    [property: JsonPropertyName("focalPointY")] double FocalPointY,
    [property: JsonPropertyName("durationSeconds")] double DurationSeconds,
    [property: JsonPropertyName("startScale")] double StartScale = 1.0,
    [property: JsonPropertyName("endScale")] double EndScale = 1.08,
    // F14.8/E04 (aditivo): enquadramento classificado e se o ponto focal é conhecido (não é o centro "por padrão").
    [property: JsonPropertyName("framing")] string? Framing = null,
    [property: JsonPropertyName("focalPointKnown")] bool FocalPointKnown = false
);

public sealed record SmartZoomProfileDto(
    [property: JsonPropertyName("interaction")] string Interaction,
    [property: JsonPropertyName("minScale")] double MinScale,
    [property: JsonPropertyName("maxScale")] double MaxScale,
    [property: JsonPropertyName("defaultScale")] double DefaultScale,
    [property: JsonPropertyName("recommendedType")] string RecommendedType,
    [property: JsonPropertyName("allowZoom")] bool AllowZoom
);

public static class TransitionConstants
{
    public const string TypeCut = "cut";
    public const string TypeVideoFadeIn = "video_fade_in";
    public const string TypeVideoFadeOut = "video_fade_out";
    public const string TypeVideoDissolve = "video_dissolve";
    public const string TypeAudioFadeIn = "audio_fade_in";
    public const string TypeAudioFadeOut = "audio_fade_out";
    public const string TypeAudioCrossfade = "audio_crossfade";

    public const string PresetShort = "short";
    public const string PresetStandard = "standard";
    public const string PresetSmooth = "smooth";
    public const string PresetCustom = "custom";

    public const string StatusPending = "pending";
    public const string StatusApproved = "approved";
    public const string StatusRejected = "rejected";
    public const string StatusDeferred = "deferred";
}

public sealed record EditorialTransitionDto(
    [property: JsonPropertyName("transitionId")] string TransitionId,
    [property: JsonPropertyName("projectId")] string ProjectId,
    [property: JsonPropertyName("mediaIdA")] string MediaIdA,
    [property: JsonPropertyName("mediaIdB")] string MediaIdB,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("durationSeconds")] double DurationSeconds,
    [property: JsonPropertyName("preset")] string Preset,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("origin")] string Origin,
    [property: JsonPropertyName("rationale")] string Rationale,
    [property: JsonPropertyName("isAdjacentOnTimeline")] bool IsAdjacentOnTimeline,
    [property: JsonPropertyName("insufficientHandles")] bool InsufficientHandles,
    [property: JsonPropertyName("handleWarningMessage")] string? HandleWarningMessage,
    [property: JsonPropertyName("availableHandleFramesA")] double AvailableHandleFramesA,
    [property: JsonPropertyName("availableHandleFramesB")] double AvailableHandleFramesB,
    [property: JsonPropertyName("timelineClipIdA")] string? TimelineClipIdA,
    [property: JsonPropertyName("timelineClipIdB")] string? TimelineClipIdB,
    [property: JsonPropertyName("createdAtUtc")] string CreatedAtUtc,
    [property: JsonPropertyName("updatedAtUtc")] string UpdatedAtUtc
);

public sealed record CreateTransitionRequest(
    [property: JsonPropertyName("projectId")] string ProjectId,
    [property: JsonPropertyName("mediaIdA")] string MediaIdA,
    [property: JsonPropertyName("mediaIdB")] string MediaIdB,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("durationSeconds")] double? DurationSeconds,
    [property: JsonPropertyName("preset")] string? Preset,
    [property: JsonPropertyName("origin")] string Origin = "manual",
    [property: JsonPropertyName("rationale")] string? Rationale = null
);

public sealed record ValidateHandlesRequest(
    [property: JsonPropertyName("mediaIdA")] string MediaIdA,
    [property: JsonPropertyName("mediaIdB")] string MediaIdB,
    [property: JsonPropertyName("durationSeconds")] double DurationSeconds,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("clipEndSecondsA")] double? ClipEndSecondsA,
    [property: JsonPropertyName("clipStartSecondsB")] double? ClipStartSecondsB
);

public sealed record ValidateHandlesResultDto(
    [property: JsonPropertyName("isValid")] bool IsValid,
    [property: JsonPropertyName("hasEnoughHandles")] bool HasEnoughHandles,
    [property: JsonPropertyName("framesAAvailable")] int FramesAAvailable,
    [property: JsonPropertyName("framesBAvailable")] int FramesBAvailable,
    [property: JsonPropertyName("framesRequired")] int FramesRequired,
    [property: JsonPropertyName("warningMessage")] string? WarningMessage,
    [property: JsonPropertyName("alternativeAction")] string? AlternativeAction
);

public sealed record EffectPreviewStateDto(
    [property: JsonPropertyName("effectId")] string EffectId,
    [property: JsonPropertyName("effectType")] string EffectType,
    [property: JsonPropertyName("isSuggested")] bool IsSuggested,
    [property: JsonPropertyName("isApproved")] bool IsApproved,
    [property: JsonPropertyName("isActiveInPreview")] bool IsActiveInPreview,
    [property: JsonPropertyName("isExportSupported")] bool IsExportSupported,
    [property: JsonPropertyName("exportLimitationNote")] string? ExportLimitationNote
);
