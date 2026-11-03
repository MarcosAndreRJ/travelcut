import type { EditPlanDto, PlanClipDto, TimelineTimeBaseDto } from '../../types/ipc';
import { clipEndFrame, sortedClips } from './planModel';

/**
 * Matemática pura da prévia de PROGRAMA (Tela 03). Sem DOM e sem IPC.
 * Frames são do timeBase do plano; tempos de fonte em segundos (sourceInUs/1e6).
 * Velocidade: speedNum/speedDen segundos de fonte por segundo de timeline.
 */

export const PROGRAM_VIDEO_TRACK_ID = 'V1';
/** Tolerância (s) para considerar que a fonte chegou ao sourceOut. */
export const SOURCE_END_EPSILON_S = 0.02;

/** Clipes de V1 em ordem cronológica. */
export const programClips = (plan: EditPlanDto): PlanClipDto[] =>
  sortedClips(plan).filter((clip) => clip.trackId === PROGRAM_VIDEO_TRACK_ID);

/** Clipe sob a agulha (início inclusivo, fim exclusivo). Nunca usa o clipe selecionado como fallback. */
export const activeClipAt = (clips: PlanClipDto[], frame: number): PlanClipDto | null =>
  clips.find((clip) => frame >= clip.timelineStartFrame && frame < clipEndFrame(clip)) ?? null;

/** Razão de velocidade: fonte por timeline. Valores inválidos viram 1 (sem inventar velocidade). */
export const clipSpeedRatio = (clip: PlanClipDto): number =>
  clip.speedNum > 0 && clip.speedDen > 0 ? clip.speedNum / clip.speedDen : 1;

export const sourceInSeconds = (clip: PlanClipDto): number => clip.sourceInUs / 1_000_000;
export const sourceOutSeconds = (clip: PlanClipDto): number => clip.sourceOutUs / 1_000_000;

/** Segundos por frame do timeBase (num/den). NaN quando inválido. */
export const secondsPerFrame = (timeBase: TimelineTimeBaseDto): number =>
  timeBase.frameDurationNum > 0 && timeBase.frameDurationDen > 0
    ? timeBase.frameDurationNum / timeBase.frameDurationDen
    : Number.NaN;

/**
 * Tempo da fonte para um frame da timeline:
 * sourceIn + (frame − timelineStart) · segundosPorFrame · velocidade, limitado a [sourceIn, sourceOut].
 */
export const sourceSecondsForFrame = (clip: PlanClipDto, frame: number, spf: number): number => {
  const inSec = sourceInSeconds(clip);
  const outSec = sourceOutSeconds(clip);
  const raw = inSec + (frame - clip.timelineStartFrame) * spf * clipSpeedRatio(clip);
  return Math.min(outSec, Math.max(inSec, raw));
};

/** Frame da timeline para um tempo da fonte dentro do clipe (inverso de sourceSecondsForFrame). */
export const frameForSourceSeconds = (clip: PlanClipDto, sourceSeconds: number, spf: number): number => {
  const ratio = clipSpeedRatio(clip);
  const raw = clip.timelineStartFrame + (sourceSeconds - sourceInSeconds(clip)) / (spf * ratio);
  const end = clipEndFrame(clip) - 1;
  return Math.min(Math.max(clip.timelineStartFrame, Math.floor(raw + 1e-6)), Math.max(clip.timelineStartFrame, end));
};

export type ClipTransition =
  | { kind: 'next'; clip: PlanClipDto }
  | { kind: 'gap'; clip: PlanClipDto }
  | { kind: 'end' };

/**
 * Após o fim de um clipe de V1: 'next' se o próximo começa exatamente no fim; 'gap' se há lacuna
 * (sem vídeo até o próximo início); 'end' se não há mais clipes de V1.
 */
export const transitionAfter = (clips: PlanClipDto[], clip: PlanClipDto): ClipTransition => {
  const end = clipEndFrame(clip);
  const next = clips.find((candidate) => candidate.timelineStartFrame >= end && candidate.clipId !== clip.clipId);
  if (!next) return { kind: 'end' };
  return next.timelineStartFrame === end ? { kind: 'next', clip: next } : { kind: 'gap', clip: next };
};

/** Primeiro clipe de V1 que começa depois do frame (usado para avançar a lacuna). */
export const nextClipStartingAfter = (clips: PlanClipDto[], frame: number): PlanClipDto | null =>
  clips.find((clip) => clip.timelineStartFrame > frame) ?? null;
