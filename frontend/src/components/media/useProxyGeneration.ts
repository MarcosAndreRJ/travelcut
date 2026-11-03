import { useCallback, useEffect, useRef, useState } from 'react';
import type { ProxyJobStatusDto } from '../../types/ipc';
import { getProxyStatus, previewErrorMessage, startProxy } from '../../services/previewApi';

export type ProxyPhase = 'idle' | 'starting' | 'running' | 'done' | 'failed' | 'unavailable';

export interface ProxyGeneration {
  phase: ProxyPhase;
  message: string | null;
  busy: boolean;
  start: () => void;
}

const POLL_INTERVAL_MS = 1000;
const ACTIVE_STATES: ReadonlySet<ProxyJobStatusDto['state']> = new Set(['queued', 'running']);

const wait = (ms: number): Promise<void> => new Promise((resolve) => window.setTimeout(resolve, ms));

/**
 * Gera proxy de UMA mídia via media.proxy.start e faz polling de media.proxy.status até um estado
 * terminal. Ao terminar com sucesso chama onFinished (o chamador recarrega a fonte). Cancela o
 * polling ao desmontar ou ao trocar de mídia.
 */
export function useProxyGeneration(projectId: string, mediaId: string, onFinished: () => void): ProxyGeneration {
  const [phase, setPhase] = useState<ProxyPhase>('idle');
  const [message, setMessage] = useState<string | null>(null);
  const runRef = useRef<number>(0);
  const phaseRef = useRef<ProxyPhase>('idle');
  const onFinishedRef = useRef(onFinished);
  onFinishedRef.current = onFinished;

  const updatePhase = useCallback((next: ProxyPhase) => {
    phaseRef.current = next;
    setPhase(next);
  }, []);

  // Trocar de mídia ou de projeto invalida qualquer job em andamento na tela.
  useEffect(() => {
    runRef.current += 1;
    updatePhase('idle');
    setMessage(null);
  }, [projectId, mediaId, updatePhase]);

  // Desmontagem cancela o polling (a execução vira obsoleta).
  useEffect(() => {
    return () => {
      runRef.current += 1;
    };
  }, []);

  const start = useCallback(() => {
    if (phaseRef.current === 'starting' || phaseRef.current === 'running') return;
    const run = runRef.current + 1;
    runRef.current = run;
    const isCurrent = () => runRef.current === run;

    updatePhase('starting');
    setMessage(null);

    void (async () => {
      try {
        const { jobId } = await startProxy(projectId, [mediaId]);
        if (!isCurrent()) return;
        updatePhase('running');

        for (;;) {
          const status = await getProxyStatus(projectId, jobId);
          if (!isCurrent()) return;

          if (ACTIVE_STATES.has(status.state)) {
            await wait(POLL_INTERVAL_MS);
            if (!isCurrent()) return;
            continue;
          }

          if (status.state === 'completed' || status.state === 'completed_with_errors') {
            updatePhase('done');
            setMessage(status.state === 'completed' ? 'Proxy gerado.' : 'Proxy concluído com erros.');
            onFinishedRef.current();
            return;
          }
          if (status.state === 'ffmpeg_unavailable') {
            updatePhase('unavailable');
            setMessage('FFmpeg não encontrado: não é possível gerar proxy neste ambiente.');
            return;
          }
          updatePhase('failed');
          setMessage(
            status.state === 'canceled' ? 'A geração do proxy foi cancelada.' : 'A geração do proxy falhou.'
          );
          return;
        }
      } catch (err: unknown) {
        if (!isCurrent()) return;
        updatePhase('failed');
        setMessage(previewErrorMessage(err));
      }
    })();
  }, [projectId, mediaId, updatePhase]);

  return {
    phase,
    message,
    busy: phase === 'starting' || phase === 'running',
    start
  };
}
