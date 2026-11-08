import type { EditPlanDto, PlanClipDto, RationalTimeBaseDto } from '../../types/ipc';
import { dbToGain, type TrackAudioState } from './multitrackModel';

export interface ActiveAudioClipInfo {
  clip: PlanClipDto;
  trackId: string;
  sourceTimeSeconds: number;
  effectiveGain: number;
  isMuted: boolean;
}

/**
 * Encontra todos os clipes ativos por trilha no frame especificado.
 */
export function findActiveClipsByTrack(
  plan: EditPlanDto,
  currentFrame: number
): Map<string, PlanClipDto> {
  const activeByTrack = new Map<string, PlanClipDto>();
  for (const clip of plan.clips) {
    if (
      currentFrame >= clip.timelineStartFrame &&
      currentFrame < clip.timelineStartFrame + clip.durationFrames
    ) {
      // Se houver mais de um clipe na mesma trilha por erro do plano, mantém o primeiro encontrado
      if (!activeByTrack.has(clip.trackId)) {
        activeByTrack.set(clip.trackId, clip);
      }
    }
  }
  return activeByTrack;
}

/**
 * Resolve o clipe visual prioritário para exibição no frame atual.
 * Prioridade: V2 (B-roll) se ativo; caso contrário, V1 (vídeo principal); ou outra trilha de vídeo se houver.
 */
export function resolveActiveVisualClip(
  plan: EditPlanDto,
  currentFrame: number
): { activeClip: PlanClipDto | null; trackId: string | null } {
  const activeTracks = findActiveClipsByTrack(plan, currentFrame);

  // 1. Verifica B-roll (V2)
  const brollClip = activeTracks.get('V2');
  if (brollClip) {
    return { activeClip: brollClip, trackId: 'V2' };
  }

  // 2. Verifica vídeo principal (V1)
  const v1Clip = activeTracks.get('V1');
  if (v1Clip) {
    return { activeClip: v1Clip, trackId: 'V1' };
  }

  // 3. Fallback: qualquer outra trilha de vídeo cadastrada
  for (const track of plan.tracks) {
    if (track.kind === 'video' || track.kind === 'b_roll') {
      const clip = activeTracks.get(track.trackId);
      if (clip) {
        return { activeClip: clip, trackId: track.trackId };
      }
    }
  }

  return { activeClip: null, trackId: null };
}

/**
 * Calcula o tempo exato da fonte em segundos para um clipe no frame atual da timeline.
 * Respeita Source In, velocidade editorial e avanço de frames.
 */
export function calculateClipPlaybackTime(
  clip: PlanClipDto,
  currentFrame: number,
  timeBase: any
): number {
  const sourceInSec = clip.sourceInUs / 1_000_000;
  const frameOffset = Math.max(0, currentFrame - clip.timelineStartFrame);
  const fps = (timeBase.fpsNumerator ?? 30) / (timeBase.fpsDenominator ?? 1);
  const speed = (Number(clip.speedNumerator ?? clip.speedNum ?? 1) / Number(clip.speedDenominator ?? clip.speedDen ?? 1));
  // Tempo de reprodução no arquivo original
  return sourceInSec + (frameOffset / fps) * speed;
}

/**
 * Resolve todas as fontes de áudio ativas no frame atual com seus volumes e tempos.
 */
export function resolveActiveAudioClips(
  plan: EditPlanDto,
  currentFrame: number,
  timeBase: RationalTimeBaseDto,
  trackStates: Record<string, TrackAudioState> = {}
): ActiveAudioClipInfo[] {
  const activeTracks = findActiveClipsByTrack(plan, currentFrame);
  const audioClips: ActiveAudioClipInfo[] = [];

  // Considera trilhas A1 (diálogo), A2 (música), A3 (efeitos) e qualquer outra trilha de áudio
  for (const track of plan.tracks) {
    if (track.trackId.startsWith('A') || (track.kind !== 'video' && track.kind !== 'b_roll')) {
      const clip = activeTracks.get(track.trackId);
      if (clip) {
        const state = trackStates[track.trackId];
        const isTrackMuted = state?.isMuted ?? false;
        const volumeDb = state?.volumeDb ?? 0;
        const effectiveGain = isTrackMuted ? 0 : dbToGain(volumeDb);
        const sourceTimeSeconds = calculateClipPlaybackTime(clip, currentFrame, timeBase);

        audioClips.push({
          clip,
          trackId: track.trackId,
          sourceTimeSeconds,
          effectiveGain,
          isMuted: isTrackMuted
        });
      }
    }
  }

  return audioClips;
}
