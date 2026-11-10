import type { EditPlanDto, PlanClipDto, TimelineTimeBaseDto } from '../../types/ipc';

export interface SnapResult {
  snappedFrame: number;
  snappedTo: number | null;
}

export function calculateSnapPoint(
  frame: number,
  snapPoints: number[],
  thresholdFrames: number = 8
): SnapResult {
  let closest: number | null = null;
  let minDiff = Infinity;

  for (const pt of snapPoints) {
    const diff = Math.abs(pt - frame);
    if (diff <= thresholdFrames && diff < minDiff) {
      minDiff = diff;
      closest = pt;
    }
  }

  if (closest !== null) {
    return { snappedFrame: closest, snappedTo: closest };
  }
  return { snappedFrame: frame, snappedTo: null };
}

export function moveClips(
  clips: PlanClipDto[],
  clipId: string,
  newStartFrame: number
): PlanClipDto[] {
  const target = clips.find((c) => c.clipId === clipId);
  if (!target) return clips;

  const validStart = Math.max(0, newStartFrame);
  const delta = validStart - target.timelineStartFrame;
  if (delta === 0) return clips;

  const linked = target.linkedClipId
    ? clips.find((c) => c.clipId === target.linkedClipId)
    : null;

  return clips.map((c) => {
    if (c.clipId === target.clipId || (linked && c.clipId === linked.clipId)) {
      return {
        ...c,
        timelineStartFrame: Math.max(0, c.timelineStartFrame + delta),
      };
    }
    return c;
  });
}

export function trimClips(
  clips: PlanClipDto[],
  clipId: string,
  edge: 'start' | 'end',
  newFrame: number,
  timeBase: TimelineTimeBaseDto
): PlanClipDto[] {
  const target = clips.find((c) => c.clipId === clipId);
  if (!target) return clips;

  const secPerFrame = timeBase.frameDurationNum / timeBase.frameDurationDen;
  const usPerFrame = secPerFrame * 1_000_000;
  const linked = target.linkedClipId
    ? clips.find((c) => c.clipId === target.linkedClipId)
    : null;

  let newStart = target.timelineStartFrame;
  let newDuration = target.durationFrames;
  let newSourceIn = target.sourceInUs;
  let newSourceOut = target.sourceOutUs;

  if (edge === 'start') {
    const clampedFrame = Math.max(0, Math.min(newFrame, target.timelineStartFrame + target.durationFrames - 1));
    const deltaFrames = clampedFrame - target.timelineStartFrame;
    newStart = clampedFrame;
    newDuration = target.durationFrames - deltaFrames;
    newSourceIn = Math.max(0, target.sourceInUs + Math.round(deltaFrames * usPerFrame));
  } else {
    const clampedFrame = Math.max(target.timelineStartFrame + 1, newFrame);
    newDuration = clampedFrame - target.timelineStartFrame;
    newSourceOut = target.sourceInUs + Math.round(newDuration * usPerFrame);
  }

  return clips.map((c) => {
    if (c.clipId === target.clipId || (linked && c.clipId === linked.clipId)) {
      return {
        ...c,
        timelineStartFrame: newStart,
        durationFrames: newDuration,
        sourceInUs: newSourceIn,
        sourceOutUs: newSourceOut,
      };
    }
    return c;
  });
}

/** `idSuffix` gera os IDs das partes novas (padrão: relógio). Reaplicação passa sufixo único. */
export function splitClips(
  clips: PlanClipDto[],
  clipId: string,
  atFrame: number,
  timeBase: TimelineTimeBaseDto,
  idSuffix: string = String(Date.now())
): PlanClipDto[] {
  const target = clips.find((c) => c.clipId === clipId);
  if (!target) return clips;

  const start = target.timelineStartFrame;
  const end = target.timelineStartFrame + target.durationFrames;
  if (atFrame <= start || atFrame >= end) return clips;

  const secPerFrame = timeBase.frameDurationNum / timeBase.frameDurationDen;
  const usPerFrame = secPerFrame * 1_000_000;
  const deltaLeftFrames = atFrame - start;
  const deltaRightFrames = end - atFrame;
  const splitSourceUs = target.sourceInUs + Math.round(deltaLeftFrames * usPerFrame);

  const linked = target.linkedClipId
    ? clips.find((c) => c.clipId === target.linkedClipId)
    : null;

  const newPart1Id = target.clipId;
  const newPart2Id = `${target.clipId}_split_${idSuffix}`;
  const newPart1LinkedId = linked ? linked.clipId : null;
  const newPart2LinkedId = linked ? `${linked.clipId}_split_${idSuffix}` : null;

  const part1Target: PlanClipDto = {
    ...target,
    durationFrames: deltaLeftFrames,
    sourceOutUs: splitSourceUs,
    linkedClipId: newPart1LinkedId,
  };

  const part2Target: PlanClipDto = {
    ...target,
    clipId: newPart2Id,
    timelineStartFrame: atFrame,
    durationFrames: deltaRightFrames,
    sourceInUs: splitSourceUs,
    linkedClipId: newPart2LinkedId,
  };

  const result: PlanClipDto[] = [];
  for (const c of clips) {
    if (c.clipId === target.clipId) {
      result.push(part1Target, part2Target);
    } else if (linked && c.clipId === linked.clipId) {
      const part1Linked: PlanClipDto = {
        ...linked,
        durationFrames: deltaLeftFrames,
        sourceOutUs: splitSourceUs,
        linkedClipId: newPart1Id,
      };
      const part2Linked: PlanClipDto = {
        ...linked,
        clipId: newPart2LinkedId!,
        timelineStartFrame: atFrame,
        durationFrames: deltaRightFrames,
        sourceInUs: splitSourceUs,
        linkedClipId: newPart2Id,
      };
      result.push(part1Linked, part2Linked);
    } else {
      result.push(c);
    }
  }
  return result;
}

export function duplicateClips(
  clips: PlanClipDto[],
  clipId: string,
  idSuffix: string = String(Date.now())
): PlanClipDto[] {
  const target = clips.find((c) => c.clipId === clipId);
  if (!target) return clips;

  const linked = target.linkedClipId
    ? clips.find((c) => c.clipId === target.linkedClipId)
    : null;

  const maxEndFrame = clips.length > 0
    ? Math.max(...clips.map((c) => c.timelineStartFrame + c.durationFrames))
    : 0;

  const dupTargetId = `${target.clipId}_dup_${idSuffix}`;
  const dupLinkedId = linked ? `${linked.clipId}_dup_${idSuffix}` : null;

  const dupTarget: PlanClipDto = {
    ...target,
    clipId: dupTargetId,
    timelineStartFrame: maxEndFrame,
    linkedClipId: dupLinkedId,
  };

  const toAdd = [dupTarget];
  if (linked) {
    toAdd.push({
      ...linked,
      clipId: dupLinkedId!,
      timelineStartFrame: maxEndFrame,
      linkedClipId: dupTargetId,
    });
  }

  return [...clips, ...toAdd];
}

export function deleteClips(clips: PlanClipDto[], clipId: string): PlanClipDto[] {
  const target = clips.find((c) => c.clipId === clipId);
  if (!target) return clips;

  const toRemove = new Set<string>([target.clipId]);
  if (target.linkedClipId) toRemove.add(target.linkedClipId);

  return clips.filter((c) => !toRemove.has(c.clipId));
}
