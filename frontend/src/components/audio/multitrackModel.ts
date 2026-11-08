import type { EditPlanDto, PlanClipDto, PlanTrackDto } from '../../types/ipc';

export interface TrackAudioState {
  trackId: string;
  volumeDb: number;
  isMuted: boolean;
  isLocked: boolean;
  isSolo: boolean;
  linkedTrackId: string | null;
}

export interface SyncOffsetResult {
  timelineOffsetFrames: number;
  sourceOffsetUs: number;
  inSync: boolean;
}

export interface ExportCapabilityReport {
  isFullyExportable: boolean;
  supportedClipsCount: number;
  unsupportedClipsCount: number;
  warnings: string[];
  rejectionReasons: string[];
}

export const DEFAULT_TRACK_STATES: Record<string, TrackAudioState> = {
  V1: { trackId: 'V1', volumeDb: 0, isMuted: false, isLocked: false, isSolo: false, linkedTrackId: 'A1' },
  V2: { trackId: 'V2', volumeDb: 0, isMuted: false, isLocked: false, isSolo: false, linkedTrackId: null },
  A1: { trackId: 'A1', volumeDb: 0, isMuted: false, isLocked: false, isSolo: false, linkedTrackId: 'V1' },
  A2: { trackId: 'A2', volumeDb: -12, isMuted: false, isLocked: false, isSolo: false, linkedTrackId: null },
  A3: { trackId: 'A3', volumeDb: 0, isMuted: false, isLocked: false, isSolo: false, linkedTrackId: null }
};

export function dbToGain(db: number): number {
  if (db <= -60) return 0;
  return Math.pow(10, db / 20);
}

export function gainToDb(gain: number): number {
  if (gain <= 0.001) return -60;
  return 20 * Math.log10(gain);
}

export function isClipLinked(clip: PlanClipDto, plan: EditPlanDto): boolean {
  if (!clip.linkedClipId) return false;
  const target = plan.clips.find((c) => c.clipId === clip.linkedClipId);
  return Boolean(target && target.linkedClipId === clip.clipId);
}

export function findLinkedClip(clip: PlanClipDto, plan: EditPlanDto): PlanClipDto | null {
  if (!clip.linkedClipId) return null;
  return plan.clips.find((c) => c.clipId === clip.linkedClipId && c.linkedClipId === clip.clipId) ?? null;
}

export function calculateSyncOffset(clipA: PlanClipDto, clipB: PlanClipDto): SyncOffsetResult {
  const timelineOffsetFrames = clipB.timelineStartFrame - clipA.timelineStartFrame;
  const sourceOffsetUs = clipB.sourceInUs - clipA.sourceInUs;
  const inSync = timelineOffsetFrames === 0 && sourceOffsetUs === 0 && clipA.mediaId === clipB.mediaId;
  return { timelineOffsetFrames, sourceOffsetUs, inSync };
}

export function unlinkClip(plan: EditPlanDto, clipId: string): EditPlanDto {
  const target = plan.clips.find((c) => c.clipId === clipId);
  if (!target || !target.linkedClipId) return plan;
  const partnerId = target.linkedClipId;
  const updatedClips = plan.clips.map((c) => {
    if (c.clipId === clipId || c.clipId === partnerId) {
      return { ...c, linkedClipId: null };
    }
    return c;
  });
  return { ...plan, clips: updatedClips };
}

export function canRelinkClips(clipA: PlanClipDto, clipB: PlanClipDto): { canRelink: boolean; reason?: string } {
  if (clipA.clipId === clipB.clipId) return { canRelink: false, reason: 'Mesmo clipe' };
  if (clipA.mediaId !== clipB.mediaId) return { canRelink: false, reason: 'Mídias diferentes' };
  if (clipA.trackId === clipB.trackId) return { canRelink: false, reason: 'Mesma faixa' };
  return { canRelink: true };
}

export function relinkClips(plan: EditPlanDto, masterClipId: string, targetClipId: string, alignToMaster = true): EditPlanDto {
  const master = plan.clips.find((c) => c.clipId === masterClipId);
  const target = plan.clips.find((c) => c.clipId === targetClipId);
  if (!master || !target) return plan;
  const check = canRelinkClips(master, target);
  if (!check.canRelink) return plan;
  const updatedClips = plan.clips.map((c) => {
    if (c.clipId === masterClipId) return { ...c, linkedClipId: targetClipId };
    if (c.clipId === targetClipId) {
      if (alignToMaster) {
        return {
          ...c,
          linkedClipId: masterClipId,
          timelineStartFrame: master.timelineStartFrame,
          durationFrames: master.durationFrames,
          sourceInUs: master.sourceInUs,
          sourceOutUs: master.sourceOutUs,
          speedNum: master.speedNum,
          speedDen: master.speedDen
        };
      }
      return { ...c, linkedClipId: masterClipId };
    }
    return c;
  });
  return { ...plan, clips: updatedClips };
}

export function addDynamicTrack(plan: EditPlanDto, mediaType: 'video' | 'audio', customLabel?: string): { plan: EditPlanDto; newTrack: PlanTrackDto } {
  const prefix = mediaType === 'video' ? 'V' : 'A';
  const existingIndices = plan.tracks
    .map((t) => {
      const match = new RegExp('^' + prefix + '(\\d+)$').exec(t.trackId);
      return match ? parseInt(match[1], 10) : 0;
    })
    .filter((n) => n > 0);
  const nextIndex = existingIndices.length > 0 ? Math.max(...existingIndices) + 1 : 1;
  const newTrackId = prefix + nextIndex;
  const defaultKind = mediaType === 'video' ? (nextIndex === 2 ? 'b_roll' : 'video') : nextIndex === 2 ? 'music' : 'audio';
  const defaultLabel = customLabel || (mediaType === 'video' ? (nextIndex === 2 ? 'V2 — B-roll' : 'V' + nextIndex + ' — Vídeo sobreposto') : nextIndex === 2 ? 'A2 — Música' : nextIndex === 3 ? 'A3 — Efeitos e narração' : 'A' + nextIndex + ' — Áudio auxiliar');
  const newTrack: PlanTrackDto = { trackId: newTrackId, label: defaultLabel, kind: defaultKind };
  return { plan: { ...plan, tracks: [...plan.tracks, newTrack] }, newTrack };
}

export interface InsertBrollParams {
  mediaId: string;
  relativePath: string;
  timelineStartFrame: number;
  durationFrames: number;
  sourceInUs: number;
  sourceOutUs: number;
  targetTrackId?: string;
}

export function insertBrollClip(plan: EditPlanDto, params: InsertBrollParams): EditPlanDto {
  let updatedPlan = plan;
  const trackId = params.targetTrackId || 'V2';
  if (!updatedPlan.tracks.some((t) => t.trackId === trackId)) {
    updatedPlan = { ...updatedPlan, tracks: [...updatedPlan.tracks, { trackId, label: 'V2 — B-roll', kind: 'b_roll' }] };
  }
  const brollClipId = 'broll-' + Date.now() + '-' + Math.random().toString(36).substring(2, 7);
  const newClip: PlanClipDto = {
    clipId: brollClipId,
    trackId,
    mediaId: params.mediaId,
    relativePath: params.relativePath,
    sectionId: null,
    sourceInUs: params.sourceInUs,
    sourceOutUs: params.sourceOutUs,
    timelineStartFrame: params.timelineStartFrame,
    durationFrames: params.durationFrames,
    speedNum: 1,
    speedDen: 1,
    linkedClipId: null,
    needsHumanReview: false,
    reviewReason: null
  };
  return { ...updatedPlan, clips: [...updatedPlan.clips, newClip] };
}

export interface InsertMusicParams {
  mediaId: string;
  relativePath: string;
  timelineStartFrame: number;
  durationFrames: number;
  sourceInUs?: number;
  sourceOutUs?: number;
  targetTrackId?: string;
}

export function insertMusicClip(plan: EditPlanDto, params: InsertMusicParams): EditPlanDto {
  let updatedPlan = plan;
  const trackId = params.targetTrackId || 'A2';
  if (!updatedPlan.tracks.some((t) => t.trackId === trackId)) {
    updatedPlan = { ...updatedPlan, tracks: [...updatedPlan.tracks, { trackId, label: 'A2 — Música', kind: 'music' }] };
  }
  const musicClipId = 'music-' + Date.now() + '-' + Math.random().toString(36).substring(2, 7);
  const newClip: PlanClipDto = {
    clipId: musicClipId,
    trackId,
    mediaId: params.mediaId,
    relativePath: params.relativePath,
    sectionId: null,
    sourceInUs: params.sourceInUs ?? 0,
    sourceOutUs: params.sourceOutUs ?? Math.round((params.durationFrames / 30) * 1000000),
    timelineStartFrame: params.timelineStartFrame,
    durationFrames: params.durationFrames,
    speedNum: 1,
    speedDen: 1,
    linkedClipId: null,
    needsHumanReview: false,
    reviewReason: null
  };
  return { ...updatedPlan, clips: [...updatedPlan.clips, newClip] };
}

export function validateMultitrackExportCapabilities(plan: EditPlanDto): ExportCapabilityReport {
  let supportedClipsCount = 0;
  let unsupportedClipsCount = 0;
  const warnings: string[] = [];
  const rejectionReasons: string[] = [];
  const v1ClipIds = new Set(plan.clips.filter((c) => c.trackId === 'V1').map((c) => c.clipId));
  for (const clip of plan.clips) {
    if (clip.trackId === 'V1') {
      supportedClipsCount++;
    } else if (clip.trackId === 'A1') {
      if (clip.linkedClipId && v1ClipIds.has(clip.linkedClipId)) {
        supportedClipsCount++;
        warnings.push('Clipe A1 ' + clip.relativePath + ' vinculado a V1 será incorporado como áudio de V1 na montagem DaVinci.');
      } else {
        unsupportedClipsCount++;
        rejectionReasons.push('Clipe A1 ' + clip.relativePath + ' desvinculado não é suportado pelo montador DaVinci nesta versão.');
      }
    } else if (clip.trackId === 'V2') {
      unsupportedClipsCount++;
      rejectionReasons.push('Faixa V2 (B-roll: ' + clip.relativePath + ') não é suportada para exportação DaVinci nesta versão.');
    } else if (clip.trackId === 'A2') {
      unsupportedClipsCount++;
      rejectionReasons.push('Faixa A2 (Música: ' + clip.relativePath + ') não é suportada para exportação DaVinci nesta versão.');
    } else {
      unsupportedClipsCount++;
      rejectionReasons.push('Faixa ' + clip.trackId + ' (' + clip.relativePath + ') não é suportada para exportação DaVinci nesta versão.');
    }
  }
  if (unsupportedClipsCount > 0) {
    warnings.push('Existem ' + unsupportedClipsCount + ' clipe(s) em faixas secundárias ou desvinculadas. Eles são mantidos no projeto TravelCut, mas serão ignorados ou rejeitados na exportação ao DaVinci.');
  }
  return {
    isFullyExportable: unsupportedClipsCount === 0,
    supportedClipsCount,
    unsupportedClipsCount,
    warnings,
    rejectionReasons
  };
}
