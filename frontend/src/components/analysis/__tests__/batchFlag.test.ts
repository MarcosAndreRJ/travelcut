import { describe, expect, it } from 'vitest';
import type { EditSuggestion } from '../../../types/ipc';
import { isBatchApprovable } from '../analysisFormat';

const base = {
  suggestionId: 's1',
  mediaId: 'm1',
  startSeconds: 0,
  endSeconds: 1,
  kind: 'silence',
  rationale: '',
  confidenceBand: 'high',
  needsHumanReview: false,
  suggestedAction: 'cut',
  status: 'Proposed',
  author: 'test'
} as EditSuggestion;

describe('isBatchApprovable com flag do host', () => {
  it('flag true do backend vale mesmo fora da regra local (ex.: confiança média)', () => {
    expect(isBatchApprovable({ ...base, confidenceBand: 'medium', batchApprovable: true })).toBe(true);
  });

  it('flag false do backend bloqueia item que a regra local aceitaria', () => {
    expect(isBatchApprovable({ ...base, batchApprovable: false })).toBe(false);
  });

  it('sem flag (campo ausente ou null) usa a regra local', () => {
    expect(isBatchApprovable(base)).toBe(true);
    expect(isBatchApprovable({ ...base, batchApprovable: null })).toBe(true);
    expect(isBatchApprovable({ ...base, kind: 'pause' })).toBe(false);
  });
});
