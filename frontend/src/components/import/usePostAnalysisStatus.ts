import { useEffect, useState } from 'react';
import { ipc } from '../../services/ipcBridge';
import type { PostAnalysisStatusDto } from '../../types/ipc';

/** Intervalo do polling leve de pipeline.status.postAnalysis. */
export const POST_ANALYSIS_POLL_MS = 2000;
/** Limite de consultas na janela de transição (idle/ausente) antes de desistir do polling. */
export const POST_ANALYSIS_TRANSITION_MAX_POLLS = 30;

export const isTerminalPostAnalysis = (status: PostAnalysisStatusDto | null): boolean =>
  status !== null &&
  (status.state === 'completed' ||
    status.state === 'completed_with_issues' ||
    status.state === 'failed' ||
    status.state === 'skipped');

/** Janela em transição: pipeline concluído, orquestrador ainda não iniciou (idle ou ausente). */
export const isPostAnalysisInTransition = (status: PostAnalysisStatusDto | null): boolean =>
  status === null || status.state === 'idle';

export interface PostAnalysisPollerDeps {
  fetchStatus: () => Promise<PostAnalysisStatusDto | null>;
  onStatus: (status: PostAnalysisStatusDto | null) => void;
  setTimer: (callback: () => void, ms: number) => unknown;
  clearTimer: (handle: unknown) => void;
}

/**
 * Controlador puro do polling de postAnalysis. Retorna a função de parada.
 * Para ao atingir estado terminal, na falha de consulta ou ao ser parado (desmontagem).
 * Na janela de transição (idle/ausente) limita o número de consultas.
 */
export const startPostAnalysisPolling = (deps: PostAnalysisPollerDeps): (() => void) => {
  let active = true;
  let handle: unknown = undefined;
  let transitionPolls = 0;

  const schedule = (): void => {
    handle = deps.setTimer(() => {
      void tick();
    }, POST_ANALYSIS_POLL_MS);
  };

  const tick = async (): Promise<void> => {
    try {
      const next = await deps.fetchStatus();
      if (!active) return;
      deps.onStatus(next);
      if (isTerminalPostAnalysis(next)) return;
      if (isPostAnalysisInTransition(next)) {
        transitionPolls += 1;
        if (transitionPolls >= POST_ANALYSIS_TRANSITION_MAX_POLLS) return;
      }
      schedule();
    } catch {
      if (active) deps.onStatus(null);
    }
  };

  void tick();
  return () => {
    active = false;
    if (handle !== undefined) deps.clearTimer(handle);
  };
};

/**
 * Acompanha pipeline.status.postAnalysis enquanto `enabled` (importação terminal).
 * Desabilitar ou desmontar interrompe o polling e limpa o estado.
 */
export const usePostAnalysisStatus = (projectId: string, enabled: boolean): PostAnalysisStatusDto | null => {
  const [status, setStatus] = useState<PostAnalysisStatusDto | null>(null);

  useEffect(() => {
    if (!enabled) {
      setStatus(null);
      return;
    }
    return startPostAnalysisPolling({
      fetchStatus: async () => {
        const res = await ipc.getPipelineStatus(projectId);
        return res.postAnalysis ?? null;
      },
      onStatus: setStatus,
      setTimer: (callback, ms) => setTimeout(callback, ms),
      clearTimer: (handle) => clearTimeout(handle as ReturnType<typeof setTimeout>)
    });
  }, [projectId, enabled]);

  return status;
};
