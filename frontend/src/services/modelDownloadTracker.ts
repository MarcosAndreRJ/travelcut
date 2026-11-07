import type { StartModelDownloadRequest, ToolInstallProgressDto } from '../types/ipc';
import { ipc, IpcError } from './ipcBridge';

// Acompanhamento do download do modelo durante a sessão do aplicativo (fora do ciclo de vida das telas):
// o job continua visível ao navegar. Não há retomada após fechar o app: o backend mantém os jobs só em memória;
// ao reabrir, o modelo presente em disco é identificado pelo diagnóstico e validado por SHA-256 ao pedir o download.

export type ModelDownloadPhase =
  | 'idle'
  | 'confirming' // aguardando a confirmação nativa do host
  | 'running'
  | 'canceling' // cancelamento solicitado, ainda não confirmado pelo host
  | 'success'
  | 'failed'
  | 'canceled'
  | 'lost'; // o host deixou de reconhecer o job

export interface ModelDownloadState {
  phase: ModelDownloadPhase;
  jobId: string | null;
  modelName: string | null;
  progress: ToolInstallProgressDto | null;
  /** Bytes por segundo medidos entre amostras reais; null enquanto não houver medida. */
  speedBytesPerSecond: number | null;
  /** Segundos restantes; null sem tamanho total ou sem velocidade medida. */
  etaSeconds: number | null;
  /** Consultas consecutivas sem resposta do host (não encerra o job). */
  pollFailures: number;
  /** Instante da última resposta real do host. */
  lastResponseAtMs: number | null;
  errorMessage: string | null;
}

export interface ModelDownloadClient {
  start(request: StartModelDownloadRequest): Promise<{ jobId: string }>;
  getProgress(jobId: string): Promise<ToolInstallProgressDto | null>;
  cancel(jobId: string): Promise<{ canceled: boolean }>;
}

export interface TransferSample {
  atMs: number;
  bytes: number;
}

const IDLE: ModelDownloadState = {
  phase: 'idle',
  jobId: null,
  modelName: null,
  progress: null,
  speedBytesPerSecond: null,
  etaSeconds: null,
  pollFailures: 0,
  lastResponseAtMs: null,
  errorMessage: null
};

const POLL_INTERVAL_MS = 800;
const SPEED_WINDOW_MS = 10_000;
const MIN_SPEED_SPAN_MS = 2_000;

export const isModelDownloadActive = (phase: ModelDownloadPhase): boolean =>
  phase === 'confirming' || phase === 'running' || phase === 'canceling';

/** Velocidade pela janela de amostras; null sem intervalo suficiente ou sem avanço real de bytes. */
export function measureSpeed(samples: readonly TransferSample[]): number | null {
  if (samples.length < 2) return null;
  const first = samples[0];
  const last = samples[samples.length - 1];
  const spanMs = last.atMs - first.atMs;
  const delta = last.bytes - first.bytes;
  if (spanMs < MIN_SPEED_SPAN_MS || delta <= 0) return null;
  return (delta / spanMs) * 1000;
}

/** Tempo restante só com total conhecido e velocidade medida. */
export function estimateEtaSeconds(
  bytes: number | null | undefined,
  total: number | null | undefined,
  speedBytesPerSecond: number | null
): number | null {
  if (bytes == null || total == null || total <= 0 || bytes >= total) return null;
  if (speedBytesPerSecond == null || speedBytesPerSecond <= 0) return null;
  return Math.ceil((total - bytes) / speedBytesPerSecond);
}

export class ModelDownloadTracker {
  private state: ModelDownloadState = IDLE;
  private readonly listeners = new Set<() => void>();
  private readonly successListeners = new Set<() => void>();
  private timer: ReturnType<typeof setTimeout> | null = null;
  private samples: TransferSample[] = [];

  constructor(
    private readonly client: ModelDownloadClient,
    private readonly now: () => number = () => Date.now(),
    private readonly pollIntervalMs: number = POLL_INTERVAL_MS
  ) {}

  getState = (): ModelDownloadState => this.state;

  subscribe = (listener: () => void): (() => void) => {
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  };

  /** Chamado uma vez por download concluído com sucesso (ex.: reconsultar o diagnóstico). */
  onSuccess(listener: () => void): () => void {
    this.successListeners.add(listener);
    return () => this.successListeners.delete(listener);
  }

  async start(modelName: string): Promise<void> {
    if (isModelDownloadActive(this.state.phase)) return;

    this.samples = [];
    this.setState({ ...IDLE, phase: 'confirming', modelName });

    try {
      const { jobId } = await this.client.start({ modelName, confirmed: true });
      this.setState({ ...this.state, phase: 'running', jobId });
      await this.poll();
    } catch (err: unknown) {
      const message = err instanceof Error ? err.message : String(err);
      // Recusa no diálogo nativo não é falha técnica.
      const refused = err instanceof IpcError && err.code === 'USER_CANCELED';
      this.setState({ ...this.state, phase: refused ? 'canceled' : 'failed', errorMessage: message });
    }
  }

  async cancel(): Promise<void> {
    const { jobId, phase } = this.state;
    if (!jobId || phase !== 'running') return;

    this.setState({ ...this.state, phase: 'canceling', errorMessage: null });
    try {
      const { canceled } = await this.client.cancel(jobId);
      if (!canceled && this.state.phase === 'canceling') {
        this.setState({ ...this.state, phase: 'running', errorMessage: 'O host não aceitou o cancelamento neste momento.' });
      }
    } catch (err: unknown) {
      if (this.state.phase === 'canceling') {
        const message = err instanceof Error ? err.message : String(err);
        this.setState({ ...this.state, phase: 'running', errorMessage: `Falha ao solicitar o cancelamento: ${message}` });
      }
    }
  }

  /** Fecha um resultado terminal. Download ativo não pode ser dispensado. */
  dismiss(): void {
    if (isModelDownloadActive(this.state.phase)) return;
    this.clearTimer();
    this.setState(IDLE);
  }

  private async poll(): Promise<void> {
    const { jobId } = this.state;
    if (!jobId) return;

    try {
      const progress = await this.client.getProgress(jobId);
      if (this.state.jobId !== jobId) return;

      if (!progress) {
        this.setState({
          ...this.state,
          phase: 'lost',
          errorMessage: 'O host não reconhece mais este download. Confira o diagnóstico do Whisper.'
        });
        return;
      }
      this.applyProgress(progress);
    } catch {
      // Falha temporária de IPC não encerra o job: o estado real continua sendo o do host.
      if (this.state.jobId !== jobId) return;
      this.setState({ ...this.state, pollFailures: this.state.pollFailures + 1 });
    }

    if (isModelDownloadActive(this.state.phase)) {
      this.clearTimer();
      this.timer = setTimeout(() => void this.poll(), this.pollIntervalMs);
    }
  }

  private applyProgress(progress: ToolInstallProgressDto): void {
    const atMs = this.now();
    const bytes = progress.bytesDownloaded;
    if (typeof bytes === 'number') {
      this.samples.push({ atMs, bytes });
      this.samples = this.samples.filter((s) => atMs - s.atMs <= SPEED_WINDOW_MS);
    }

    const transferring = !progress.isCompleted && typeof bytes === 'number';
    const speed = transferring ? measureSpeed(this.samples) : null;
    const base = {
      ...this.state,
      progress,
      pollFailures: 0,
      lastResponseAtMs: atMs,
      speedBytesPerSecond: speed,
      etaSeconds: transferring ? estimateEtaSeconds(bytes, progress.totalBytes, speed) : null
    };

    if (progress.isCanceled) {
      this.setState({ ...base, phase: 'canceled', errorMessage: null });
    } else if (progress.isFailed) {
      this.setState({ ...base, phase: 'failed', errorMessage: progress.errorMessage ?? progress.currentMessage });
    } else if (progress.isSuccess) {
      this.setState({ ...base, phase: 'success', errorMessage: null });
      this.successListeners.forEach((listener) => listener());
    } else {
      this.setState({ ...base, phase: this.state.phase === 'canceling' ? 'canceling' : 'running' });
    }
  }

  private clearTimer(): void {
    if (this.timer) clearTimeout(this.timer);
    this.timer = null;
  }

  private setState(next: ModelDownloadState): void {
    this.state = next;
    this.listeners.forEach((listener) => listener());
  }
}

export const modelDownloadTracker = new ModelDownloadTracker({
  start: (request) => ipc.startModelDownload(request),
  getProgress: (jobId) => ipc.getModelDownloadProgress(jobId),
  cancel: (jobId) => ipc.cancelModelDownload(jobId)
});
