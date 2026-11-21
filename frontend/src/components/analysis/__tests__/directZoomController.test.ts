import { describe, expect, it, vi } from 'vitest';
import { DirectZoomController, type DirectZoomApi, ZOOM_CLIP_OCCUPIED_CODE } from '../directZoomController';
import type { DecisionResultDto, EditSuggestion, ZoomApplyResponse } from '../../../types/ipc';

const sug = (over: Partial<EditSuggestion> = {}): EditSuggestion => ({
  suggestionId: 'z-1',
  mediaId: 'm-1',
  startSeconds: 10,
  endSeconds: 14,
  kind: 'manual_zoom',
  rationale: '',
  confidenceBand: 'high',
  needsHumanReview: false,
  suggestedAction: 'zoom',
  status: 'Approved',
  author: 'Editor',
  parameters: { zoom: { zoomType: 'punch_in', scale: 1.08, focalPointX: 0.5, focalPointY: 0.5, durationSeconds: 4 } },
  ...over
});

const ok = (over: Partial<ZoomApplyResponse> = {}): ZoomApplyResponse => ({
  suggestion: sug(),
  applied: true,
  alreadyApplied: false,
  planRevision: 2,
  planChanged: true,
  planError: null,
  planSkippedReason: null,
  zoom: { scale: 1.08, focalPointX: 0.5, focalPointY: 0.5, transitionInSeconds: 0.35, transitionOutSeconds: 0.35, sourceStartSeconds: 10, sourceEndSeconds: 14 },
  ...over
});

const dec = (over: Partial<DecisionResultDto> = {}): DecisionResultDto => ({
  suggestions: [],
  recordedCount: 1,
  missingSuggestionIds: [],
  planRevision: 3,
  planChanged: true,
  planError: null,
  ...over
});

const input = {
  mediaId: 'm-1',
  startSec: 10,
  endSec: 14,
  scale: 1.08,
  focalPointX: 0.5,
  focalPointY: 0.5,
  transitionInSeconds: 0.35,
  transitionOutSeconds: 0.35,
  rationale: 'teste'
};

const makeApi = (over: Partial<DirectZoomApi> = {}): DirectZoomApi => ({
  applyDirectZoom: vi.fn().mockResolvedValue(ok()),
  undoDecision: vi.fn().mockResolvedValue(dec()),
  redoDecision: vi.fn().mockResolvedValue(dec()),
  submitDecision: vi.fn().mockResolvedValue(dec()),
  listSuggestions: vi.fn().mockResolvedValue([sug()]),
  getEditPlan: vi.fn().mockResolvedValue(null),
  ...over
});

describe('DirectZoomController', () => {
  it('confirma zoom só com applied=true e oferece Desfazer', async () => {
    const api = makeApi();
    const c = new DirectZoomController(api, 'p-1', () => 'req-1');
    const out = await c.apply(input);
    expect(out?.status).toBe('applied');
    expect(c.getSnapshot().feedback?.text).toContain('Zoom aplicado 1,08×');
    expect(c.getSnapshot().feedback?.action?.op).toBe('undo');
    expect(c.getSnapshot().canUndo).toBe(true);
  });

  it('applied=false não confirma e não oferece desfazer', async () => {
    const api = makeApi({
      applyDirectZoom: vi.fn().mockResolvedValue(
        ok({ applied: false, suggestion: sug({ status: 'Proposed' }), planError: 'ZOOM_NOT_APPLIED', zoom: null })
      )
    });
    const c = new DirectZoomController(api, 'p-1');
    const out = await c.apply(input);
    expect(out?.status).toBe('not_applied');
    expect(c.getSnapshot().feedback?.text).not.toContain('Zoom aplicado');
    expect(c.getSnapshot().feedback?.action).toBeFalsy();
  });

  it('ZOOM_CLIP_OCCUPIED vira mensagem clara sem confirmar', async () => {
    const err = Object.assign(new Error('x'), { code: ZOOM_CLIP_OCCUPIED_CODE });
    const api = makeApi({ applyDirectZoom: vi.fn().mockRejectedValue(err) });
    const c = new DirectZoomController(api, 'p-1');
    const out = await c.apply(input);
    expect(out?.status).toBe('error');
    expect(c.getSnapshot().feedback?.tone).toBe('error');
    expect(c.getSnapshot().feedback?.text).toContain('1 zoom por trecho');
  });

  it('escala fora da UI não chama o host', async () => {
    const api = makeApi();
    const c = new DirectZoomController(api, 'p-1');
    const out = await c.apply({ ...input, scale: 1.25 });
    expect(out?.status).toBe('invalid');
    expect(api.applyDirectZoom).not.toHaveBeenCalled();
  });

  it('repetir a mesma tentativa reutiliza a requestId e base revision', async () => {
    const api = makeApi({ applyDirectZoom: vi.fn().mockRejectedValue(new Error('falha de rede')) });
    const ids = ['a', 'b'];
    const c = new DirectZoomController(api, 'p-1', () => ids.shift() as string);
    await c.apply(input);
    await c.apply(input);
    const calls = (api.applyDirectZoom as ReturnType<typeof vi.fn>).mock.calls;
    expect(calls[0][0].requestId).toBe('a');
    expect(calls[1][0].requestId).toBe('a');
  });

  it('desfazer e refazer chamam decision.undo/redo no mesmo id', async () => {
    const api = makeApi();
    const c = new DirectZoomController(api, 'p-1', () => 'r');
    await c.apply(input);
    await c.runFeedbackAction({ label: 'Desfazer', op: 'undo', suggestionIds: ['z-1'] });
    expect(api.undoDecision).toHaveBeenCalledWith('z-1');
    expect(c.getSnapshot().canRedo).toBe(true);
    await c.runFeedbackAction({ label: 'Refazer', op: 'redo', suggestionIds: ['z-1'] });
    expect(api.redoDecision).toHaveBeenCalledWith('z-1');
  });

  it('remover zoom usa reject no zoom e não toca em cortes', async () => {
    const cut = sug({ suggestionId: 'cut-1', suggestedAction: 'cut', startSeconds: 10, endSeconds: 14 });
    const api = makeApi({ listSuggestions: vi.fn().mockResolvedValue([sug(), cut]) });
    const c = new DirectZoomController(api, 'p-1');
    const out = await c.removeOverlapping({ mediaId: 'm-1', startSec: 11, endSec: 12 });
    expect(out).toEqual({ status: 'removed', suggestionIds: ['z-1'] });
    expect(api.submitDecision).toHaveBeenCalledTimes(1);
    expect(api.submitDecision).toHaveBeenCalledWith(expect.objectContaining({ suggestionId: 'z-1', action: 'reject' }));
    expect(c.getSnapshot().feedback?.text).toContain('cortes do trecho foram mantidos');
  });

  it('erro real do host ao remover aparece na confirmação', async () => {
    const api = makeApi({ submitDecision: vi.fn().mockRejectedValue(new Error('PLAN_STALE')) });
    const c = new DirectZoomController(api, 'p-1');
    const out = await c.removeOverlapping({ mediaId: 'm-1', startSec: 11, endSec: 12 });
    expect(out?.status).toBe('error');
    expect(c.getSnapshot().feedback?.tone).toBe('error');
    expect(c.getSnapshot().feedback?.text).toContain('PLAN_STALE');
  });

  it('zoom removido some da marcação após refresh', async () => {
    const list = [sug()];
    const api = makeApi({
      listSuggestions: vi.fn().mockImplementation(async () => list.map((s) => ({ ...s }))),
      submitDecision: vi.fn().mockImplementation(async () => {
        list[0] = { ...list[0], status: 'Rejected' };
        return dec();
      })
    });
    const c = new DirectZoomController(api, 'p-1');
    await c.refresh();
    expect(c.getSnapshot().windows).toHaveLength(1);
    await c.removeOverlapping({ mediaId: 'm-1', startSec: 11, endSec: 12 });
    expect(c.getSnapshot().windows).toHaveLength(0);
  });
});
