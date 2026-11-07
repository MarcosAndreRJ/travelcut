import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { ToolInstallProgressDto } from '../../types/ipc';
import { IpcError } from '../ipcBridge';
import {
  estimateEtaSeconds,
  measureSpeed,
  ModelDownloadTracker,
  type ModelDownloadClient
} from '../modelDownloadTracker';

const progress = (over: Partial<ToolInstallProgressDto> = {}): ToolInstallProgressDto => ({
  jobId: 'job-1',
  toolKey: 'whisper',
  stage: 'Baixando Modelo',
  progressPercent: 10,
  currentMessage: 'Baixando...',
  outputLogs: [],
  canCancel: true,
  isCompleted: false,
  isSuccess: false,
  isFailed: false,
  isCanceled: false,
  ...over
});

const done = (over: Partial<ToolInstallProgressDto>): ToolInstallProgressDto =>
  progress({ isCompleted: true, canCancel: false, ...over });

function setup(responses: Array<ToolInstallProgressDto | null | Error>) {
  let nowMs = 0;
  const client: ModelDownloadClient = {
    start: vi.fn(async () => ({ jobId: 'job-1' })),
    getProgress: vi.fn(async () => {
      const next = responses.length > 1 ? responses.shift()! : responses[0];
      if (next instanceof Error) throw next;
      return next;
    }),
    cancel: vi.fn(async () => ({ canceled: true }))
  };
  const tracker = new ModelDownloadTracker(client, () => nowMs, 800);
  return {
    client,
    tracker,
    tick: async (ms = 800) => {
      nowMs += ms;
      await vi.advanceTimersByTimeAsync(ms);
    }
  };
}

describe('ModelDownloadTracker', () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  it('passa por confirmação, progresso e conclusão, avisando o sucesso uma única vez', async () => {
    const { tracker, tick } = setup([
      progress({ bytesDownloaded: 100, totalBytes: 1000 }),
      done({ isSuccess: true, stage: 'Concluído', progressPercent: 100 })
    ]);
    const onSuccess = vi.fn();
    tracker.onSuccess(onSuccess);

    const started = tracker.start('large-v3');
    expect(tracker.getState().phase).toBe('confirming');
    await started;
    expect(tracker.getState().phase).toBe('running');
    expect(tracker.getState().progress?.bytesDownloaded).toBe(100);

    await tick();
    expect(tracker.getState().phase).toBe('success');
    expect(onSuccess).toHaveBeenCalledTimes(1);

    await tick(5000);
    expect(onSuccess).toHaveBeenCalledTimes(1);
  });

  it('mantém o resultado visível até ser dispensado (estado fora das telas sobrevive à navegação)', async () => {
    const { tracker } = setup([done({ isSuccess: true, stage: 'Concluído' })]);
    await tracker.start('large-v3');

    // Uma tela nova que assina depois da conclusão enxerga o mesmo resultado.
    const seen = tracker.getState();
    expect(seen.phase).toBe('success');

    tracker.dismiss();
    expect(tracker.getState().phase).toBe('idle');
  });

  it('não dispensa nem reinicia um download ativo', async () => {
    const { tracker, client } = setup([progress()]);
    await tracker.start('large-v3');

    tracker.dismiss();
    expect(tracker.getState().phase).toBe('running');

    await tracker.start('large-v3');
    expect(client.start).toHaveBeenCalledTimes(1);
  });

  it('distingue cancelamento solicitado de cancelamento confirmado', async () => {
    const { tracker, client, tick } = setup([
      progress(),
      progress(),
      done({ isCanceled: true, stage: 'Cancelado' })
    ]);
    await tracker.start('large-v3');

    await tracker.cancel();
    expect(client.cancel).toHaveBeenCalledWith('job-1');
    expect(tracker.getState().phase).toBe('canceling');

    await tick();
    expect(tracker.getState().phase).toBe('canceling');

    await tick();
    expect(tracker.getState().phase).toBe('canceled');
  });

  it('volta a "em andamento" com aviso se o pedido de cancelamento falhar', async () => {
    const { tracker, client } = setup([progress()]);
    client.cancel = vi.fn(async () => {
      throw new Error('canal fechado');
    });
    await tracker.start('large-v3');

    await tracker.cancel();

    expect(tracker.getState().phase).toBe('running');
    expect(tracker.getState().errorMessage).toContain('canal fechado');
  });

  it('propaga erro real de download', async () => {
    const { tracker } = setup([done({ isFailed: true, stage: 'Erro', errorMessage: 'Hash inválido' })]);
    await tracker.start('large-v3');

    expect(tracker.getState().phase).toBe('failed');
    expect(tracker.getState().errorMessage).toBe('Hash inválido');
  });

  it('falha temporária de IPC não encerra o job e continua consultando', async () => {
    const { tracker, tick } = setup([
      new Error('timeout'),
      new Error('timeout'),
      progress({ bytesDownloaded: 5 })
    ]);
    await tracker.start('large-v3');
    expect(tracker.getState().phase).toBe('running');
    expect(tracker.getState().pollFailures).toBe(1);

    await tick();
    expect(tracker.getState().pollFailures).toBe(2);

    await tick();
    expect(tracker.getState().pollFailures).toBe(0);
    expect(tracker.getState().progress?.bytesDownloaded).toBe(5);
  });

  it('job desconhecido pelo host vira estado "lost", sem fingir sucesso', async () => {
    const { tracker } = setup([null]);
    await tracker.start('large-v3');

    expect(tracker.getState().phase).toBe('lost');
    expect(tracker.getState().errorMessage).toContain('não reconhece');
  });

  it('recusa no diálogo nativo é cancelamento, não falha', async () => {
    const { tracker, client } = setup([progress()]);
    client.start = vi.fn(async () => {
      throw new IpcError('Download recusado.', 'USER_CANCELED');
    });

    await tracker.start('large-v3');

    expect(tracker.getState().phase).toBe('canceled');
    expect(client.getProgress).not.toHaveBeenCalled();
  });

  it('falha ao iniciar aparece como falha e permite nova tentativa', async () => {
    const { tracker, client } = setup([done({ isSuccess: true })]);
    client.start = vi.fn().mockRejectedValueOnce(new Error('Já existe um download')).mockResolvedValue({ jobId: 'job-1' });

    await tracker.start('large-v3');
    expect(tracker.getState().phase).toBe('failed');

    await tracker.start('large-v3');
    expect(tracker.getState().phase).toBe('success');
  });

  it('reabertura com modelo já presente: o host valida e conclui sem bytes de transferência', async () => {
    const { tracker } = setup([
      done({
        isSuccess: true,
        stage: 'Concluído',
        progressPercent: 100,
        currentMessage: 'Modelo large-v3 já está instalado e íntegro com hash SHA-256 verificado!'
      })
    ]);

    await tracker.start('large-v3');

    const state = tracker.getState();
    expect(state.phase).toBe('success');
    expect(state.progress?.currentMessage).toContain('SHA-256');
    expect(state.speedBytesPerSecond).toBeNull();
    expect(state.etaSeconds).toBeNull();
  });

  it('só mede velocidade e tempo restante com amostras reais suficientes', async () => {
    const { tracker, tick } = setup([
      progress({ bytesDownloaded: 0, totalBytes: 1000 }),
      progress({ bytesDownloaded: 100, totalBytes: 1000 }),
      progress({ bytesDownloaded: 200, totalBytes: 1000 }),
      progress({ bytesDownloaded: 300, totalBytes: 1000 })
    ]);
    await tracker.start('large-v3');
    expect(tracker.getState().speedBytesPerSecond).toBeNull();

    await tick(1000);
    expect(tracker.getState().speedBytesPerSecond).toBeNull();

    await tick(1000);
    await tick(1000);
    const state = tracker.getState();
    expect(state.speedBytesPerSecond).toBe(100);
    expect(state.etaSeconds).toBe(7);
  });
});

describe('medidas de transferência', () => {
  it('não calcula velocidade sem avanço de bytes ou com intervalo curto', () => {
    expect(measureSpeed([])).toBeNull();
    expect(measureSpeed([{ atMs: 0, bytes: 10 }, { atMs: 5000, bytes: 10 }])).toBeNull();
    expect(measureSpeed([{ atMs: 0, bytes: 0 }, { atMs: 500, bytes: 1000 }])).toBeNull();
    expect(measureSpeed([{ atMs: 0, bytes: 0 }, { atMs: 2000, bytes: 1000 }])).toBe(500);
  });

  it('não estima tempo restante sem total ou velocidade', () => {
    expect(estimateEtaSeconds(100, null, 50)).toBeNull();
    expect(estimateEtaSeconds(100, 1000, null)).toBeNull();
    expect(estimateEtaSeconds(1000, 1000, 50)).toBeNull();
    expect(estimateEtaSeconds(100, 1000, 50)).toBe(18);
  });
});
