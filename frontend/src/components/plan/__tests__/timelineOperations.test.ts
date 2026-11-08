import { describe, expect, it } from 'vitest';
import type { PlanClipDto, TimelineTimeBaseDto } from '../../../types/ipc';
import {
  calculateSnapPoint,
  deleteClips,
  duplicateClips,
  moveClips,
  splitClips,
  trimClips,
} from '../timelineOperations';

const TB_30FPS: TimelineTimeBaseDto = {
  frameDurationNum: 1001,
  frameDurationDen: 30000,
  label: '29,97 fps',
};

function createPair(): PlanClipDto[] {
  const v1: PlanClipDto = {
    clipId: 'v1',
    trackId: 'V1',
    mediaId: 'm1',
    relativePath: 'video.mp4',
    sectionId: null,
    sourceInUs: 1_000_000,
    sourceOutUs: 4_000_000,
    timelineStartFrame: 0,
    durationFrames: 90,
    speedNum: 1,
    speedDen: 1,
    linkedClipId: 'a1',
    zoom: null,
    rationale: '',
    evidence: [],
    suggestionIds: [],
    confidenceBand: 'high',
    needsHumanReview: false,
  };
  const a1: PlanClipDto = {
    ...v1,
    clipId: 'a1',
    trackId: 'A1',
    linkedClipId: 'v1',
  };
  return [v1, a1];
}

describe('timelineOperations', () => {
  it('moveClips moves both V1 and A1 simultaneously and clamps to 0', () => {
    const clips = createPair();
    const moved = moveClips(clips, 'v1', 45);

    expect(moved[0].timelineStartFrame).toBe(45);
    expect(moved[1].timelineStartFrame).toBe(45);

    const clamped = moveClips(clips, 'a1', -20);
    expect(clamped[0].timelineStartFrame).toBe(0);
    expect(clamped[1].timelineStartFrame).toBe(0);
  });

  it('trimClips trims in-point and updates duration and sourceInUs for both V1 and A1', () => {
    const clips = createPair();
    const trimmed = trimClips(clips, 'v1', 'start', 30, TB_30FPS);

    expect(trimmed[0].timelineStartFrame).toBe(30);
    expect(trimmed[0].durationFrames).toBe(60);
    expect(trimmed[1].timelineStartFrame).toBe(30);
    expect(trimmed[1].durationFrames).toBe(60);
    expect(trimmed[0].sourceInUs).toBeGreaterThan(1_000_000);
  });

  it('trimClips trims out-point and updates duration for both V1 and A1', () => {
    const clips = createPair();
    const trimmed = trimClips(clips, 'v1', 'end', 120, TB_30FPS);

    expect(trimmed[0].timelineStartFrame).toBe(0);
    expect(trimmed[0].durationFrames).toBe(120);
    expect(trimmed[1].durationFrames).toBe(120);
  });

  it('splitClips splits clip and linked companion into two contiguous pairs with cross-links', () => {
    const clips = createPair();
    const split = splitClips(clips, 'v1', 45, TB_30FPS);

    expect(split.length).toBe(4);
    const vPart1 = split.find((c) => c.trackId === 'V1' && c.timelineStartFrame === 0);
    const vPart2 = split.find((c) => c.trackId === 'V1' && c.timelineStartFrame === 45);
    const aPart1 = split.find((c) => c.trackId === 'A1' && c.timelineStartFrame === 0);
    const aPart2 = split.find((c) => c.trackId === 'A1' && c.timelineStartFrame === 45);

    expect(vPart1?.durationFrames).toBe(45);
    expect(vPart2?.durationFrames).toBe(45);
    expect(aPart1?.durationFrames).toBe(45);
    expect(aPart2?.durationFrames).toBe(45);

    expect(vPart1?.linkedClipId).toBe(aPart1?.clipId);
    expect(aPart1?.linkedClipId).toBe(vPart1?.clipId);
    expect(vPart2?.linkedClipId).toBe(aPart2?.clipId);
    expect(aPart2?.linkedClipId).toBe(vPart2?.clipId);
  });

  it('duplicateClips duplicates clip and linked audio pair at timeline end', () => {
    const clips = createPair();
    const duplicated = duplicateClips(clips, 'v1');

    expect(duplicated.length).toBe(4);
    const dupV = duplicated[2];
    const dupA = duplicated[3];

    expect(dupV.trackId).toBe('V1');
    expect(dupA.trackId).toBe('A1');
    expect(dupV.timelineStartFrame).toBe(90);
    expect(dupA.timelineStartFrame).toBe(90);
    expect(dupV.linkedClipId).toBe(dupA.clipId);
    expect(dupA.linkedClipId).toBe(dupV.clipId);
  });

  it('deleteClips removes both clip and linked audio pair', () => {
    const clips = createPair();
    const remaining = deleteClips(clips, 'v1');
    expect(remaining.length).toBe(0);
  });

  it('calculateSnapPoint snaps when frame is within threshold', () => {
    const snapPoints = [0, 90, 180];
    const res1 = calculateSnapPoint(88, snapPoints, 8);
    expect(res1.snappedFrame).toBe(90);
    expect(res1.snappedTo).toBe(90);

    const res2 = calculateSnapPoint(50, snapPoints, 8);
    expect(res2.snappedFrame).toBe(50);
    expect(res2.snappedTo).toBeNull();
  });
});
