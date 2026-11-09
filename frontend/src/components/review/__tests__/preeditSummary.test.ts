import { describe, expect, it } from 'vitest';
import type { EditPlanDto, EditSuggestion, PlanAppliedCutDto, PlanClipDto } from '../../../types/ipc';
import {
  PREEDIT_OUTCOME_LABEL,
  derivePreeditSummary,
  formatRemovedSeconds,
  outcomeOf
} from '../preeditSummary';
import {
  DEFAULT_REVIEW_FILTERS,
  countByStatus,
  filterSuggestions,
  prioritizePending
} from '../reviewModel';

const sug = (over: Partial<EditSuggestion> & Pick<EditSuggestion, 'suggestionId'>): EditSuggestion => ({
  mediaId: 'med-1',
  startSeconds: 10,
  endSeconds: 12,
  kind: 'silence',
  rationale: '',
  confidenceBand: 'high',
  needsHumanReview: false,
  suggestedAction: 'cut',
  status: 'Proposed',
  author: 'auto',
  ...over
});

const cut = (suggestionId: string, origin: PlanAppliedCutDto['origin']): PlanAppliedCutDto => ({
  suggestionId,
  mediaId: 'med-1',
  sourceInUs: 0,
  sourceOutUs: 1,
  kind: 'silence',
  origin
});

const clip = (clipId: string, zoom: number | null): PlanClipDto =>
  ({ clipId, zoom } as unknown as PlanClipDto);

const plan = (appliedCuts: PlanAppliedCutDto[], clips: PlanClipDto[], removedSourceUs: number): EditPlanDto =>
  ({
    revision: 4,
    appliedCuts,
    clips,
    summary: { removedSourceUs }
  } as unknown as EditPlanDto);

describe('rotulos da pre-edicao', () => {
  it('usa os rotulos exatos do painel', () => {
    expect(PREEDIT_OUTCOME_LABEL.autoApplied).toBe('Aplicado automaticamente');
    expect(PREEDIT_OUTCOME_LABEL.userApplied).toBe('Aplicado pelo usuário');
    expect(PREEDIT_OUTCOME_LABEL.pending).toBe('Pendente de revisão');
    expect(PREEDIT_OUTCOME_LABEL.notApplied).toBe('Não aplicado');
    expect(PREEDIT_OUTCOME_LABEL.conflict).toBe('Conflito');
  });

  it('formata tempo removido em PT-BR', () => {
    expect(formatRemovedSeconds(12.34)).toBe('12,3 s');
    expect(formatRemovedSeconds(null)).toBe('—');
  });
});

describe('outcomeOf', () => {
  const p = plan(
    [cut('auto-1', 'auto_trivial'), cut('user-1', 'approved_by_user'), cut('sub-1', 'residual_sub_frame')],
    [],
    0
  );

  it('classifica pelo plano vigente e pelo status efetivo', () => {
    expect(outcomeOf(sug({ suggestionId: 'auto-1' }), p)).toBe('autoApplied');
    expect(outcomeOf(sug({ suggestionId: 'sub-1' }), p)).toBe('autoApplied');
    expect(outcomeOf(sug({ suggestionId: 'user-1', status: 'Approved' }), p)).toBe('userApplied');
    expect(outcomeOf(sug({ suggestionId: 'rej-1', status: 'Rejected' }), p)).toBe('notApplied');
    expect(outcomeOf(sug({ suggestionId: 'pen-1' }), p)).toBe('pending');
    expect(outcomeOf(sug({ suggestionId: 'def-1', status: 'Deferred' }), p)).toBe('deferred');
  });

  it('aprovada sem corte no plano vigente vira conflito', () => {
    expect(outcomeOf(sug({ suggestionId: 'x', status: 'Approved' }), p)).toBe('conflict');
  });

  it('sem plano, aprovada conta como aplicada pelo usuario e auto nao existe', () => {
    expect(outcomeOf(sug({ suggestionId: 'x', status: 'Approved' }), null)).toBe('userApplied');
    expect(outcomeOf(sug({ suggestionId: 'auto-1' }), null)).toBe('pending');
  });
});

describe('derivePreeditSummary', () => {
  it('conta buckets, tempo removido e zooms reais do plano', () => {
    const suggestions = [
      sug({ suggestionId: 'auto-1' }),
      sug({ suggestionId: 'user-1', status: 'Approved' }),
      sug({ suggestionId: 'pen-1' }),
      sug({ suggestionId: 'rej-1', status: 'Rejected', canRedo: true }),
      sug({ suggestionId: 'conf-1', status: 'Approved' })
    ];
    const p = plan(
      [cut('auto-1', 'auto_trivial'), cut('user-1', 'approved_by_user')],
      [clip('c1', 1.1), clip('c2', 1), clip('c3', null), clip('c4', 1.2)],
      12_300_000
    );
    const s = derivePreeditSummary(suggestions, p);
    expect(s).toMatchObject({
      total: 5,
      autoApplied: 1,
      userApplied: 1,
      pending: 1,
      notApplied: 1,
      conflict: 1,
      restored: 1,
      removedSourceSeconds: 12.3,
      zoomsApplied: 2
    });
  });

  it('sem plano, tempo removido e zooms ficam nulos', () => {
    const s = derivePreeditSummary([sug({ suggestionId: 'a' })], null);
    expect(s.removedSourceSeconds).toBeNull();
    expect(s.zoomsApplied).toBeNull();
    expect(s.pending).toBe(1);
  });
});

describe('fila e contadores com plano', () => {
  const suggestions = [
    sug({ suggestionId: 'auto-1' }),
    sug({ suggestionId: 'pen-low', confidenceBand: 'low' }),
    sug({ suggestionId: 'pen-high', confidenceBand: 'high' }),
    sug({ suggestionId: 'pen-review', confidenceBand: 'high', needsHumanReview: true })
  ];
  const p = plan([cut('auto-1', 'auto_trivial')], [], 0);

  it('pendente exclui itens ja aplicados automaticamente', () => {
    const ids = filterSuggestions(suggestions, DEFAULT_REVIEW_FILTERS, p).map((s) => s.suggestionId);
    expect(ids).toEqual(['pen-low', 'pen-high', 'pen-review']);
    expect(countByStatus(suggestions, p)).toMatchObject({ pending: 3, applied: 1 });
  });

  it('filtro aplicados lista so os aplicados', () => {
    const ids = filterSuggestions(suggestions, { ...DEFAULT_REVIEW_FILTERS, status: 'applied' }, p).map((s) => s.suggestionId);
    expect(ids).toEqual(['auto-1']);
  });

  it('prioriza revisao humana e baixa confianca', () => {
    const ordered = prioritizePending(suggestions.slice(1)).map((s) => s.suggestionId);
    expect(ordered).toEqual(['pen-review', 'pen-low', 'pen-high']);
  });
});
