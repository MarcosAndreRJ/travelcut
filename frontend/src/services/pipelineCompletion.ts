// Regras puras da notificação de fim de ciclo do pipeline (P14). Sem React, sem IPC, sem jsdom.
import type { PipelineCompletionDto, PipelineStatusDto, PostAnalysisStatusDto } from '../types/ipc';
import { isPipelineActive } from '../components/analysis/analysisFormat';

/** Intervalo do polling leve enquanto houver pipeline ativo ou lote pendente. */
export const COMPLETION_POLL_MS = 2000;

/** Evento de janela disparado quando uma análise é iniciada pela UI; reacorda o acompanhamento do shell. */
export const PIPELINE_STARTED_EVENT = 'travelcut:pipeline-started';

export type CompletionOutcome = NonNullable<PipelineCompletionDto['outcome']>;

export interface CompletionNotice {
  title: string;
  message: string;
  type: 'success' | 'error' | 'info';
}

/**
 * Notifica somente quando o ciclo está assentado e o token mudou para um valor novo e não nulo.
 * `previousToken === undefined` = primeira leitura desde que o app/projeto foi aberto: nunca notifica.
 */
export const shouldNotifyCompletion = (
  previousToken: string | null | undefined,
  next: PipelineCompletionDto | null | undefined
): boolean =>
  previousToken !== undefined &&
  next !== null &&
  next !== undefined &&
  next.settled === true &&
  next.token !== null &&
  next.token !== undefined &&
  next.token !== previousToken;

/** Mantém o polling enquanto o ciclo não assentou, há lotes pendentes ou o job está ativo. */
export const shouldPollCompletion = (status: PipelineStatusDto | null | undefined): boolean => {
  if (!status) return false;
  if (isPipelineActive(status.state)) return true;
  const completion = status.completion;
  return completion != null && (!completion.settled || completion.pendingBatches > 0);
};

/** Resumo da pós-análise; null quando não há automação a resumir. */
export const postAnalysisSummary = (postAnalysis: PostAnalysisStatusDto | null | undefined): string | null => {
  if (!postAnalysis || postAnalysis.state === 'skipped' || postAnalysis.state === 'idle') return null;
  return `${postAnalysis.autoCutsApplied} cortes aplicados, ${postAnalysis.pendingReview} pendentes de revisão, ${postAnalysis.proxiesQueued} proxies na fila.`;
};

/** Título, mensagem e tipo da notificação de fim de ciclo conforme o resultado. */
export const completionNotice = (
  outcome: CompletionOutcome | null,
  postAnalysis: PostAnalysisStatusDto | null | undefined
): CompletionNotice => {
  const summary = postAnalysisSummary(postAnalysis);
  switch (outcome) {
    case 'completed':
      return {
        title: 'Análise concluída com sucesso',
        message: summary ?? 'Transcrições e classificação de cenas disponíveis para revisão.',
        type: 'success'
      };
    case 'completed_with_issues':
      return {
        title: 'Análise concluída com pendências',
        message: summary ?? 'Há itens que pedem atenção na revisão.',
        type: 'info'
      };
    case 'failed':
      return {
        title: 'Falha na análise',
        message: summary ? `Ocorreu uma falha. ${summary}` : 'Ocorreu um erro durante o processamento da fila.',
        type: 'error'
      };
    case 'canceled':
      return { title: 'Análise cancelada', message: summary ?? 'A execução foi cancelada.', type: 'info' };
    default:
      return { title: 'Processamento finalizado', message: summary ?? 'A fila de análise terminou.', type: 'info' };
  }
};

/** Dispara o evento que faz o shell reconsultar o status logo após um start pela UI. */
export const announcePipelineStarted = (): void => {
  if (typeof window === 'undefined') return;
  window.dispatchEvent(new Event(PIPELINE_STARTED_EVENT));
};
