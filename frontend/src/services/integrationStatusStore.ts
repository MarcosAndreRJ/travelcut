import type { AnalysisEnvironmentDto, MotionCapabilitiesDto } from '../types/ipc';
import { ipc } from './ipcBridge';

// Estado autoritativo das capacidades do host: Sidebar, Painel, Configurações e Verificação leem daqui.
// Regras: resposta antiga nunca vence a mais nova; falha de consulta não apaga leitura válida (ela fica
// marcada como desatualizada); falha de consulta é diferente de ferramenta indisponível.

export interface CapabilitySlot<T> {
  /** Última leitura válida do host. */
  value: T | null;
  /** Falha da consulta mais recente; null se ela teve sucesso. */
  error: string | null;
  /** Instante da última leitura válida. */
  checkedAtMs: number | null;
}

export interface IntegrationStatusState {
  analysis: CapabilitySlot<AnalysisEnvironmentDto>;
  motion: CapabilitySlot<MotionCapabilitiesDto>;
  loading: boolean;
}

export interface IntegrationStatusClient {
  /** refresh=true pede ao host para ignorar o cache de ambiente. */
  getAnalysis(refresh: boolean): Promise<AnalysisEnvironmentDto>;
  getMotion(): Promise<MotionCapabilitiesDto>;
}

type SlotKey = 'analysis' | 'motion';

/** Reconsulta ao navegar só se a leitura for mais antiga que isto. */
const FRESH_FOR_MS = 60_000;
const RETRY_MIN_MS = 15_000;
const RETRY_MAX_MS = 120_000;

/** A partir daqui a leitura aparece como desatualizada mesmo sem falha. */
export const STATUS_STALE_AFTER_MS = 5 * 60_000;

const emptySlot = <T,>(): CapabilitySlot<T> => ({ value: null, error: null, checkedAtMs: null });

/** Motivo para tratar uma leitura válida como desatualizada; null se está atual ou não há leitura. */
export function slotStaleReason<T>(slot: CapabilitySlot<T>, nowMs: number): string | null {
  if (slot.value === null) return null;
  if (slot.error) return `a última consulta falhou (${slot.error})`;
  if (slot.checkedAtMs !== null && nowMs - slot.checkedAtMs > STATUS_STALE_AFTER_MS) return 'leitura antiga';
  return null;
}

export class IntegrationStatusStore {
  private state: IntegrationStatusState = { analysis: emptySlot(), motion: emptySlot(), loading: false };
  private readonly listeners = new Set<() => void>();
  private readonly issued: Record<SlotKey, number> = { analysis: 0, motion: 0 };
  private readonly applied: Record<SlotKey, number> = { analysis: 0, motion: 0 };
  private inFlight = 0;
  private retryTimer: ReturnType<typeof setTimeout> | null = null;
  private retryDelayMs = RETRY_MIN_MS;

  constructor(
    private readonly client: IntegrationStatusClient,
    private readonly now: () => number = () => Date.now()
  ) {}

  getState = (): IntegrationStatusState => this.state;

  subscribe = (listener: () => void): (() => void) => {
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  };

  /** Reconsulta forçada: botão "Atualizar", instalação, configuração e download concluído. */
  refresh = (): Promise<void> => this.run(true);

  /** Consulta só se não houver leitura recente nem consulta em andamento. */
  ensureFresh = (): Promise<void> => {
    const { analysis, motion } = this.state;
    const oldest = Math.min(analysis.checkedAtMs ?? 0, motion.checkedAtMs ?? 0);
    if (this.inFlight > 0 || this.now() - oldest < FRESH_FOR_MS) return Promise.resolve();
    return this.run(false);
  };

  private async run(forceHostRefresh: boolean): Promise<void> {
    this.clearRetry();
    this.inFlight += 1;
    this.setState({ ...this.state, loading: true });

    await Promise.all([
      this.query('analysis', () => this.client.getAnalysis(forceHostRefresh)),
      this.query('motion', () => this.client.getMotion())
    ]);

    this.inFlight -= 1;
    this.setState({ ...this.state, loading: this.inFlight > 0 });

    if (this.inFlight === 0) this.scheduleRetryIfNeeded();
  }

  private async query<K extends SlotKey>(
    key: K,
    fetch: () => Promise<NonNullable<IntegrationStatusState[K]['value']>>
  ): Promise<void> {
    const seq = ++this.issued[key];
    try {
      const value = await fetch();
      if (seq < this.applied[key]) return;
      this.applied[key] = seq;
      this.setSlot(key, { value, error: null, checkedAtMs: this.now() } as IntegrationStatusState[K]);
    } catch (err: unknown) {
      if (seq < this.applied[key]) return;
      this.applied[key] = seq;
      console.error(`Falha ao consultar capacidade '${key}':`, err);
      const error = err instanceof Error ? err.message : String(err);
      this.setSlot(key, { ...this.state[key], error });
    }
  }

  /** A consulta falhou: tenta de novo sozinha, para nenhuma tela ficar presa no erro da primeira carga. */
  private scheduleRetryIfNeeded(): void {
    if (!this.state.analysis.error && !this.state.motion.error) {
      this.retryDelayMs = RETRY_MIN_MS;
      return;
    }
    const delay = this.retryDelayMs;
    this.retryDelayMs = Math.min(RETRY_MAX_MS, delay * 2);
    this.retryTimer = setTimeout(() => void this.run(false), delay);
  }

  private clearRetry(): void {
    if (this.retryTimer) clearTimeout(this.retryTimer);
    this.retryTimer = null;
  }

  private setSlot<K extends SlotKey>(key: K, slot: IntegrationStatusState[K]): void {
    this.setState({ ...this.state, [key]: slot });
  }

  private setState(next: IntegrationStatusState): void {
    this.state = next;
    this.listeners.forEach((listener) => listener());
  }
}

export const integrationStatusStore = new IntegrationStatusStore({
  getAnalysis: (refresh) => ipc.getAnalysisEnvironment(refresh),
  getMotion: () => ipc.getMotionCapabilities()
});
