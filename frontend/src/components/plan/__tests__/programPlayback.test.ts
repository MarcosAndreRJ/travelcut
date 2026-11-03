import { describe, expect, it } from 'vitest';
import type { EditPlanDto, PlanClipDto, TimelineTimeBaseDto } from '../../../types/ipc';
import {
  activeClipAt,
  clipSpeedRatio,
  frameForSourceSeconds,
  nextClipStartingAfter,
  programClips,
  secondsPerFrame,
  sourceInSeconds,
  sourceOutSeconds,
  sourceSecondsForFrame,
  transitionAfter
} from '../programPlayback';

const TB_2997: TimelineTimeBaseDto = { frameDurationNum: 1001, frameDurationDen: 30000, label: '29,97 fps' };
const SPF_2997 = 1001 / 30000;

const clip = (over: Partial<PlanClipDto> & Pick<PlanClipDto, 'clipId' | 'timelineStartFrame' | 'durationFrames'>): PlanClipDto => ({
  trackId: 'V1',
  mediaId: 'med-1',
  relativePath: 'DEMO_CLIP_0001.MP4',
  sectionId: null,
  sourceInUs: 10_000_000,
  sourceOutUs: 100_000_000,
  speedNum: 1,
  speedDen: 1,
  linkedClipId: null,
  zoom: null,
  rationale: '',
  evidence: [],
  suggestionIds: [],
  confidenceBand: 'high',
  needsHumanReview: false,
  ...over
});

const planWith = (clips: PlanClipDto[]): EditPlanDto => ({
  schemaVersion: '1',
  planId: 'plan-1',
  projectId: 'proj-1',
  revision: 1,
  baseRevision: null,
  createdAtUtc: '2026-11-02T00:00:00Z',
  author: 'DEMO',
  profile: {
    profileId: 'demo',
    version: 1,
    silencePolicy: 'balanced',
    pace: 'normal',
    jokePreservation: 'strict',
    zoomStrength: 'subtle',
    speedBroll: '2x',
    musicPolicy: 'suggest_only',
    reviewAmbiguous: true,
    autoApplyTrivialCuts: false
  },
  timeBase: TB_2997,
  mediaSetFingerprint: 'f',
  planFingerprint: 'f',
  timeZoneId: 'UTC',
  tracks: [
    { trackId: 'V1', kind: 'video', label: 'Vídeo' },
    { trackId: 'A1', kind: 'dialogue', label: 'Diálogo' }
  ],
  sections: [],
  clips,
  transitions: [],
  appliedCuts: [],
  pending: [],
  summary: {
    sourceDurationUs: 0,
    timelineDurationFrames: 0,
    removedSourceUs: 0,
    approvedCutsApplied: 0,
    autoCutsApplied: 0,
    pendingSuggestions: 0,
    excludedMedia: 0
  },
  validation: { isValid: true, issues: [], validatedAtUtc: '2026-11-02T00:00:00Z' }
});

// Trilha V1 contígua (0..300, 300..600), lacuna (600..700) e outro clipe (700..900).
const c1 = clip({ clipId: 'c1', timelineStartFrame: 0, durationFrames: 300 });
const c2 = clip({ clipId: 'c2', timelineStartFrame: 300, durationFrames: 300, sourceInUs: 0 });
const c3 = clip({ clipId: 'c3', timelineStartFrame: 700, durationFrames: 200, sourceInUs: 50_000_000, sourceOutUs: 60_000_000 });
const PROGRAM = [c1, c2, c3];

describe('programPlayback: clipe sob a agulha', () => {
  it('início é inclusivo e fim é exclusivo', () => {
    expect(activeClipAt(PROGRAM, 0)?.clipId).toBe('c1');
    expect(activeClipAt(PROGRAM, 299)?.clipId).toBe('c1');
    expect(activeClipAt(PROGRAM, 300)?.clipId).toBe('c2');
    expect(activeClipAt(PROGRAM, 599)?.clipId).toBe('c2');
  });

  it('lacunas e depois do último clipe não têm clipe ativo', () => {
    expect(activeClipAt(PROGRAM, 600)).toBeNull();
    expect(activeClipAt(PROGRAM, 699)).toBeNull();
    expect(activeClipAt(PROGRAM, 700)?.clipId).toBe('c3');
    expect(activeClipAt(PROGRAM, 899)?.clipId).toBe('c3');
    expect(activeClipAt(PROGRAM, 900)).toBeNull();
  });

  it('sem clipes, não há clipe ativo', () => {
    expect(activeClipAt([], 0)).toBeNull();
  });

  it('programClips considera somente a trilha V1 em ordem cronológica', () => {
    const audio = clip({ clipId: 'a1', trackId: 'A1', timelineStartFrame: 10, durationFrames: 50 });
    const plan = planWith([c2, audio, c1]);
    expect(programClips(plan).map((c) => c.clipId)).toEqual(['c1', 'c2']);
  });
});

describe('programPlayback: velocidade e razão', () => {
  it('razão é speedNum/speedDen', () => {
    expect(clipSpeedRatio(clip({ clipId: 'x', timelineStartFrame: 0, durationFrames: 1, speedNum: 2, speedDen: 1 }))).toBe(2);
    expect(clipSpeedRatio(clip({ clipId: 'x', timelineStartFrame: 0, durationFrames: 1, speedNum: 1001, speedDen: 1000 }))).toBeCloseTo(1.001, 12);
  });

  it('valores inválidos viram 1 (sem inventar velocidade)', () => {
    expect(clipSpeedRatio(clip({ clipId: 'x', timelineStartFrame: 0, durationFrames: 1, speedNum: 0, speedDen: 1 }))).toBe(1);
    expect(clipSpeedRatio(clip({ clipId: 'x', timelineStartFrame: 0, durationFrames: 1, speedNum: 2, speedDen: 0 }))).toBe(1);
  });

  it('segundos por frame vêm do timeBase 1001/30000', () => {
    expect(secondsPerFrame(TB_2997)).toBeCloseTo(SPF_2997, 12);
    expect(secondsPerFrame({ frameDurationNum: 0, frameDurationDen: 30, label: 'x' })).toBeNaN();
  });

  it('fonte in/out em segundos a partir de microssegundos', () => {
    expect(sourceInSeconds(c1)).toBe(10);
    expect(sourceOutSeconds(c1)).toBe(100);
  });
});

describe('programPlayback: fonte ↔ frame', () => {
  it('velocidade 1×: 30 frames a partir do início do clipe equivalem a 1,001 s de fonte (29,97)', () => {
    expect(sourceSecondsForFrame(c1, 0, SPF_2997)).toBeCloseTo(10, 9);
    // c2 começa na fonte 0 s; o frame 330 é 30 frames após o início.
    expect(sourceSecondsForFrame(c2, 330, SPF_2997)).toBeCloseTo(30 * SPF_2997, 9);
  });

  it('velocidade 2×: 30 frames de timeline avançam 2,002 s de fonte', () => {
    const fast = clip({ clipId: 'f', timelineStartFrame: 100, durationFrames: 300, speedNum: 2, speedDen: 1 });
    expect(sourceSecondsForFrame(fast, 100, SPF_2997)).toBeCloseTo(10, 9);
    expect(sourceSecondsForFrame(fast, 130, SPF_2997)).toBeCloseTo(12.002, 9);
    expect(frameForSourceSeconds(fast, 12.002, SPF_2997)).toBe(130);
  });

  it('razão 1001/1000 com 29,97: 1000 frames avançam 1,001 × 33,3667 s', () => {
    const odd = clip({
      clipId: 'o',
      timelineStartFrame: 0,
      durationFrames: 2000,
      speedNum: 1001,
      speedDen: 1000,
      sourceInUs: 10_000_000,
      sourceOutUs: 500_000_000
    });
    expect(sourceSecondsForFrame(odd, 1000, SPF_2997)).toBeCloseTo(10 + (1000 * 1001) / 30000 * 1.001, 6);
  });

  it('a fonte é limitada entre sourceIn e sourceOut', () => {
    expect(sourceSecondsForFrame(c3, 0, SPF_2997)).toBe(50);
    expect(sourceSecondsForFrame(c3, 10_000, SPF_2997)).toBe(60);
  });

  it('ida e volta fonte → frame → fonte preserva o frame (1×, 2× e 1001/1000)', () => {
    const speeds: [number, number][] = [
      [1, 1],
      [2, 1],
      [1001, 1000]
    ];
    for (const [num, den] of speeds) {
      const sc = clip({ clipId: `s${num}-${den}`, timelineStartFrame: 40, durationFrames: 500, speedNum: num, speedDen: den, sourceInUs: 0, sourceOutUs: 1_000_000_000 });
      for (const offset of [0, 1, 29, 30, 137, 299]) {
        const frame = sc.timelineStartFrame + offset;
        const src = sourceSecondsForFrame(sc, frame, SPF_2997);
        expect(frameForSourceSeconds(sc, src, SPF_2997)).toBe(frame);
      }
    }
  });

  it('fonte antes do início do clipe prende no primeiro frame; no fim, no último frame do clipe', () => {
    expect(frameForSourceSeconds(c3, 0, SPF_2997)).toBe(700);
    expect(frameForSourceSeconds(c3, 999, SPF_2997)).toBe(899);
  });
});

describe('programPlayback: transições, lacunas e fim', () => {
  it('próximo clipe encostado é "next"', () => {
    expect(transitionAfter(PROGRAM, c1)).toEqual({ kind: 'next', clip: c2 });
  });

  it('próximo clipe com lacuna é "gap" (sem vídeo até o início)', () => {
    expect(transitionAfter(PROGRAM, c2)).toEqual({ kind: 'gap', clip: c3 });
  });

  it('último clipe leva a "end"', () => {
    expect(transitionAfter(PROGRAM, c3)).toEqual({ kind: 'end' });
  });

  it('nextClipStartingAfter avança a lacuna pelo primeiro início posterior ao frame', () => {
    expect(nextClipStartingAfter(PROGRAM, 600)?.clipId).toBe('c3');
    expect(nextClipStartingAfter(PROGRAM, 699)?.clipId).toBe('c3');
    expect(nextClipStartingAfter(PROGRAM, 700)).toBeNull();
    expect(nextClipStartingAfter(PROGRAM, -1)?.clipId).toBe('c1');
  });
});
