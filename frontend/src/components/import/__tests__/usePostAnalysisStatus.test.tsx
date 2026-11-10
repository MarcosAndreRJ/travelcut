import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import type { PostAnalysisStatusDto, PostAnalysisState } from '../../../types/ipc';
import {
  startPostAnalysisPolling,
  isTerminalPostAnalysis,
  isPostAnalysisInTransition,
  POST_ANALYSIS_POLL_MS,
  POST_ANALYSIS_TRANSITION_MAX_POLLS
} from '../usePostAnalysisStatus';

const post = (state: PostAnalysisState): PostAnalysisStatusDto => ({
  state,
  stage: state === 'running' ? 'plan' : null,
  reason: null,
  autoCutsApplied: 0,
  pendingReview: 0,
  proxiesQueued: 0,
  failures: 0,
  issues: [],
  planRevision: null,
  startedAtUtc: null,
  finishedAtUtc: null
});

/** Controlador sem DOM: o mesmo código do hook, com timers falsos. */
const setup = (responses: (PostAnalysisStatusDto | null | Error)[] | (() => PostAnalysisStatusDto | null)) => {
  const fetchStatus = vi.fn(async () => {
    if (typeof responses === 'function') return responses();
    const next = responses.length > 1 ? responses.shift() : responses[0];
    if (next instanceof Error) throw next;
    return next ?? null;
  });
  const onStatus = vi.fn();
  const stop = startPostAnalysisPolling({
    fetchStatus,
    onStatus,
    setTimer: (cb, ms) => setTimeout(cb, ms),
    clearTimer: (h) => clearTimeout(h as ReturnType<typeof setTimeout>)
  });
  return { fetchStatus, onStatus, stop };
};

const flush = async (ms: number): Promise<void> => {
  await vi.advanceTimersByTimeAsync(ms);
};

beforeEach(() => {
  vi.useFakeTimers();
});

afterEach(() => {
  vi.useRealTimers();
});

describe('classificacao de postAnalysis', () => {
  it('separa estados terminais da janela de transicao', () => {
    expect(isTerminalPostAnalysis(post('completed'))).toBe(true);
    expect(isTerminalPostAnalysis(post('completed_with_issues'))).toBe(true);
    expect(isTerminalPostAnalysis(post('failed'))).toBe(true);
    expect(isTerminalPostAnalysis(post('skipped'))).toBe(true);
    expect(isTerminalPostAnalysis(post('running'))).toBe(false);
    expect(isTerminalPostAnalysis(post('idle'))).toBe(false);
    expect(isTerminalPostAnalysis(null)).toBe(false);
    expect(isPostAnalysisInTransition(post('idle'))).toBe(true);
    expect(isPostAnalysisInTransition(null)).toBe(true);
    expect(isPostAnalysisInTransition(post('running'))).toBe(false);
    expect(isPostAnalysisInTransition(post('completed'))).toBe(false);
  });
});

describe('startPostAnalysisPolling', () => {
  it('acompanha running e para ao atingir estado terminal', async () => {
    const { fetchStatus, onStatus } = setup([post('running'), post('running'), post('completed')]);
    expect(fetchStatus).toHaveBeenCalledTimes(1);
    await flush(POST_ANALYSIS_POLL_MS);
    expect(fetchStatus).toHaveBeenCalledTimes(2);
    await flush(POST_ANALYSIS_POLL_MS);
    expect(fetchStatus).toHaveBeenCalledTimes(3);
    expect(onStatus).toHaveBeenLastCalledWith(expect.objectContaining({ state: 'completed' }));
    await flush(POST_ANALYSIS_POLL_MS * 10);
    expect(fetchStatus).toHaveBeenCalledTimes(3);
  });

  it('trata idle como em transicao e limita as consultas nessa janela', async () => {
    const { fetchStatus, onStatus } = setup(() => post('idle'));
    await flush(POST_ANALYSIS_POLL_MS * (POST_ANALYSIS_TRANSITION_MAX_POLLS + 10));
    expect(fetchStatus).toHaveBeenCalledTimes(POST_ANALYSIS_TRANSITION_MAX_POLLS);
    expect(onStatus).toHaveBeenLastCalledWith(expect.objectContaining({ state: 'idle' }));
  });

  it('janela ausente (postAnalysis null) tambem conta como transicao', async () => {
    const { fetchStatus } = setup(() => null);
    await flush(POST_ANALYSIS_POLL_MS * (POST_ANALYSIS_TRANSITION_MAX_POLLS + 5));
    expect(fetchStatus).toHaveBeenCalledTimes(POST_ANALYSIS_TRANSITION_MAX_POLLS);
  });

  it('idle que vira running volta a acompanhar ate terminal', async () => {
    const { fetchStatus } = setup([post('idle'), post('running'), post('completed_with_issues')]);
    await flush(POST_ANALYSIS_POLL_MS * 2);
    expect(fetchStatus).toHaveBeenCalledTimes(3);
    await flush(POST_ANALYSIS_POLL_MS * 5);
    expect(fetchStatus).toHaveBeenCalledTimes(3);
  });

  it('para o polling quando parado (desmontagem) e nao emite estado depois', async () => {
    const { fetchStatus, onStatus, stop } = setup(() => post('running'));
    expect(fetchStatus).toHaveBeenCalledTimes(1);
    stop();
    await flush(POST_ANALYSIS_POLL_MS * 5);
    expect(fetchStatus).toHaveBeenCalledTimes(1);
    expect(onStatus).not.toHaveBeenCalled();
  });

  it('erro de consulta limpa o estado e encerra o polling', async () => {
    const { fetchStatus, onStatus } = setup([new Error('host offline')]);
    await flush(POST_ANALYSIS_POLL_MS * 3);
    expect(fetchStatus).toHaveBeenCalledTimes(1);
    expect(onStatus).toHaveBeenLastCalledWith(null);
  });
});
