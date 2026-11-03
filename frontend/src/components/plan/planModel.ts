import type {
  EditPlanDto,
  PlanClipDto,
  PlanIssueDto,
  PlanSectionDto,
  PlanTransitionDto,
  PlanValidationDto
} from '../../types/ipc';

/** Derivações puras sobre EditPlanDto. Sem chamadas IPC e sem valores estimados. */

export interface TrackRow {
  trackId: string;
  label: string;
  kind: string;
  clips: PlanClipDto[];
}

export interface TransitionMarker {
  transition: PlanTransitionDto;
  toClip: PlanClipDto;
}

export const clipEndFrame = (clip: PlanClipDto): number => clip.timelineStartFrame + clip.durationFrames;

export const sortedSections = (plan: EditPlanDto): PlanSectionDto[] =>
  [...plan.sections].sort((a, b) => a.order - b.order || a.startFrame - b.startFrame);

/** Ordem cronológica; em empate de início, a ordem declarada das trilhas (não declaradas vão por último). */
export const sortedClips = (plan: EditPlanDto): PlanClipDto[] => {
  const trackOrder = new Map(plan.tracks.map((t, index) => [t.trackId, index] as const));
  const orderOf = (trackId: string): number => trackOrder.get(trackId) ?? Number.MAX_SAFE_INTEGER;
  return [...plan.clips].sort(
    (a, b) =>
      a.timelineStartFrame - b.timelineStartFrame ||
      orderOf(a.trackId) - orderOf(b.trackId) ||
      a.trackId.localeCompare(b.trackId) ||
      a.clipId.localeCompare(b.clipId)
  );
};

/** Fim da timeline: maior valor entre o resumo, clipes e seções (nunca um número fixo). */
export const timelineEndFrame = (plan: EditPlanDto): number => {
  let end = Math.max(0, plan.summary.timelineDurationFrames || 0);
  for (const clip of plan.clips) end = Math.max(end, clipEndFrame(clip));
  for (const section of plan.sections) end = Math.max(end, section.endFrame);
  return end;
};

/** Linhas de trilha: as declaradas no plano, mais trilhas usadas por clipes e não declaradas (sem descartar clipes). */
export const buildTrackRows = (plan: EditPlanDto): TrackRow[] => {
  const rows: TrackRow[] = plan.tracks.map((track) => ({
    trackId: track.trackId,
    label: track.label,
    kind: track.kind,
    clips: []
  }));
  for (const clip of sortedClips(plan)) {
    let row = rows.find((r) => r.trackId === clip.trackId);
    if (!row) {
      row = { trackId: clip.trackId, label: clip.trackId, kind: 'desconhecida', clips: [] };
      rows.push(row);
    }
    row.clips.push(clip);
  }
  return rows;
};

export const findClip = (plan: EditPlanDto, clipId: string | null | undefined): PlanClipDto | null =>
  clipId ? plan.clips.find((c) => c.clipId === clipId) ?? null : null;

export const clipsOfSection = (plan: EditPlanDto, sectionId: string): PlanClipDto[] =>
  sortedClips(plan).filter((c) => c.sectionId === sectionId);

/** Transições diferentes de corte seco, com o clipe de destino (para marcar a entrada). */
export const transitionMarkers = (plan: EditPlanDto): TransitionMarker[] => {
  const markers: TransitionMarker[] = [];
  for (const transition of plan.transitions) {
    if (transition.kind === 'cut') continue;
    const toClip = findClip(plan, transition.toClipId);
    if (toClip) markers.push({ transition, toClip });
  }
  return markers;
};

/** Clique em issue: só é navegável quando o clipe existe no plano exibido. */
export const issueTargetClipId = (plan: EditPlanDto, issue: PlanIssueDto): string | null =>
  findClip(plan, issue.clipId) ? issue.clipId : null;

/** Validação exibida: a da revalidação manual quando é da mesma revisão; senão a do próprio plano. */
export const displayedValidation = (
  plan: EditPlanDto,
  override: { revision: number; validation: PlanValidationDto } | null
): PlanValidationDto =>
  override && override.revision === plan.revision ? override.validation : plan.validation;

/** Próximo clipe na ordem cronológica (delta +1/-1). Sem clipes ou sem seleção, retorna o primeiro/último. */
export const adjacentClipId = (plan: EditPlanDto, currentClipId: string | null, delta: 1 | -1): string | null => {
  const clips = sortedClips(plan);
  if (clips.length === 0) return null;
  const index = clips.findIndex((c) => c.clipId === currentClipId);
  if (index < 0) return delta === 1 ? clips[0].clipId : clips[clips.length - 1].clipId;
  const next = Math.min(clips.length - 1, Math.max(0, index + delta));
  return clips[next].clipId;
};

/**
 * Intervalo de régua (segundos): o menor que mantém rótulos a pelo menos ~96 px
 * e não gera mais de 300 marcas para a duração informada.
 */
const RULER_STEPS_SECONDS = [1, 2, 5, 10, 15, 30, 60, 120, 300, 600, 900, 1800, 3600];
const RULER_MAX_TICKS = 300;
export const rulerStepSeconds = (pxPerSecond: number, totalSeconds: number): number => {
  const largest = RULER_STEPS_SECONDS[RULER_STEPS_SECONDS.length - 1];
  if (!(pxPerSecond > 0)) return largest;
  return (
    RULER_STEPS_SECONDS.find(
      (step) => step * pxPerSecond >= 96 && (!(totalSeconds > 0) || totalSeconds / step <= RULER_MAX_TICKS)
    ) ?? largest
  );
};

export const countReviewFlags = (plan: EditPlanDto): number => plan.clips.filter((c) => c.needsHumanReview).length;

export const countErrors = (validation: PlanValidationDto): number =>
  validation.issues.filter((i) => i.severity === 'error').length;

/** Código de issue: arquivo de mídia do clipe ausente em disco (validação do host). */
export const SOURCE_FILE_MISSING_CODE = 'SOURCE_FILE_MISSING';
/** Código de issue: plano gerado com entradas diferentes das atuais (decisões, mídias ou perfil). */
export const PLAN_OUTDATED_CODE = 'PLAN_OUTDATED';

/** Issues PLAN_OUTDATED da validação exibida. */
export const hasPlanOutdated = (validation: PlanValidationDto | null): boolean =>
  !!validation && validation.issues.some((issue) => issue.code === PLAN_OUTDATED_CODE);

/** Clipes com SOURCE_FILE_MISSING, vinculados pelo clipId que o host envia na issue. Issues sem clipId válido são ignoradas aqui. */
export const clipsWithMissingSource = (plan: EditPlanDto, validation: PlanValidationDto | null): Set<string> => {
  const ids = new Set<string>();
  if (!validation) return ids;
  for (const issue of validation.issues) {
    if (issue.code !== SOURCE_FILE_MISSING_CODE) continue;
    if (issue.clipId && findClip(plan, issue.clipId)) ids.add(issue.clipId);
  }
  return ids;
};
