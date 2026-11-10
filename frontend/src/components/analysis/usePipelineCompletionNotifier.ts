import { useEffect } from 'react';
import { ipc } from '../../services/ipcBridge';
import { notifyGeneral } from '../../services/notificationService';
import {
  COMPLETION_POLL_MS,
  PIPELINE_STARTED_EVENT,
  completionNotice,
  shouldNotifyCompletion,
  shouldPollCompletion
} from '../../services/pipelineCompletion';

/**
 * Ponto único de notificação de fim de pipeline no shell (P14).
 * - Primeira leitura do projeto só estabelece a linha de base (não notifica).
 * - Polling leve somente enquanto há job ativo ou lote pendente; ocioso, não consulta.
 * - Um start feito pela UI dispara PIPELINE_STARTED_EVENT para reconsultar imediatamente.
 */
export const usePipelineCompletionNotifier = (projectId: string | null): void => {
  useEffect(() => {
    if (!projectId) return;
    let active = true;
    let inFlight = false;
    let pollingNow = false;
    let lastToken: string | null | undefined = undefined;
    let timer: ReturnType<typeof setTimeout> | undefined;

    const clearTimer = (): void => {
      if (timer !== undefined) {
        clearTimeout(timer);
        timer = undefined;
      }
    };

    const schedule = (): void => {
      clearTimer();
      timer = setTimeout(() => void tick(), COMPLETION_POLL_MS);
    };

    const tick = async (): Promise<void> => {
      if (!active || inFlight) return;
      clearTimer();
      inFlight = true;
      try {
        const status = await ipc.getPipelineStatus(projectId);
        if (!active) return;
        const completion = status.completion ?? null;
        if (shouldNotifyCompletion(lastToken, completion) && completion) {
          const notice = completionNotice(completion.outcome, status.postAnalysis);
          notifyGeneral(notice.title, notice.message, notice.type);
        }
        lastToken = completion?.token ?? null;
        pollingNow = shouldPollCompletion(status);
        if (pollingNow) schedule();
      } catch {
        // Falha transitória: tenta de novo só se havia acompanhamento ativo; senão aguarda o próximo start.
        if (active && pollingNow) schedule();
      } finally {
        inFlight = false;
      }
    };

    const onStarted = (): void => {
      if (!inFlight) void tick();
    };

    void tick();
    window.addEventListener(PIPELINE_STARTED_EVENT, onStarted);
    return () => {
      active = false;
      clearTimer();
      window.removeEventListener(PIPELINE_STARTED_EVENT, onStarted);
    };
  }, [projectId]);
};
