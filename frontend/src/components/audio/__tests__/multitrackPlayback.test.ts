import { describe, it, expect } from 'vitest';
import type { EditPlanDto, PlanClipDto, RationalTimeBaseDto } from '../../../types/ipc';
import {
  findActiveClipsByTrack,
  resolveActiveVisualClip,
  calculateClipPlaybackTime,
  resolveActiveAudioClips
} from '../multitrackPlayback';

const tb2997: RationalTimeBaseDto = {
  fpsNumerator: 30000,
  fpsDenominator: 1001,
  dropFrame: true
};

const makeClip = (
  clipId: string,
  trackId: string,
  startFrame: number,
  durationFrames: number,
  sourceInUs: number = 0,
  speedNum: number = 1,
  speedDen: number = 1
): PlanClipDto => ({
  clipId,
  trackId,
  mediaId: `media-${trackId}`,
  relativePath: `media/${trackId}.mp4`,
  sectionId: null,
  sourceInUs,
  sourceOutUs: sourceInUs + 10_000_000,
  timelineStartFrame: startFrame,
  durationFrames,
  speedNumerator: speedNum,
  speedDenominator: speedDen,
  needsHumanReview: false,
  reviewReason: null,
  linkedClipId: null
});

const makePlan = (clips: PlanClipDto[]): EditPlanDto => ({
  schemaVersion: 'editplan/1',
  projectId: 'p1',
  timeBase: tb2997,
  tracks: [
    { trackId: 'V1', kind: 'video', label: 'V1 — Vídeo' },
    { trackId: 'V2', kind: 'b_roll', label: 'V2 — B-roll' },
    { trackId: 'A1', kind: 'dialogue', label: 'A1 — Diálogo' },
    { trackId: 'A2', kind: 'music', label: 'A2 — Música' }
  ],
  sections: [],
  clips,
  transitions: [],
  appliedCuts: [],
  pendingItems: [],
  revision: 1,
  baseRevision: null,
  createdAtUtc: '2026-11-07T00:00:00Z',
  author: 'system',
  planFingerprint: 'fp',
  isValid: true,
  clipCount: clips.length,
  timelineDurationFrames: 500
});

describe('multitrackPlayback', () => {
  describe('findActiveClipsByTrack', () => {
    it('encontra clipes corretos ativos em cada trilha', () => {
      const v1 = makeClip('v1', 'V1', 0, 100);
      const v2 = makeClip('v2', 'V2', 50, 60);
      const a1 = makeClip('a1', 'A1', 0, 100);
      const a2 = makeClip('a2', 'A2', 20, 80);
      const plan = makePlan([v1, v2, a1, a2]);

      const atFrame10 = findActiveClipsByTrack(plan, 10);
      expect(atFrame10.get('V1')?.clipId).toBe('v1');
      expect(atFrame10.get('A1')?.clipId).toBe('a1');
      expect(atFrame10.has('V2')).toBe(false);
      expect(atFrame10.has('A2')).toBe(false);

      const atFrame60 = findActiveClipsByTrack(plan, 60);
      expect(atFrame60.get('V1')?.clipId).toBe('v1');
      expect(atFrame60.get('V2')?.clipId).toBe('v2');
      expect(atFrame60.get('A1')?.clipId).toBe('a1');
      expect(atFrame60.get('A2')?.clipId).toBe('a2');
    });
  });

  describe('resolveActiveVisualClip', () => {
    it('prioriza V2 (B-roll) sobre V1 quando ambos estão ativos no frame', () => {
      const v1 = makeClip('v1', 'V1', 0, 200);
      const broll = makeClip('v2', 'V2', 50, 50);
      const plan = makePlan([v1, broll]);

      // Frame 20: apenas V1 ativo
      const res20 = resolveActiveVisualClip(plan, 20);
      expect(res20.trackId).toBe('V1');
      expect(res20.activeClip?.clipId).toBe('v1');

      // Frame 60: V2 e V1 ativos, V2 deve ter prioridade visual!
      const res60 = resolveActiveVisualClip(plan, 60);
      expect(res60.trackId).toBe('V2');
      expect(res60.activeClip?.clipId).toBe('v2');

      // Frame 120: V2 terminou, volta para V1
      const res120 = resolveActiveVisualClip(plan, 120);
      expect(res120.trackId).toBe('V1');
      expect(res120.activeClip?.clipId).toBe('v1');
    });

    it('retorna null quando nenhum clipe de vídeo está ativo no frame', () => {
      const v1 = makeClip('v1', 'V1', 0, 50);
      const plan = makePlan([v1]);

      const res = resolveActiveVisualClip(plan, 100);
      expect(res.trackId).toBeNull();
      expect(res.activeClip).toBeNull();
    });
  });

  describe('calculateClipPlaybackTime', () => {
    it('calcula o tempo em segundos na fonte considerando sourceIn e velocidade 1x', () => {
      // Início na fonte: 5s (5_000_000 us). Timeline start: frame 30.
      const clip = makeClip('c1', 'V1', 30, 90, 5_000_000, 1, 1);
      // No frame 30, deve ser exatamente 5.0 s
      const timeAtStart = calculateClipPlaybackTime(clip, 30, tb2997);
      expect(timeAtStart).toBeCloseTo(5.0, 3);

      // No frame 60 (30 frames depois, a ~29.97 fps = ~1.001 s)
      const timeAt30f = calculateClipPlaybackTime(clip, 60, tb2997);
      expect(timeAt30f).toBeCloseTo(5.0 + 30 / (30000 / 1001), 3);
    });

    it('ajusta o tempo da fonte com velocidade 2x', () => {
      const clip = makeClip('c1', 'V1', 0, 60, 2_000_000, 2, 1);
      // No frame 30 (1 segundo de timeline), avança 2 segundos de mídia na fonte
      const time = calculateClipPlaybackTime(clip, 30, tb2997);
      const expectedAdvance = (30 / (30000 / 1001)) * 2;
      expect(time).toBeCloseTo(2.0 + expectedAdvance, 3);
    });
  });

  describe('resolveActiveAudioClips', () => {
    it('resolve fontes de áudio com ganho e mute aplicados', () => {
      const a1 = makeClip('a1', 'A1', 0, 100);
      const a2 = makeClip('a2', 'A2', 0, 100);
      const plan = makePlan([a1, a2]);

      const trackStates = {
        A1: {
          trackId: 'A1',
          volumeDb: 0,
          isMuted: false,
          isLocked: false,
          isSolo: false,
          linkedTrackId: 'V1'
        },
        A2: {
          trackId: 'A2',
          volumeDb: -6,
          isMuted: true,
          isLocked: false,
          isSolo: false,
          linkedTrackId: null
        }
      };

      const activeAudio = resolveActiveAudioClips(plan, 50, tb2997, trackStates);
      expect(activeAudio.length).toBe(2);

      const a1Info = activeAudio.find((a) => a.trackId === 'A1');
      expect(a1Info?.effectiveGain).toBe(1);
      expect(a1Info?.isMuted).toBe(false);

      const a2Info = activeAudio.find((a) => a.trackId === 'A2');
      expect(a2Info?.effectiveGain).toBe(0); // mutado tem ganho 0
      expect(a2Info?.isMuted).toBe(true);
    });
  });
});
