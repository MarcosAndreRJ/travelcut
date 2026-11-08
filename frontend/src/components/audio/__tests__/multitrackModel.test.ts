import { describe, it, expect } from 'vitest';
import type { EditPlanDto, PlanClipDto } from '../../../types/ipc';
import {
  dbToGain,
  gainToDb,
  isClipLinked,
  findLinkedClip,
  calculateSyncOffset,
  unlinkClip,
  canRelinkClips,
  relinkClips,
  addDynamicTrack,
  insertBrollClip,
  insertMusicClip,
  validateMultitrackExportCapabilities,
  DEFAULT_TRACK_STATES
} from '../multitrackModel';

const basePlan = (): any => ({
  schemaVersion: 'editplan/1',
  projectId: 'proj-1',
  planId: 'p1',
  revision: 1,
  baseRevision: null,
  createdAtUtc: '2026-11-07T12:00:00Z',
  author: 'test',
  planFingerprint: 'fp',
  mediaSetFingerprint: 'fp',
  timeZoneId: 'UTC',
  timeBase: { frameDurationNum: 1, frameDurationDen: 30, label: '30 fps' },
  timelineDurationFrames: 300,
  tracks: [
    { trackId: 'V1', kind: 'video', label: 'V1' },
    { trackId: 'A1', kind: 'dialogue', label: 'A1' }
  ],
  sections: [],
  clips: [],
  transitions: [],
  appliedCuts: [],
  pending: [],
  summary: {},
  validation: { isValid: true, issues: [], validatedAtUtc: '' },
  profile: {
    schemaVersion: 'editorialprofile/1',
    pace: 'normal',
    jokePreservation: 'normal',
    zoomStrength: 'subtle',
    speedBroll: 'off',
    musicPolicy: 'suggest_only'
  }
});

describe('multitrackModel', () => {
  describe('dbToGain / gainToDb', () => {
    it('converte 0 dB em ganho unitário 1.0', () => {
      expect(dbToGain(0)).toBeCloseTo(1.0, 4);
      expect(gainToDb(1.0)).toBeCloseTo(0, 2);
    });

    it('converte -6 dB em ~0.501 e -60 dB ou menos em 0 (mute)', () => {
      expect(dbToGain(-6)).toBeCloseTo(0.501, 3);
      expect(dbToGain(-60)).toBe(0);
      expect(dbToGain(-80)).toBe(0);
      expect(gainToDb(0)).toBe(-60);
    });
  });

  describe('isClipLinked & findLinkedClip', () => {
    it('detecta clips reciprocamente vinculados', () => {
      const plan = basePlan();
      expect(isClipLinked(plan.clips[0], plan)).toBe(true);
      expect(isClipLinked(plan.clips[1], plan)).toBe(true);
      expect(findLinkedClip(plan.clips[0], plan)?.clipId).toBe('a1-clip');
    });

    it('retorna false para clipes sem link ou com vínculo quebrado', () => {
      const plan = basePlan();
      const brokenClip: PlanClipDto = { ...plan.clips[0], linkedClipId: 'non-existent' };
      expect(isClipLinked(brokenClip, plan)).toBe(false);
      expect(findLinkedClip(brokenClip, plan)).toBeNull();
    });
  });

  describe('calculateSyncOffset', () => {
    it('retorna inSync: true para clipes perfeitamente alinhados', () => {
      const plan = basePlan();
      const sync = calculateSyncOffset(plan.clips[0], plan.clips[1]);
      expect(sync.inSync).toBe(true);
      expect(sync.timelineOffsetFrames).toBe(0);
      expect(sync.sourceOffsetUs).toBe(0);
    });

    it('calcula offset quando o áudio é deslocado na timeline', () => {
      const plan = basePlan();
      const slippedAudio: PlanClipDto = {
        ...plan.clips[1],
        timelineStartFrame: 15,
        sourceInUs: 500000
      };
      const sync = calculateSyncOffset(plan.clips[0], slippedAudio);
      expect(sync.inSync).toBe(false);
      expect(sync.timelineOffsetFrames).toBe(15);
      expect(sync.sourceOffsetUs).toBe(500000);
    });
  });

  describe('unlinkClip', () => {
    it('desvincula reciprocamente ambos os clipes sem alterar outros dados', () => {
      const plan = basePlan();
      const unlinked = unlinkClip(plan, 'v1-clip');
      const v1 = unlinked.clips.find((c) => c.clipId === 'v1-clip');
      const a1 = unlinked.clips.find((c) => c.clipId === 'a1-clip');
      expect(v1?.linkedClipId).toBeNull();
      expect(a1?.linkedClipId).toBeNull();
      expect(isClipLinked(v1!, unlinked)).toBe(false);
      expect(isClipLinked(a1!, unlinked)).toBe(false);
    });
  });

  describe('relinkClips', () => {
    it('revê e alinha clipe de áudio desvinculado ao clipe mestre de vídeo', () => {
      const plan = basePlan();
      const unlinked = unlinkClip(plan, 'v1-clip');
      // Desloca áudio
      const modified: EditPlanDto = {
        ...unlinked,
        clips: unlinked.clips.map((c) =>
          c.clipId === 'a1-clip' ? { ...c, timelineStartFrame: 20, durationFrames: 70 } : c
        )
      };
      const relinked = relinkClips(modified, 'v1-clip', 'a1-clip', true);
      const v1 = relinked.clips.find((c) => c.clipId === 'v1-clip')!;
      const a1 = relinked.clips.find((c) => c.clipId === 'a1-clip')!;
      expect(v1.linkedClipId).toBe('a1-clip');
      expect(a1.linkedClipId).toBe('v1-clip');
      expect(a1.timelineStartFrame).toBe(v1.timelineStartFrame);
      expect(a1.durationFrames).toBe(v1.durationFrames);
    });

    it('recusa revinculação entre mídias diferentes', () => {
      const plan = basePlan();
      const clipOtherMedia: PlanClipDto = {
        ...plan.clips[1],
        clipId: 'other-media-clip',
        mediaId: 'media-2'
      };
      const check = canRelinkClips(plan.clips[0], clipOtherMedia);
      expect(check.canRelink).toBe(false);
      expect(check.reason).toBe('Mídias diferentes');
    });
  });

  describe('addDynamicTrack', () => {
    it('adiciona faixa V2 quando apenas V1 existe', () => {
      const plan = basePlan();
      const result = addDynamicTrack(plan, 'video');
      expect(result.newTrack.trackId).toBe('V2');
      expect(result.newTrack.kind).toBe('b_roll');
      expect(result.plan.tracks.map((t) => t.trackId)).toEqual(['V1', 'A1', 'V2']);
    });

    it('adiciona faixa A2 de música quando apenas A1 existe', () => {
      const plan = basePlan();
      const result = addDynamicTrack(plan, 'audio');
      expect(result.newTrack.trackId).toBe('A2');
      expect(result.newTrack.kind).toBe('music');
      expect(result.plan.tracks.map((t) => t.trackId)).toEqual(['V1', 'A1', 'A2']);
    });
  });

  describe('insertBrollClip', () => {
    it('insere clipe na faixa V2 garantindo existência da faixa', () => {
      const plan = basePlan();
      const updated = insertBrollClip(plan, {
        mediaId: 'broll-media-1',
        relativePath: '2-Assets/broll1.mp4',
        timelineStartFrame: 30,
        durationFrames: 45,
        sourceInUs: 1000000,
        sourceOutUs: 2500000
      });
      expect(updated.tracks.some((t) => t.trackId === 'V2')).toBe(true);
      const broll = updated.clips.find((c) => c.trackId === 'V2');
      expect(broll).toBeDefined();
      expect(broll?.timelineStartFrame).toBe(30);
      expect(broll?.durationFrames).toBe(45);
      expect(broll?.relativePath).toBe('2-Assets/broll1.mp4');
    });
  });

  describe('insertMusicClip', () => {
    it('insere clipe na faixa A2 de música', () => {
      const plan = basePlan();
      const updated = insertMusicClip(plan, {
        mediaId: 'music-media-1',
        relativePath: '2-Assets/music.mp3',
        timelineStartFrame: 0,
        durationFrames: 90
      });
      expect(updated.tracks.some((t) => t.trackId === 'A2')).toBe(true);
      const music = updated.clips.find((c) => c.trackId === 'A2');
      expect(music).toBeDefined();
      expect(music?.timelineStartFrame).toBe(0);
      expect(music?.durationFrames).toBe(90);
    });
  });

  describe('validateMultitrackExportCapabilities', () => {
    it('aprova plano com V1 e A1 vinculado como 100% exportável', () => {
      const plan = basePlan();
      const report = validateMultitrackExportCapabilities(plan);
      expect(report.isFullyExportable).toBe(true);
      expect(report.supportedClipsCount).toBe(2);
      expect(report.unsupportedClipsCount).toBe(0);
      expect(report.rejectionReasons.length).toBe(0);
    });

    it('identifica e lista rejeições para B-roll V2, música A2 e A1 desvinculado', () => {
      let plan = basePlan();
      plan = unlinkClip(plan, 'v1-clip'); // A1 desvinculado
      plan = insertBrollClip(plan, {
        mediaId: 'broll-m',
        relativePath: '2-Assets/broll.mp4',
        timelineStartFrame: 10,
        durationFrames: 20,
        sourceInUs: 0,
        sourceOutUs: 666666
      });
      plan = insertMusicClip(plan, {
        mediaId: 'music-m',
        relativePath: '2-Assets/music.mp3',
        timelineStartFrame: 0,
        durationFrames: 90
      });

      const report = validateMultitrackExportCapabilities(plan);
      expect(report.isFullyExportable).toBe(false);
      expect(report.supportedClipsCount).toBe(1); // apenas V1
      expect(report.unsupportedClipsCount).toBe(3); // A1 desvinculado, V2 B-roll, A2 Música
      expect(report.rejectionReasons.some((r) => r.includes('V2'))).toBe(true);
      expect(report.rejectionReasons.some((r) => r.includes('A2'))).toBe(true);
      expect(report.rejectionReasons.some((r) => r.includes('A1'))).toBe(true);
    });
  });
});
