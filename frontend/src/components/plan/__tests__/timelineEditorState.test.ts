import { describe, expect, it } from 'vitest';
import type { EditPlanDto, PlanEditorialOperationDto } from '../../../types/ipc';
import {
  canRedoHistory,
  canUndoHistory,
  createHistory,
  currentPlan,
  decideIncoming,
  isHistoryDirty,
  markHistorySaved,
  operationsSinceSaved,
  pushHistory,
  redoHistory,
  undoHistory
} from '../timelineEditorState';

const plan = (revision: number, planId = 'plan-1') =>
  ({ planId, projectId: 'proj-1', revision, clips: [] } as unknown as EditPlanDto);

const op = (id: string): PlanEditorialOperationDto => ({
  operationId: id,
  type: 'DELETE',
  clipId: 'c1',
  timestampUtc: '2026-11-09T00:00:00Z'
});

describe('histórico contínuo (savedIndex)', () => {
  it('undo e redo atravessam entradas e atualizam o plano corrente', () => {
    const a = plan(1);
    const b = plan(1);
    const c = plan(1);
    let h = createHistory(a);
    h = pushHistory(h, b, op('o1'));
    h = pushHistory(h, c, op('o2'));
    expect(currentPlan(h)).toBe(c);
    h = undoHistory(h);
    expect(currentPlan(h)).toBe(b);
    h = undoHistory(h);
    expect(currentPlan(h)).toBe(a);
    expect(canUndoHistory(h)).toBe(false);
    h = redoHistory(h);
    h = redoHistory(h);
    expect(currentPlan(h)).toBe(c);
    expect(canRedoHistory(h)).toBe(false);
  });

  it('salvar não zera o histórico e undo volta a ficar sujo', () => {
    const a = plan(1);
    const b = plan(1);
    const saved = plan(2);
    let h = pushHistory(createHistory(a), b, op('o1'));
    const idx = h.index;
    h = markHistorySaved(h, idx, b, saved);

    expect(h.entries).toHaveLength(2);
    expect(isHistoryDirty(h)).toBe(false);
    expect(currentPlan(h)).toBe(saved);

    h = undoHistory(h);
    expect(isHistoryDirty(h)).toBe(true);
    h = redoHistory(h);
    expect(isHistoryDirty(h)).toBe(false);
  });

  it('edição após salvar fica suja e o salvamento seguinte cobre só o novo trecho', () => {
    const a = plan(1);
    const b = plan(1);
    const saved = plan(2);
    const c = plan(2);
    let h = pushHistory(createHistory(a), b, op('o1'));
    h = markHistorySaved(h, h.index, b, saved);
    h = pushHistory(h, c, op('o2'));
    expect(isHistoryDirty(h)).toBe(true);
    expect(operationsSinceSaved(h).map((o) => o.operationId)).toEqual(['o2']);
  });

  it('push a partir de um estado anterior ao salvo torna o histórico sempre sujo', () => {
    const a = plan(1);
    const b = plan(1);
    const saved = plan(2);
    let h = pushHistory(createHistory(a), b, op('o1'));
    h = markHistorySaved(h, h.index, b, saved);
    h = undoHistory(h);
    h = pushHistory(h, plan(1), op('o3'));
    expect(h.savedIndex).toBe(-1);
    expect(isHistoryDirty(h)).toBe(true);
  });

  it('salvamento com entrada que já saiu do histórico mantém o estado sujo', () => {
    const a = plan(1);
    const b = plan(1);
    let h = pushHistory(createHistory(a), b, op('o1'));
    h = markHistorySaved(h, h.index, plan(1), plan(2));
    expect(isHistoryDirty(h)).toBe(true);
  });

  it('log de operações acompanha o ramo atual (undo remove a operação)', () => {
    let h = pushHistory(createHistory(plan(1)), plan(1), op('o1'));
    h = pushHistory(h, plan(1), op('o2'));
    h = undoHistory(h);
    expect(operationsSinceSaved(h).map((o) => o.operationId)).toEqual(['o1']);
  });
});

describe('decideIncoming (revisão vinda de fora)', () => {
  const base = { planId: 'plan-1', revision: 3 };

  it('ignora o eco da mesma revisão, mesmo com edições', () => {
    expect(decideIncoming(base, { planId: 'plan-1', revision: 3 }, true, 'external')).toBe('ignore');
    expect(decideIncoming(base, { planId: 'plan-1', revision: 3 }, false, 'external')).toBe('ignore');
  });

  it('adota a revisão nova quando não há edições não salvas', () => {
    expect(decideIncoming(base, { planId: 'plan-1', revision: 4 }, false, 'external')).toBe('adopt');
  });

  it('mostra aviso (sem descartar) quando chega revisão nova com edições', () => {
    expect(decideIncoming(base, { planId: 'plan-1', revision: 4 }, true, 'external')).toBe('banner');
  });

  it('recarga explícita do mesmo plano com edições faz rebase leve', () => {
    expect(decideIncoming(base, { planId: 'plan-1', revision: 4 }, true, 'reload')).toBe('rebase');
  });

  it('plano de outro projeto com edições não é tratado como rebase', () => {
    expect(decideIncoming(base, { planId: 'plan-2', revision: 1 }, true, 'reload')).toBe('banner');
    expect(decideIncoming(null, { planId: 'plan-2', revision: 1 }, true, 'external')).toBe('banner');
  });
});
