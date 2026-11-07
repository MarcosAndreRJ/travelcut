import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { AnalysisEnvironmentDto, MotionCapabilitiesDto } from '../../types/ipc';
import { IntegrationStatusStore, slotStaleReason, STATUS_STALE_AFTER_MS, type IntegrationStatusClient } from '../integrationStatusStore';
import { toIntegrationSource } from '../../components/environment/useIntegrationStatus';
import { buildIntegrationRows, toolAvailability } from '../../components/environment/integrationStatus';

const env = (over: Partial<AnalysisEnvironmentDto> = {}): AnalysisEnvironmentDto =>
  ({
    pythonAvailable: true,
    whisperAvailable: true,
    whisperEngine: 'openai-whisper 20240930',
    whisperModel: 'large-v3',
    ffmpegAvailable: true,
    ffmpegVersion: '7.0',
    cudaAvailable: false,
    ...over
  }) as AnalysisEnvironmentDto;

const motion = { environmentState: 'READY', statusMessage: 'CLI detectado' } as MotionCapabilitiesDto;

interface Deferred<T> {
  promise: Promise<T>;
  resolve: (value: T) => void;
  reject: (err: Error) => void;
}

function deferred<T>(): Deferred<T> {
  let resolve!: (value: T) => void;
  let reject!: (err: Error) => void;
  const promise = new Promise<T>((res, rej) => {
    resolve = res;
    reject = rej;
  });
  return { promise, resolve, reject };
}

function setup() {
  let nowMs = 1_000_000;
  const client: IntegrationStatusClient = {
    getAnalysis: vi.fn(async () => env()),
    getMotion: vi.fn(async () => motion)
  };
  const store = new IntegrationStatusStore(client, () => nowMs);
  return { client, store, advance: (ms: number) => (nowMs += ms), now: () => nowMs };
}

const rowsFor = (store: IntegrationStatusStore, nowMs: number) => {
  const s = toIntegrationSource(store.getState(), nowMs, store.refresh);
  return buildIntegrationRows({
    isMock: false,
    editorCaps: null,
    analysisEnv: s.analysisEnv,
    analysisError: s.analysisError,
    motionCaps: s.motionCaps,
    motionError: s.motionError,
    analysisStaleReason: s.analysisStaleReason,
    motionStaleReason: s.motionStaleReason,
    analysisCheckedAtMs: s.analysisCheckedAtMs,
    motionCheckedAtMs: s.motionCheckedAtMs
  });
};

describe('IntegrationStatusStore', () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  it('Sidebar e tela principal veem o mesmo resultado da mesma atualização', async () => {
    const { store, now } = setup();
    await store.refresh();

    const source = toIntegrationSource(store.getState(), now(), store.refresh);
    const sidebar = toolAvailability(source.analysisEnv?.whisperAvailable, source.analysisError, source.analysisStaleReason);
    const row = rowsFor(store, now()).find((r) => r.id === 'whisper')!;

    expect(sidebar.kind).toBe('available');
    expect(row.state).toBe('ready');
  });

  it('notifica todos os assinantes em uma atualização', async () => {
    const { store } = setup();
    const a = vi.fn();
    const b = vi.fn();
    store.subscribe(a);
    store.subscribe(b);

    await store.refresh();

    expect(a).toHaveBeenCalled();
    expect(b).toHaveBeenCalled();
  });

  it('falha temporária de IPC não apaga leitura válida e marca como desatualizada', async () => {
    const { store, client, now } = setup();
    await store.refresh();

    client.getAnalysis = vi.fn(async () => {
      throw new Error('Timeout IPC');
    });
    await store.refresh();

    const source = toIntegrationSource(store.getState(), now(), store.refresh);
    expect(source.analysisEnv?.whisperAvailable).toBe(true);
    expect(source.analysisError).toBeNull();
    expect(source.analysisStaleReason).toContain('Timeout IPC');

    const sidebar = toolAvailability(source.analysisEnv?.ffmpegAvailable, source.analysisError, source.analysisStaleReason);
    expect(sidebar).toEqual({ kind: 'available', stale: true });

    const row = rowsFor(store, now()).find((r) => r.id === 'ffmpeg')!;
    expect(row.state).toBe('ready');
    expect(row.stale).toBe(true);
    expect(row.detail).toContain('desatualizados');
  });

  it('falha de consulta sem leitura é diferente de ferramenta indisponível', async () => {
    const { store, client, now } = setup();
    client.getAnalysis = vi.fn(async () => {
      throw new Error('Timeout IPC');
    });
    await store.refresh();
    const failed = toIntegrationSource(store.getState(), now(), store.refresh);
    expect(toolAvailability(failed.analysisEnv?.whisperAvailable, failed.analysisError, null).kind).toBe('failed');
    expect(rowsFor(store, now()).find((r) => r.id === 'whisper')!.state).toBe('error');

    client.getAnalysis = vi.fn(async () => env({ whisperAvailable: false }));
    await store.refresh();
    const missing = toIntegrationSource(store.getState(), now(), store.refresh);
    expect(toolAvailability(missing.analysisEnv?.whisperAvailable, missing.analysisError, null).kind).toBe('missing');
    expect(rowsFor(store, now()).find((r) => r.id === 'whisper')!.state).toBe('missing');
  });

  it('resposta antiga que chega depois não sobrescreve a mais nova', async () => {
    const { store, client } = setup();
    const first = deferred<AnalysisEnvironmentDto>();
    const second = deferred<AnalysisEnvironmentDto>();
    client.getAnalysis = vi.fn().mockReturnValueOnce(first.promise).mockReturnValueOnce(second.promise);

    const p1 = store.refresh();
    const p2 = store.refresh();
    second.resolve(env({ ffmpegAvailable: true }));
    await vi.advanceTimersByTimeAsync(0);
    first.resolve(env({ ffmpegAvailable: false }));
    await Promise.all([p1, p2]);

    expect(store.getState().analysis.value?.ffmpegAvailable).toBe(true);
  });

  it('falha antiga que chega depois não substitui diagnóstico recente válido', async () => {
    const { store, client } = setup();
    const first = deferred<AnalysisEnvironmentDto>();
    const second = deferred<AnalysisEnvironmentDto>();
    client.getAnalysis = vi.fn().mockReturnValueOnce(first.promise).mockReturnValueOnce(second.promise);

    const p1 = store.refresh();
    const p2 = store.refresh();
    second.resolve(env());
    await vi.advanceTimersByTimeAsync(0);
    first.reject(new Error('timeout antigo'));
    await Promise.all([p1, p2]);

    expect(store.getState().analysis.error).toBeNull();
    expect(store.getState().analysis.value).not.toBeNull();
  });

  it('não fica preso no erro da primeira carga: tenta de novo sozinho', async () => {
    const { store, client, advance } = setup();
    client.getAnalysis = vi.fn().mockRejectedValueOnce(new Error('Timeout IPC')).mockResolvedValue(env());
    await store.refresh();
    expect(store.getState().analysis.error).toBe('Timeout IPC');

    advance(15_000);
    await vi.advanceTimersByTimeAsync(15_000);

    expect(store.getState().analysis.error).toBeNull();
    expect(store.getState().analysis.value?.whisperAvailable).toBe(true);
    expect(client.getAnalysis).toHaveBeenCalledTimes(2);
  });

  it('atualização após instalação força o host a ignorar o cache', async () => {
    const { store, client } = setup();
    await store.ensureFresh();
    expect(client.getAnalysis).toHaveBeenLastCalledWith(false);

    await store.refresh();
    expect(client.getAnalysis).toHaveBeenLastCalledWith(true);
  });

  it('atualização depois de instalar reflete nas duas áreas', async () => {
    const { store, client, now } = setup();
    client.getAnalysis = vi.fn(async () => env({ whisperAvailable: false }));
    await store.refresh();

    client.getAnalysis = vi.fn(async () => env({ whisperAvailable: true }));
    await store.refresh();

    const s = toIntegrationSource(store.getState(), now(), store.refresh);
    expect(toolAvailability(s.analysisEnv?.whisperAvailable, s.analysisError, s.analysisStaleReason).kind).toBe('available');
    expect(rowsFor(store, now()).find((r) => r.id === 'whisper')!.state).toBe('ready');
  });

  it('reinício com modelo presente: a primeira leitura traz o estado do host', async () => {
    const { store, client, now } = setup();
    client.getAnalysis = vi.fn(async () => env({ whisperAvailable: true, whisperModel: 'large-v3' }));
    await store.ensureFresh();

    expect(rowsFor(store, now()).find((r) => r.id === 'whisper')!.detail).toContain('large-v3');
  });

  it('ensureFresh não repete consulta recente nem concorrente', async () => {
    const { store, client, advance } = setup();
    await store.ensureFresh();
    await store.ensureFresh();
    expect(client.getAnalysis).toHaveBeenCalledTimes(1);

    advance(61_000);
    await store.ensureFresh();
    expect(client.getAnalysis).toHaveBeenCalledTimes(2);
  });

  it('indica o horário da verificação e marca leitura antiga', async () => {
    const { store, advance, now } = setup();
    await store.refresh();

    const fresh = rowsFor(store, now()).find((r) => r.id === 'ffmpeg')!;
    expect(fresh.source).toContain('verificado às');
    expect(fresh.stale).toBeUndefined();

    advance(STATUS_STALE_AFTER_MS + 1);
    expect(slotStaleReason(store.getState().analysis, now())).toBe('leitura antiga');
    expect(rowsFor(store, now()).find((r) => r.id === 'ffmpeg')!.stale).toBe(true);
  });

  it('não marca disponível sem evidência do host', () => {
    const { store, now } = setup();
    const s = toIntegrationSource(store.getState(), now(), store.refresh);
    expect(toolAvailability(s.analysisEnv?.whisperAvailable, s.analysisError, s.analysisStaleReason).kind).toBe('unverified');
    expect(rowsFor(store, now()).find((r) => r.id === 'whisper')!.state).toBe('unverified');
  });
});
