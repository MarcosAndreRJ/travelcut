using System.Text.Json.Serialization;

namespace TravelCut.Contracts.Dtos;

// Contrato do plano editorial (F06). Regras (docs/07, docs/09):
// - Plano é imutável por revisão; reverter = nova revisão baseada em outra. Nada é aplicado no DaVinci aqui.
// - Tempo de FONTE em microssegundos (PTS da mídia; independe de fps, seguro para VFR).
// - Tempo de TIMELINE em frames inteiros do timebase da timeline (rational: frameDurationNum/frameDurationDen segundos por frame).
// - Velocidade racional (speedNum/speedDen > 0). Duração na timeline = round(duraçãoFonte / velocidade) em frames, política "half-even".
// - Só entram cortes APROVADOS pelo usuário e cortes triviais de alta evidência (autoApplied=true, sempre reversíveis).
// - Confiança é qualitativa; seções narrativas são hipóteses até revisão humana.
// Métodos IPC: plan.generate, plan.get, plan.list, plan.validate, plan.revert, profile.get.

public record EditorialProfileDto(
    [property: JsonPropertyName("profileId")] string ProfileId,          // ex.: "viagens"
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("silencePolicy")] string SilencePolicy,  // conservative | balanced | aggressive
    [property: JsonPropertyName("pace")] string Pace,                    // calm | normal | fast
    [property: JsonPropertyName("jokePreservation")] string JokePreservation, // strict | normal
    [property: JsonPropertyName("zoomStrength")] string ZoomStrength,    // off | subtle (1.05) | medium (1.10)
    [property: JsonPropertyName("speedBroll")] string SpeedBroll,        // off | 2x | 4x | auto
    [property: JsonPropertyName("musicPolicy")] string MusicPolicy,      // suggest_only | off
    [property: JsonPropertyName("reviewAmbiguous")] bool ReviewAmbiguous, // regra crítica: sempre true na v1
    [property: JsonPropertyName("autoApplyTrivialCuts")] bool AutoApplyTrivialCuts
);

public record TimelineTimeBaseDto(
    [property: JsonPropertyName("frameDurationNum")] long FrameDurationNum, // ex.: 1001
    [property: JsonPropertyName("frameDurationDen")] long FrameDurationDen, // ex.: 30000 → 29,97 fps
    [property: JsonPropertyName("label")] string Label                      // ex.: "29,97 fps"
);

public record PlanTrackDto(
    [property: JsonPropertyName("trackId")] string TrackId,  // V1 | A1 | A2
    [property: JsonPropertyName("kind")] string Kind,        // video | dialogue | music
    [property: JsonPropertyName("label")] string Label
);

public record PlanSectionDto(
    [property: JsonPropertyName("sectionId")] Guid SectionId,
    [property: JsonPropertyName("order")] int Order,
    [property: JsonPropertyName("role")] string Role,            // abertura | desenvolvimento | encerramento
    [property: JsonPropertyName("title")] string Title,          // derivado de evidência (ex.: "Sessão 2 — 14/10 manhã"), nunca inventado
    [property: JsonPropertyName("rationale")] string Rationale,
    [property: JsonPropertyName("isHypothesis")] bool IsHypothesis,
    [property: JsonPropertyName("startFrame")] long StartFrame,
    [property: JsonPropertyName("endFrame")] long EndFrame       // exclusivo
);

public record PlanClipDto(
    [property: JsonPropertyName("clipId")] Guid ClipId,
    [property: JsonPropertyName("trackId")] string TrackId,
    [property: JsonPropertyName("mediaId")] Guid MediaId,
    [property: JsonPropertyName("relativePath")] string RelativePath,
    [property: JsonPropertyName("sectionId")] Guid? SectionId,
    [property: JsonPropertyName("sourceInUs")] long SourceInUs,     // inclusivo
    [property: JsonPropertyName("sourceOutUs")] long SourceOutUs,   // exclusivo
    [property: JsonPropertyName("timelineStartFrame")] long TimelineStartFrame,
    [property: JsonPropertyName("durationFrames")] long DurationFrames,
    [property: JsonPropertyName("speedNum")] int SpeedNum,
    [property: JsonPropertyName("speedDen")] int SpeedDen,
    [property: JsonPropertyName("linkedClipId")] Guid? LinkedClipId, // A1 ↔ V1 da mesma tomada
    [property: JsonPropertyName("zoom")] double? Zoom,               // SEMPRE null nesta versão; zoom é só sugestão em pending "zoom_suggestion" (clipId)
    [property: JsonPropertyName("rationale")] string Rationale,
    [property: JsonPropertyName("evidence")] List<string> Evidence,
    [property: JsonPropertyName("suggestionIds")] List<Guid> SuggestionIds, // sugestões que originaram este recorte
    [property: JsonPropertyName("confidenceBand")] string ConfidenceBand,
    [property: JsonPropertyName("needsHumanReview")] bool NeedsHumanReview
);

public record PlanTransitionDto(
    [property: JsonPropertyName("fromClipId")] Guid FromClipId,
    [property: JsonPropertyName("toClipId")] Guid ToClipId,
    [property: JsonPropertyName("kind")] string Kind,          // cut | dissolve | fade
    [property: JsonPropertyName("durationFrames")] long DurationFrames,
    [property: JsonPropertyName("needsHumanReview")] bool NeedsHumanReview
);

public record PlanAppliedCutDto(
    [property: JsonPropertyName("suggestionId")] Guid SuggestionId,
    [property: JsonPropertyName("mediaId")] Guid MediaId,
    [property: JsonPropertyName("sourceInUs")] long SourceInUs,
    [property: JsonPropertyName("sourceOutUs")] long SourceOutUs,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("origin")] string Origin       // approved_by_user | auto_trivial | residual_sub_frame (trecho que arredonda para 0 quadros; suggestionId = corte/velocidade adjacente)
);

public record PlanPendingItemDto(
    [property: JsonPropertyName("suggestionId")] Guid? SuggestionId,
    [property: JsonPropertyName("mediaId")] Guid? MediaId,
    [property: JsonPropertyName("kind")] string Kind,          // suggestion_pending | music_suggestion | zoom_suggestion | speed_suggestion | residual_segment | media_excluded
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("clipId")] Guid? ClipId = null // clipe relacionado (zoom_suggestion, residual_segment); null quando não se aplica
);

public record PlanIssueDto(
    [property: JsonPropertyName("severity")] string Severity,  // error | warning
    [property: JsonPropertyName("code")] string Code,          // ex.: CLIP_OUTSIDE_MEDIA, TRACK_OVERLAP, SPEED_OVER_SPEECH, MEDIA_CHANGED, EMPTY_INTERVAL, LINK_MISMATCH, TRANSITION_OUT_OF_BOUNDS, INVALID_TRACK, PLAN_TAMPERED
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("clipId")] Guid? ClipId
);

public record PlanValidationDto(
    [property: JsonPropertyName("isValid")] bool IsValid,       // sem issues "error"
    [property: JsonPropertyName("issues")] List<PlanIssueDto> Issues,
    [property: JsonPropertyName("validatedAtUtc")] DateTime ValidatedAtUtc
);

public record PlanSummaryDto(
    [property: JsonPropertyName("sourceDurationUs")] long SourceDurationUs,      // soma das mídias incluídas (metadados ok)
    [property: JsonPropertyName("timelineDurationFrames")] long TimelineDurationFrames,
    [property: JsonPropertyName("removedSourceUs")] long RemovedSourceUs,       // só cortes efetivamente aplicados
    [property: JsonPropertyName("approvedCutsApplied")] int ApprovedCutsApplied,
    [property: JsonPropertyName("autoCutsApplied")] int AutoCutsApplied,
    [property: JsonPropertyName("pendingSuggestions")] int PendingSuggestions,
    [property: JsonPropertyName("excludedMedia")] int ExcludedMedia
);

public record EditPlanDto(
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,   // "editplan/1"
    [property: JsonPropertyName("planId")] Guid PlanId,
    [property: JsonPropertyName("projectId")] Guid ProjectId,
    [property: JsonPropertyName("revision")] int Revision,
    [property: JsonPropertyName("baseRevision")] int? BaseRevision,
    [property: JsonPropertyName("createdAtUtc")] DateTime CreatedAtUtc,
    [property: JsonPropertyName("author")] string Author,                 // "planner-rules/1" | "revert" | "user"
    [property: JsonPropertyName("profile")] EditorialProfileDto Profile,
    [property: JsonPropertyName("timeBase")] TimelineTimeBaseDto TimeBase,
    [property: JsonPropertyName("timeZoneId")] string TimeZoneId,        // fuso dos títulos/evidências (Windows ou IANA); preservado no revert
    [property: JsonPropertyName("mediaSetFingerprint")] string MediaSetFingerprint, // hash dos fingerprints das mídias usadas
    [property: JsonPropertyName("planFingerprint")] string PlanFingerprint,         // hash do JSON canônico (sem datas)
    [property: JsonPropertyName("tracks")] List<PlanTrackDto> Tracks,
    [property: JsonPropertyName("sections")] List<PlanSectionDto> Sections,
    [property: JsonPropertyName("clips")] List<PlanClipDto> Clips,
    [property: JsonPropertyName("transitions")] List<PlanTransitionDto> Transitions,
    [property: JsonPropertyName("appliedCuts")] List<PlanAppliedCutDto> AppliedCuts,
    [property: JsonPropertyName("pending")] List<PlanPendingItemDto> Pending,
    [property: JsonPropertyName("summary")] PlanSummaryDto Summary,
    [property: JsonPropertyName("validation")] PlanValidationDto Validation
);

public record PlanRevisionInfoDto(
    [property: JsonPropertyName("planId")] Guid PlanId,
    [property: JsonPropertyName("revision")] int Revision,
    [property: JsonPropertyName("baseRevision")] int? BaseRevision,
    [property: JsonPropertyName("createdAtUtc")] DateTime CreatedAtUtc,
    [property: JsonPropertyName("author")] string Author,
    [property: JsonPropertyName("planFingerprint")] string PlanFingerprint,
    [property: JsonPropertyName("isValid")] bool IsValid,
    [property: JsonPropertyName("clipCount")] int ClipCount,
    [property: JsonPropertyName("timelineDurationFrames")] long TimelineDurationFrames
);

// ---- Requests ----

public record GeneratePlanRequest(
    [property: JsonPropertyName("projectId")] Guid ProjectId,
    [property: JsonPropertyName("profile")] EditorialProfileDto? Profile,       // null = perfil padrão "viagens" v1
    [property: JsonPropertyName("timeBase")] TimelineTimeBaseDto? TimeBase,     // null = mais frequente entre as mídias incluídas; empate/sem dado → 30000/1001
    [property: JsonPropertyName("timeZoneId")] string? TimeZoneId = null        // null = TimeZoneInfo.Local.Id; id inválido → INVALID_ARGUMENT
);

public record GetPlanRequest(
    [property: JsonPropertyName("projectId")] Guid ProjectId,
    [property: JsonPropertyName("revision")] int? Revision                       // null = mais recente
);

public record RevertPlanRequest(
    [property: JsonPropertyName("projectId")] Guid ProjectId,
    [property: JsonPropertyName("toRevision")] int ToRevision
);

public record PlanListResponse(
    [property: JsonPropertyName("revisions")] List<PlanRevisionInfoDto> Revisions
);
