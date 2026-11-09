import { describe, expect, it } from 'vitest';
import type { PlanTransitionDto } from '../../../types/ipc';
import { effectTransitionAfter } from '../PlanPreviewPlayer';

const t = (fromClipId: string, kind: string): PlanTransitionDto => ({
  fromClipId,
  toClipId: `${fromClipId}-next`,
  kind,
  durationFrames: 30,
  needsHumanReview: false
});

describe('effectTransitionAfter (limitação rotulada da prévia)', () => {
  it('retorna fade/dissolve/crossfade que saem do clipe ativo', () => {
    expect(effectTransitionAfter([t('a', 'fade')], 'a')?.kind).toBe('fade');
    expect(effectTransitionAfter([t('a', 'dissolve')], 'a')?.kind).toBe('dissolve');
    expect(effectTransitionAfter([t('a', 'crossfade')], 'a')?.kind).toBe('crossfade');
  });

  it('ignora corte seco, transições de outros clipes e clipe ausente', () => {
    expect(effectTransitionAfter([t('a', 'cut')], 'a')).toBeNull();
    expect(effectTransitionAfter([t('b', 'fade')], 'a')).toBeNull();
    expect(effectTransitionAfter([t('a', 'fade')], null)).toBeNull();
    expect(effectTransitionAfter([], 'a')).toBeNull();
  });
});
