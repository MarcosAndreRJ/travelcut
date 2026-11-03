import { useCallback, useEffect, useRef, useState } from 'react';
import { ipc, IpcError } from '../../services/ipcBridge';
import type { PipelineStatusDto } from '../../types/ipc';
import { errorMessage, isPipelineActive } from './analysisFormat';

export const PIPELINE_POLL_INTERVAL_MS = 1000;
/** Tentativas aguardando o job aparecer no status após um start (evita polling infinito). */
const MAX_WAIT_ATTEMPTS = 15;

interface Tracking {
  jobId: string | null;
  sawActive: boolean;
  waitAttempts: number;
}

export interface StartAnalysisOptions {
  mediaIds?: string[] | null;
  force: boolean;
}

export interface UsePipelineResult {
  status: PipelineStatusDto | null;
  statusError: string | null;
  actionError: string | null;
  isStarting: boolean;
  isCanceling: boolean;
  isActive: boolean;
  refresh: () => void;
  startAnalysis: (options: StartAnalysisOptions) => Promise<void>;
  cancelAnalysis: () => Promise<void>;
}

/**
 * Acompanha pipeline.status do projeto.
 * - Consulta uma vez ao abrir; faz polling (~1 s) enquanto queued/running.
 * - Ao terminar um job acompanhado (ou que estava ativo), chama onFinished.
 */
export function usePipeline(projectId: string | null, onFinished: () => void): UsePipelineResult {
  const [status, setStatus] = useState<PipelineStatusDto | null>(null);
  const [statusError, setStatusError] = useState<string | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);
  const [isStarting, setIsStarting] = useState(false);
  const [isCanceling, setIsCanceling] = useState(false);
  const [pollToken, setPollToken] = useState(0);
  const trackRef = useRef<Tracking | null>(null);
  const onFinishedRef = useRef(onFinished);

  useEffect(() => {
    onFinishedRef.current = onFinished;
  }, [onFinished]);

  // Troca de projeto descarta o estado do anterior.
  useEffect(() => {
    trackRef.current = null;
    setStatus(null);
    setStatusError(null);
    setActionError(null);
  }, [projectId]);

  useEffect(() => {
    if (!projectId) return;
    let cancelled = false;
    let timer: ReturnType<typeof setTimeout> | undefined;

    const tick = async (): Promise<void> => {
      let next: PipelineStatusDto;
      try {
        next = await ipc.getPipelineStatus(projectId);
      } catch (err) {
        if (!cancelled) {
          setStatusError(errorMessage(err));
          trackRef.current = null;
        }
        return;
      }
      if (cancelled) return;
      setStatus(next);
      setStatusError(null);

      const prev = trackRef.current;
      if (isPipelineActive(next.state)) {
        trackRef.current = { jobId: next.jobId ?? prev?.jobId ?? null, sawActive: true, waitAttempts: 0 };
        timer = setTimeout(() => void tick(), PIPELINE_POLL_INTERVAL_MS);
        return;
      }

      if (prev && (prev.sawActive || (prev.jobId !== null && next.jobId === prev.jobId))) {
        trackRef.current = null;
        onFinishedRef.current();
        return;
      }

      if (prev) {
        const waitAttempts = prev.waitAttempts + 1;
        if (waitAttempts < MAX_WAIT_ATTEMPTS) {
          trackRef.current = { ...prev, waitAttempts };
          timer = setTimeout(() => void tick(), PIPELINE_POLL_INTERVAL_MS);
        } else {
          trackRef.current = null;
        }
      }
    };

    void tick();
    return () => {
      cancelled = true;
      if (timer !== undefined) clearTimeout(timer);
    };
  }, [projectId, pollToken]);

  const refresh = useCallback(() => setPollToken((t) => t + 1), []);

  const startAnalysis = useCallback(
    async ({ mediaIds, force }: StartAnalysisOptions) => {
      if (!projectId) return;
      setIsStarting(true);
      setActionError(null);
      try {
        const res = await ipc.startPipeline({
          projectId,
          mediaIds: mediaIds ?? null,
          stages: null,
          language: null,
          force
        });
        trackRef.current = { jobId: res.jobId, sawActive: false, waitAttempts: 0 };
        setPollToken((t) => t + 1);
      } catch (err) {
        if (err instanceof IpcError && err.code === 'PIPELINE_IN_PROGRESS') {
          setActionError('Já existe uma análise em andamento. Aguarde o término ou cancele a execução atual.');
          setPollToken((t) => t + 1);
        } else {
          setActionError(errorMessage(err));
        }
      } finally {
        setIsStarting(false);
      }
    },
    [projectId]
  );

  const cancelAnalysis = useCallback(async () => {
    const jobId = status?.jobId;
    if (!jobId) return;
    setIsCanceling(true);
    setActionError(null);
    try {
      await ipc.cancelPipeline(jobId);
      setPollToken((t) => t + 1);
    } catch (err) {
      setActionError(errorMessage(err));
    } finally {
      setIsCanceling(false);
    }
  }, [status?.jobId]);

  return {
    status,
    statusError,
    actionError,
    isStarting,
    isCanceling,
    isActive: isPipelineActive(status?.state),
    refresh,
    startAnalysis,
    cancelAnalysis
  };
}
