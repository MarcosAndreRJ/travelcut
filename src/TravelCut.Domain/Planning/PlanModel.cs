using TravelCut.Domain.ValueObjects;

namespace TravelCut.Domain.Planning;

// Modelo imutável do plano editorial (espelha PlanDtos do contrato, sem atributos JSON; Domain não referencia Contracts).
// Fonte: µs [in,out). Timeline: frames inteiros do TimeBase (segundos por frame = Numerator/Denominator).

public sealed record PlanTrack(string TrackId, string Kind, string Label);

public sealed record PlanSection(Guid SectionId, int Order, string Role, string Title, string Rationale,
    bool IsHypothesis, long StartFrame, long EndFrame);

public sealed record PlanClip(Guid ClipId, string TrackId, Guid MediaId, string RelativePath, Guid? SectionId,
    long SourceInUs, long SourceOutUs, long TimelineStartFrame, long DurationFrames, int SpeedNum, int SpeedDen,
    Guid? LinkedClipId, double? Zoom, string Rationale, IReadOnlyList<string> Evidence,
    IReadOnlyList<Guid> SuggestionIds, string ConfidenceBand, bool NeedsHumanReview);

/// <summary>
/// Transição na fronteira from → to (saída do clipe from / entrada do clipe to). Kind: cut | fade | dissolve | crossfade.
/// Campos opcionais (aditivos, null em projetos antigos e em cortes secos): Origin (auto | human), SuggestionId da sugestão
/// que a materializou, PolicyVersion/Reason (só em auto; informativos, fora do fingerprint). Origin e SuggestionId entram no
/// fingerprint apenas quando existem.
/// </summary>
public sealed record PlanTransition(Guid FromClipId, Guid ToClipId, string Kind, long DurationFrames, bool NeedsHumanReview,
    string? Origin = null, Guid? SuggestionId = null, string? PolicyVersion = null, string? Reason = null);

public sealed record PlanAppliedCut(Guid SuggestionId, Guid MediaId, long SourceInUs, long SourceOutUs, string Kind, string Origin);

/// <summary>Pendência. ClipId liga a pendência a um clipe quando aplicável (zoom_suggestion, residual_segment).</summary>
public sealed record PlanPendingItem(Guid? SuggestionId, Guid? MediaId, string Kind, string Message, Guid? ClipId = null);

/// <summary>Snapshot completo do perfil editorial usado na revisão (todos os campos entram no fingerprint).</summary>
public sealed record PlanProfileSnapshot(
    string ProfileId,
    int Version,
    string SilencePolicy,
    string Pace,
    string JokePreservation,
    string ZoomStrength,
    string SpeedBroll,
    string MusicPolicy,
    bool ReviewAmbiguous,
    bool AutoApplyTrivialCuts);

/// <summary>
/// Revisão do plano. TimeZoneId é o identificador de fuso usado para títulos e evidências (ex.: "E. South America Standard Time" ou IANA).
/// </summary>
public sealed record PlanRevision(Guid PlanId, Guid ProjectId, int Revision, int? BaseRevision, DateTime CreatedAtUtc,
    string Author, PlanProfileSnapshot Profile, RationalTimeBase TimeBase, string TimeZoneId, string MediaSetFingerprint,
    IReadOnlyList<PlanTrack> Tracks, IReadOnlyList<PlanSection> Sections, IReadOnlyList<PlanClip> Clips,
    IReadOnlyList<PlanTransition> Transitions, IReadOnlyList<PlanAppliedCut> AppliedCuts,
    IReadOnlyList<PlanPendingItem> Pending, IReadOnlyList<PlanAppliedEffect>? AppliedEffects = null);

/// <summary>F14.8: efeito aplicado (cut | zoom) com origem auto | human. Informativo: NÃO entra no fingerprint do plano.</summary>
public sealed record PlanAppliedEffect(Guid SuggestionId, Guid MediaId, string Kind, string Origin,
    string? PolicyVersion = null, string? Reason = null, double? Scale = null, double? FocalPointX = null, double? FocalPointY = null);

public sealed record MediaFacts(Guid MediaId, long DurationUs, string Fingerprint, string RelativePath);

public enum PlanIssueSeverity { Warning, Error }

public sealed record PlanIssue(PlanIssueSeverity Severity, string Code, string Message, Guid? ClipId = null);
