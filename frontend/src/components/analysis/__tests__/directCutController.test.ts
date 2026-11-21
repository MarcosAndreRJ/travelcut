import { describe, expect, it, vi } from 'vitest';
import {
  attemptKey,
  confirmedCutsFrom,
  DirectCutController,
  interpretCutResponse,
  PLAN_STALE_BASE_CODE,
  STALE_BASE_MESSAGE,
  type DirectCutApi
} from '../directCutController';
import type { CutApplyResponse, DecisionResultDto, EditSuggestion } from '../../../types/ipc';

const suggestion = (over: Partial<EditSuggestion> = {}): EditSuggestion => ({
  suggestionId: 's-1',
  mediaId: 'm-1',
  startSeconds: 1,
  endSeconds: 2,
  kind: 'manual_cut',
  rationale: 'teste',
  confidenceBand: 'high',
  needsHumanReview: false,
  suggestedAction: 'cut',
  status: 'Approved',
  author: 'Editor',
  ...over
});

const applied = (over: Partial<CutApplyResponse> = {}): CutApplyResponse => ({
  suggestion: suggestion(),
  applied: true,
  alreadyApplied: false,
  planRevision: null,
  planChanged: false,
  planError: null,
  planSkippedReason: null,
  appliedSourceStartSeconds: 1,
  appliedSourceEndSeconds: 2,
  ...over
});

const decision = (planRevision: number | null = null): DecisionResultDto => ({
  suggestions: [],
  recordedCount: 1,
  missingSuggestionIds: [],
  planRevision,
  planChanged: false,
  planError: null
});

type Mocked = { [K in keyof DirectCutApi]: ReturnType<typeof vi.fn> };

function fakeApi(overrides: Partial<Mocked> = {}): Mocked {
  return {
    applyDirectCut: vi.fn().mockResolvedValue(applied()),
    undoDecision: vi.fn().mockResolvedValue(decision()),
    redoDecision: vi.fn().mockResolvedValue(decision()),
    listSuggestions: vi.fn().mockResolvedValue([suggestion()]),
    ...overrides
  } as Mocked;
}

const cutInput = { mediaId: 'm-1', startSec: 1, endSec: 2, rationale: 'corte' };

describe('interpretCutResponse', () => {
  it('confirma só com applied=true', () => {
    expect(interpretCutResponse(applied()).status).toBe('applied');
  });

  it('applied=false vira não aplicado com o motivo real do host', () => {
    const out = interpretCutResponse(applied({ applied: false, planSkippedReason: 'PLAN_MANUAL_PROTECTED' }));
    expect(out).toEqual({
      status: 'not_applied',
      reason: 'o plano tem edições manuais protegidas e não foi regenerado'
    });
  });

  it('CUT_NOT_APPLIED vira não aplicado mesmo com applied=true', () => {
    const out = interpretCutResponse(applied({ applied: true, planError: 'CUT_NOT_APPLIED' }));
    expect(out.status).toBe('not_applied');
    if (out.status === 'not_applied') expect(out.reason).toContain('o corte não entrou nele');
  });

  it('PLAN_MANUAL_EDITS_UNREADABLE em planSkippedReason mantém applied com aviso', () => {
    const out = interpretCutResponse(applied({ planSkippedReason: 'PLAN_MANUAL_EDITS_UNREADABLE' }));
    expect(out.status).toBe('applied');
    if (out.status === 'applied') expect(out.warning).toContain('não puderam ser lidas');
  });

  it('PLAN_REGENERATION_FAILED em planError mantém applied com aviso', () => {
    const out = interpretCutResponse(applied({ planError: 'PLAN_REGENERATION_FAILED' }));
    expect(out.status).toBe('applied');
    if (out.status === 'applied') expect(out.warning).toContain('falha ao regenerar o plano');
  });

  it('aviso de plano não regenerado não impede a confirmação do corte', () => {
    const out = interpretCutResponse(applied({ planError: 'falha do FFmpeg' }));
    expect(out.status).toBe('applied');
    if (out.status === 'applied') expect(out.warning).toContain('falha do FFmpeg');
  });
});

describe('confirmedCutsFrom', () => {
  it('considera só aprovados com ação cut, usando o intervalo efetivo', () => {
    const list = [
      suggestion({ suggestionId: 'a', effectiveStartSeconds: 3, effectiveEndSeconds: 4 }),
      suggestion({ suggestionId: 'b', status: 'Proposed' }),
      suggestion({ suggestionId: 'c', suggestedAction: 'speed2x' }),
      suggestion({ suggestionId: 'd', mediaId: 'm-2' })
    ];
    expect(confirmedCutsFrom(list)).toEqual([
      { suggestionId: 'a', mediaId: 'm-1', startSec: 3, endSec: 4 },
      { suggestionId: 'd', mediaId: 'm-2', startSec: 1, endSec: 2 }
    ]);
  });
});

describe('DirectCutController.cut', () => {
  it('não mostra sucesso quando applied=false e exibe o motivo', async () => {
    const api = fakeApi({
      applyDirectCut: vi.fn().mockResolvedValue(applied({ applied: false, planSkippedReason: 'PLAN_MANUAL_PROTECTED' }))
    });
    const c = new DirectCutController(api, 'p-1', () => 'rid-1');
    const out = await c.cut(cutInput);
    expect(out?.status).toBe('not_applied');
    const st = c.getSnapshot();
    expect(st.feedback?.tone).toBe('warning');
    expect(st.feedback?.text).toContain('edições manuais protegidas');
    expect(st.feedback?.text).not.toContain('Corte aplicado');
    expect(st.canUndo).toBe(false);
  });

  it('confirma com "Corte aplicado" e recarrega os cortes do host', async () => {
    const api = fakeApi();
    const c = new DirectCutController(api, 'p-1', () => 'rid-1');
    const out = await c.cut(cutInput);
    expect(out?.status).toBe('applied');
    expect(c.getSnapshot().feedback?.text).toBe('Corte aplicado.');
    expect(c.getSnapshot().canUndo).toBe(true);
    expect(api.listSuggestions).toHaveBeenCalledWith('p-1');
    expect(c.getSnapshot().confirmed).toEqual([{ suggestionId: 's-1', mediaId: 'm-1', startSec: 1, endSec: 2 }]);
  });

  it('envia baseRevision da última planRevision conhecida', async () => {
    const api = fakeApi({ applyDirectCut: vi.fn().mockResolvedValue(applied({ planRevision: 7 })) });
    const c = new DirectCutController(api, 'p-1', () => 'rid-1');
    await c.cut(cutInput);
    await c.cut({ ...cutInput, startSec: 5, endSec: 6 });
    expect(api.applyDirectCut.mock.calls[0][0].baseRevision).toBeNull();
    expect(api.applyDirectCut.mock.calls[1][0].baseRevision).toBe(7);
  });

  it('repetir a mesma tentativa após falha reutiliza o mesmo requestId', async () => {
    const ids = ['rid-A', 'rid-B'];
    const api = fakeApi({
      applyDirectCut: vi
        .fn()
        .mockRejectedValueOnce(Object.assign(new Error('gravação falhou'), { code: 'WRITE_FAILED' }))
        .mockResolvedValueOnce(applied())
    });
    const c = new DirectCutController(api, 'p-1', () => ids.shift() as string);
    const first = await c.cut(cutInput);
    expect(first).toEqual({ status: 'error', code: 'WRITE_FAILED', message: 'gravação falhou' });
    expect(c.getSnapshot().feedback?.tone).toBe('error');
    await c.cut(cutInput);
    expect(api.applyDirectCut.mock.calls[0][0].requestId).toBe('rid-A');
    expect(api.applyDirectCut.mock.calls[1][0].requestId).toBe('rid-A');
  });

  it('intervalo novo gera outra requestId', async () => {
    const ids = ['rid-A', 'rid-B'];
    const api = fakeApi({ applyDirectCut: vi.fn().mockResolvedValue(applied({ applied: false, planError: 'x' })) });
    const c = new DirectCutController(api, 'p-1', () => ids.shift() as string);
    await c.cut(cutInput);
    await c.cut({ ...cutInput, endSec: 3 });
    expect(api.applyDirectCut.mock.calls[0][0].requestId).toBe('rid-A');
    expect(api.applyDirectCut.mock.calls[1][0].requestId).toBe('rid-B');
  });

  it('PLAN_STALE_BASE mostra orientação para recarregar', async () => {
    const api = fakeApi({
      applyDirectCut: vi.fn().mockRejectedValue(Object.assign(new Error('stale'), { code: PLAN_STALE_BASE_CODE }))
    });
    const c = new DirectCutController(api, 'p-1', () => 'rid-1');
    const out = await c.cut(cutInput);
    expect(out).toEqual({ status: 'error', code: PLAN_STALE_BASE_CODE, message: STALE_BASE_MESSAGE });
    expect(c.getSnapshot().feedback?.text).toContain('Recarregue o projeto');
  });

  it('intervalo abaixo de 0,05s não chama o host', async () => {
    const api = fakeApi();
    const c = new DirectCutController(api, 'p-1', () => 'rid-1');
    await c.cut({ ...cutInput, endSec: 1.01 });
    expect(api.applyDirectCut).not.toHaveBeenCalled();
  });

  it('applied com alreadyApplied não empilha desfazer duplicado', async () => {
    const api = fakeApi({ applyDirectCut: vi.fn().mockResolvedValue(applied({ alreadyApplied: true })) });
    const c = new DirectCutController(api, 'p-1', () => 'rid-1');
    await c.cut(cutInput);
    expect(c.getSnapshot().canUndo).toBe(false);
    expect(c.getSnapshot().feedback?.text).toContain('já aplicado');
  });
});

describe('DirectCutController undo/redo', () => {
  it('desfaz e refaz o último corte via undoDecision/redoDecision e atualiza o texto', async () => {
    const api = fakeApi({
      undoDecision: vi.fn().mockResolvedValue(decision(3)),
      redoDecision: vi.fn().mockResolvedValue(decision(4))
    });
    const c = new DirectCutController(api, 'p-1', () => 'rid-1');
    await c.cut(cutInput);

    await c.undo();
    expect(api.undoDecision).toHaveBeenCalledWith('s-1');
    expect(c.getSnapshot().feedback?.text).toBe('Corte desfeito.');
    expect(c.getSnapshot().canUndo).toBe(false);
    expect(c.getSnapshot().canRedo).toBe(true);
    expect(c.getSnapshot().planRevision).toBe(3);

    await c.redo();
    expect(api.redoDecision).toHaveBeenCalledWith('s-1');
    expect(c.getSnapshot().feedback?.text).toBe('Corte refeito.');
    expect(c.getSnapshot().canUndo).toBe(true);
    expect(c.getSnapshot().canRedo).toBe(false);
    expect(c.getSnapshot().planRevision).toBe(4);
  });

  it('erro em undo mantém a pilha e mostra erro', async () => {
    const api = fakeApi({ undoDecision: vi.fn().mockRejectedValue(new Error('NOTHING_TO_UNDO')) });
    const c = new DirectCutController(api, 'p-1', () => 'rid-1');
    await c.cut(cutInput);
    await c.undo();
    expect(c.getSnapshot().feedback?.tone).toBe('error');
    expect(c.getSnapshot().canUndo).toBe(true);
  });

  it('undo sem corte anterior não chama o host', async () => {
    const api = fakeApi();
    const c = new DirectCutController(api, 'p-1', () => 'rid-1');
    await c.undo();
    expect(api.undoDecision).not.toHaveBeenCalled();
  });

  it('novo corte limpa a pilha de refazer', async () => {
    const api = fakeApi();
    const c = new DirectCutController(api, 'p-1', () => 'rid-1');
    await c.cut(cutInput);
    await c.undo();
    expect(c.getSnapshot().canRedo).toBe(true);
    await c.cut({ ...cutInput, startSec: 5, endSec: 6 });
    expect(c.getSnapshot().canRedo).toBe(false);
  });
});

describe('attemptKey', () => {
  it('muda com a revisão-base', () => {
    expect(attemptKey('m', 1, 2, null)).not.toBe(attemptKey('m', 1, 2, 3));
  });
});
