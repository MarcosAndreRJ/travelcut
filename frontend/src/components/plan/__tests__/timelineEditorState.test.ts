import { describe, expect, it } from 'vitest';
import type { EditPlanDto, PlanClipDto, PlanEditorialOperationDto } from '../../../types/ipc';
import {
  canRedoHistory,
  canUndoHistory,
  createHistory,
  currentPlan,
  classifySaveError,
  decideIncoming,
  isBaseOutdated,
  isHistoryDirty,
  markHistorySaved,
  reapplyOperations,
  resolveLatestRevision,
  operationsSinceSaved,
  pushHistory,
  redoHistory,
  undoHistory
} from '../timelineEditorState';

const TB = { frameDurationNum: 1, frameDurationDen: 25 };
const clip = (clipId: string, timelineStartFrame: number, durationFrames: number, extra: Partial<PlanClipDto> = {}) =>
  ({
    clipId,
    timelineStartFrame,
    durationFrames,
    sourceInUs: 0,
    sourceOutUs: durationFrames * 40000,
    linkedClipId: null,
    ...extra
  } as unknown as PlanClipDto);
const planWith = (revision: number, clips: PlanClipDto[]) =>
  ({
    planId: 'plan-1',
    projectId: 'proj-1',
    revision,
    timeBase: TB,
    summary: { timelineDurationFrames: 0 },
    clips
  } as unknown as EditPlanDto);
const clipIds = (p: EditPlanDto) => (p.clips ?? []).map((c) => c.clipId).sort();
const editOp = (id: string, type: PlanEditorialOperationDto['type'], clipId: string, extra: Partial<PlanEditorialOperationDto> = {}): PlanEditorialOperationDto =>
  ({ operationId: id, type, clipId, timestampUtc: '2026-11-09T00:00:00Z', ...extra } as PlanEditorialOperationDto);

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

describe('classifySaveError (save com baseRevision)', () => {
  it('PLAN_STALE_BASE oferece recarregar sem descartar edições', () => {
    const info = classifySaveError('PLAN_STALE_BASE', 'x');
    expect(info.reloadable).toBe(true);
    expect(info.code).toBe('PLAN_STALE_BASE');
    expect(info.message).toContain('suas edições continuam aqui'.toLowerCase().length > 0 ? 'Suas edições' : '');
  });

  it('projeto cruzado e identificador divergente são recarregáveis', () => {
    expect(classifySaveError('PLAN_PROJECT_MISMATCH', '').reloadable).toBe(true);
    expect(classifySaveError('PLAN_ID_MISMATCH', '').reloadable).toBe(true);
    expect(classifySaveError('PLAN_NOT_FOUND', '').reloadable).toBe(true);
  });

  it('erro desconhecido usa a mensagem recebida e não oferece recarga', () => {
    const info = classifySaveError('PLAN_FILE_CORRUPT', 'arquivo corrompido');
    expect(info.reloadable).toBe(false);
    expect(info.message).toBe('arquivo corrompido');
  });

  it('sem código nem mensagem usa texto genérico', () => {
    expect(classifySaveError(null, '').message).toBe('Falha ao salvar a revisão.');
  });
});

describe('reaplicação das operações sobre a revisão nova (B-1)', () => {
  it('base antiga + edição X, revisão nova com corte automático Y: resultado contém X e Y; delta contra a nova base é só X', () => {
    // Usuário excluiu "b" sobre a base antiga R3 (edição X); o log do ramo guarda só essa operação.
    const userOps = [editOp('x', 'DELETE', 'b')];

    // Revisão nova: corte automático Y divide "a" em "a" + "a_auto".
    const latest = planWith(4, [clip('a', 0, 30), clip('a_auto', 30, 30), clip('b', 60, 50)]);
    const result = reapplyOperations(latest, userOps, 'T');
    const reapplied = currentPlan(result.history)!;

    expect(result.dropped).toHaveLength(0);
    // Contém Y (corte automático) e o efeito de X (b removido).
    expect(clipIds(reapplied)).toEqual(['a', 'a_auto']);
    // Delta contra a revisão nova: removidos = {b}, adicionados = {}.
    const base = new Set(clipIds(latest));
    const cur = new Set(clipIds(reapplied));
    expect([...base].filter((id) => !cur.has(id))).toEqual(['b']);
    expect([...cur].filter((id) => !base.has(id))).toEqual([]);
    // Log do histórico = só a intenção do usuário.
    expect(operationsSinceSaved(result.history).map((o) => o.operationId)).toEqual(['x']);
    expect(isHistoryDirty(result.history)).toBe(true);
  });

  it('operação com clipe inexistente na revisão nova é descartada e as demais são aplicadas', () => {
    const latest = planWith(4, [clip('a', 0, 60), clip('c', 60, 40)]);
    const ops = [editOp('x1', 'DELETE', 'b'), editOp('x2', 'DELETE', 'c')];
    const result = reapplyOperations(latest, ops, 'T');
    expect(result.dropped.map((o) => o.operationId)).toEqual(['x1']);
    expect(clipIds(currentPlan(result.history)!)).toEqual(['a']);
  });

  it('sem operações aplicáveis o histórico fica limpo (não sujo) sobre a revisão nova', () => {
    const latest = planWith(4, [clip('a', 0, 60)]);
    const result = reapplyOperations(latest, [editOp('x', 'DELETE', 'zzz')], 'T');
    expect(result.dropped).toHaveLength(1);
    expect(isHistoryDirty(result.history)).toBe(false);
    expect(currentPlan(result.history)).toBe(latest);
  });

  it('MOVE e TRIM reaplicam valores absolutos da operação, inclusive no par vinculado', () => {
    const latest = planWith(4, [
      clip('v', 0, 60, { linkedClipId: 'ar' }),
      clip('ar', 0, 60, { linkedClipId: 'v' })
    ]);
    const ops = [
      editOp('m', 'MOVE', 'v', { linkedClipId: 'ar', newStartFrame: 10 }),
      editOp('t', 'TRIM', 'v', {
        linkedClipId: 'ar',
        newStartFrame: 10,
        newDurationFrames: 20,
        sourceInUs: 0,
        sourceOutUs: 800000
      })
    ];
    const result = reapplyOperations(latest, ops, 'T');
    const reapplied = currentPlan(result.history)!;
    const v = reapplied.clips.find((c) => c.clipId === 'v')!;
    const ar = reapplied.clips.find((c) => c.clipId === 'ar')!;
    expect(result.dropped).toHaveLength(0);
    expect([v.timelineStartFrame, v.durationFrames]).toEqual([10, 20]);
    expect([ar.timelineStartFrame, ar.durationFrames]).toEqual([10, 20]);
  });

  it('SPLIT reaplicado gera partes com IDs únicos mesmo com o mesmo sufixo de relógio em operações seguidas', () => {
    const latest = planWith(4, [clip('a', 0, 100)]);
    const ops = [editOp('s1', 'SPLIT', 'a', { splitAtFrame: 40 }), editOp('s2', 'SPLIT', 'a', { splitAtFrame: 20 })];
    const result = reapplyOperations(latest, ops, 'T');
    const ids = clipIds(currentPlan(result.history)!);
    expect(result.dropped).toHaveLength(0);
    expect(ids).toHaveLength(3);
    expect(new Set(ids).size).toBe(3);
  });

  it('split fora dos limites do clipe da revisão nova é descartado', () => {
    const latest = planWith(4, [clip('a', 0, 30)]);
    const result = reapplyOperations(latest, [editOp('s', 'SPLIT', 'a', { splitAtFrame: 90 })], 'T');
    expect(result.dropped.map((o) => o.operationId)).toEqual(['s']);
  });
});

describe('revisão mais nova conhecida (base de revisão antiga)', () => {
  it('abrir revisão antiga sem edições não é editável quando a lista mostra revisão mais nova', () => {
    const latest = resolveLatestRevision(3, 5, null);
    expect(latest).toBe(5);
    expect(isBaseOutdated({ planId: 'plan-1', revision: 3 }, latest)).toBe(true);
  });

  it('base na revisão vigente não fica desatualizada', () => {
    expect(isBaseOutdated({ planId: 'plan-1', revision: 5 }, resolveLatestRevision(5, 5, null))).toBe(false);
  });

  it('resolveLatestRevision ignora nulos e usa o maior valor', () => {
    expect(resolveLatestRevision(null, null, null)).toBeNull();
    expect(resolveLatestRevision(2, undefined, 7)).toBe(7);
  });

  it('sem revisão conhecida não há base desatualizada', () => {
    expect(isBaseOutdated(null, null)).toBe(false);
  });
});
