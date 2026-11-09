import React, { useEffect, useRef } from 'react';
import { AlertTriangle, CircleCheck, CircleX, Info, Library, Loader2, X, type LucideIcon } from 'lucide-react';
import type { ImportItemResult, ImportJobStatusDto, PostAnalysisState, PostAnalysisStatusDto } from '../../types/ipc';
import {
  basenameOf,
  formatBytes,
  IMPORT_JOB_STATE_LABEL,
  IMPORT_RESULT_LABEL,
  IMPORT_STAGE_LABEL,
  isSuccessfulResult,
  isTerminalJobState
} from './importUtils';

interface ImportProgressOverlayProps {
  job: ImportJobStatusDto | null;
  overlayError: string | null;
  cancelRequested: boolean;
  onCancel: () => void;
  onClose: () => void;
  onOpenLibrary: () => void;
  /** Resumo pós-análise (pipeline.status.postAnalysis); ausente quando não há automação. */
  postAnalysis?: PostAnalysisStatusDto | null;
}

type NoteTone = 'info' | 'warn' | 'error' | 'success';

const NOTE_CLASS: Record<NoteTone, string> = {
  info: 'bg-slate-800/60 border-slate-700 text-slate-300',
  warn: 'bg-amber-500/10 border-amber-500/30 text-amber-200',
  error: 'bg-rose-500/10 border-rose-500/30 text-rose-200',
  success: 'bg-emerald-500/10 border-emerald-500/30 text-emerald-200'
};

const NOTE_ICON: Record<NoteTone, LucideIcon> = {
  info: Info,
  warn: AlertTriangle,
  error: CircleX,
  success: CircleCheck
};

/** Texto honesto para o resultado da análise de fala; null quando não há o que comunicar. */
export const speechAnalysisNotice = (
  state: ImportJobStatusDto['speechAnalysis']
): { tone: NoteTone; text: string } | null => {
  switch (state) {
    case 'merged':
      return { tone: 'success', text: 'Transcrição e análise de fala concluídas e mescladas ao projeto.' };
    case 'failed':
      return {
        tone: 'error',
        text: 'A análise de fala falhou nesta importação. Nenhuma transcrição foi mesclada ao projeto.'
      };
    case 'skipped':
      return { tone: 'info', text: 'Análise de fala ignorada nesta importação.' };
    case 'unavailable':
      return {
        tone: 'warn',
        text: 'Detecção de fala indisponível neste ambiente. Nenhuma análise de fala foi feita nesta importação.'
      };
    case 'queued':
      return { tone: 'info', text: 'Detecção de fala em fila.' };
    default:
      return null;
  }
};

const POST_STATE_LABEL: Record<PostAnalysisState, string> = {
  idle: 'Aguardando',
  running: 'Em andamento',
  completed: 'Concluída',
  completed_with_issues: 'Concluída com pendências',
  failed: 'Falhou',
  skipped: 'Ignorada'
};

const POST_STAGE_LABEL: Record<'plan' | 'proxy', string> = { plan: 'planejamento', proxy: 'proxies' };

const POST_SKIP_LABEL: Record<string, string> = {
  auto_process_disabled: 'processamento automático desligado',
  no_analysis_results: 'nenhum resultado de análise disponível'
};

const resultTone = (result: ImportItemResult): string => {
  if (isSuccessfulResult(result)) return 'text-emerald-300';
  if (result === 'failed') return 'text-rose-300';
  if (result === 'canceled') return 'text-amber-300';
  return 'text-slate-400';
};

/** Painel modal de progresso. Só pode ser fechado após estado terminal ou erro de consulta. */
export const ImportProgressOverlay: React.FC<ImportProgressOverlayProps> = ({
  job,
  overlayError,
  cancelRequested,
  onCancel,
  onClose,
  onOpenLibrary,
  postAnalysis = null
}) => {
  const dialogRef = useRef<HTMLDivElement | null>(null);
  const speechNote = job ? speechAnalysisNotice(job.speechAnalysis) : null;
  const SpeechNoteIcon: LucideIcon = speechNote ? NOTE_ICON[speechNote.tone] : Info;
  const terminal = job !== null && isTerminalJobState(job.state);
  const canClose = terminal || overlayError !== null;

  useEffect(() => {
    dialogRef.current?.focus();
  }, []);

  useEffect(() => {
    if (!canClose) return;
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') onClose();
    };
    document.addEventListener('keydown', onKey);
    return () => document.removeEventListener('keydown', onKey);
  }, [canClose, onClose]);

  const bytesRatio = job && job.bytesTotal > 0 ? Math.min(1, job.bytesCopied / job.bytesTotal) : 0;
  const percent = Math.round(bytesRatio * 100);
  const failedMessage =
    job && (job.state === 'failed' || job.state === 'completed_with_errors' || job.state === 'canceled')
      ? job.message
      : null;
  const neutralMessage = job && job.message && !failedMessage ? job.message : null;

  const counts = job
    ? job.items.reduce<Record<string, number>>((acc, item) => {
        acc[item.result] = (acc[item.result] ?? 0) + 1;
        return acc;
      }, {})
    : {};
  const successCount = job ? job.items.filter((item) => isSuccessfulResult(item.result)).length : 0;

  const stateTone =
    job?.state === 'completed'
      ? 'bg-emerald-500/15 text-emerald-300 border-emerald-500/40'
      : job?.state === 'failed' || job?.state === 'completed_with_errors'
        ? 'bg-rose-500/15 text-rose-300 border-rose-500/40'
        : job?.state === 'canceled'
          ? 'bg-amber-500/15 text-amber-300 border-amber-500/40'
          : 'bg-cyan-500/15 text-cyan-300 border-cyan-500/40';

  return (
    <div className="fixed inset-0 z-50 bg-black/70 backdrop-blur-sm flex items-center justify-center p-4">
      <div
        ref={dialogRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby="import-progress-title"
        tabIndex={-1}
        className="w-full max-w-2xl max-h-[90vh] flex flex-col rounded-2xl bg-[#0a1222] border border-slate-700 shadow-2xl shadow-black/70 outline-none"
      >
        <header className="flex items-center justify-between gap-3 px-5 py-4 border-b border-slate-800">
          <div className="flex items-center gap-2 min-w-0">
            <h2 id="import-progress-title" className="text-sm font-bold text-white">
              Importação de mídia
            </h2>
            <span className={`px-2 py-0.5 rounded-full text-[10px] font-bold border ${stateTone}`}>
              {job ? IMPORT_JOB_STATE_LABEL[job.state] : 'Iniciando…'}
            </span>
          </div>
          <button
            type="button"
            onClick={onClose}
            disabled={!canClose}
            aria-label="Fechar"
            title={canClose ? 'Fechar' : 'Aguarde o fim da importação ou cancele-a'}
            className="p-1.5 rounded-lg text-slate-400 hover:text-white hover:bg-slate-800 disabled:opacity-30 disabled:cursor-not-allowed focus-visible:ring-2 focus-visible:ring-cyan-500 outline-none"
          >
            <X className="w-4 h-4" aria-hidden="true" />
          </button>
        </header>

        <div className="flex-1 min-h-0 overflow-y-auto px-5 py-4 space-y-4">
          {overlayError && (
            <div role="alert" className="flex items-start gap-2 px-3 py-2 rounded-lg bg-rose-500/10 border border-rose-500/30 text-xs text-rose-200">
              <CircleX className="w-4 h-4 mt-0.5 shrink-0 text-rose-400" aria-hidden="true" />
              <span>{overlayError}</span>
            </div>
          )}

          {!job && !overlayError && (
            <div className="flex items-center gap-2 text-xs text-slate-400">
              <Loader2 className="w-4 h-4 animate-spin text-cyan-400" aria-hidden="true" /> Aguardando o host iniciar a importação…
            </div>
          )}

          {job && (
            <>
              <div>
                <div className="flex items-center justify-between text-xs mb-1.5">
                  <span className="font-semibold text-white">{IMPORT_STAGE_LABEL[job.stage]}</span>
                  <span className="font-mono text-slate-400">{percent}%</span>
                </div>
                <div
                  role="progressbar"
                  aria-label="Progresso por bytes copiados"
                  aria-valuemin={0}
                  aria-valuemax={100}
                  aria-valuenow={percent}
                  className="h-2.5 w-full rounded-full bg-slate-800 overflow-hidden"
                >
                  <div
                    className="h-full bg-gradient-to-r from-cyan-600 to-cyan-400 transition-all duration-500"
                    style={{ width: `${percent}%` }}
                  />
                </div>
                <div className="flex flex-wrap items-center justify-between gap-2 mt-2 text-[11px] text-slate-400 font-mono">
                  <span>
                    {job.processedFiles} de {job.totalFiles} arquivos
                  </span>
                  <span>
                    {job.bytesTotal > 0
                      ? `${formatBytes(job.bytesCopied)} de ${formatBytes(job.bytesTotal)}`
                      : 'Tamanho total ainda não informado pelo host'}
                  </span>
                </div>
                {job.currentFile && !terminal && (
                  <p className="mt-1.5 text-[11px] text-slate-300 truncate" title={job.currentFile}>
                    Arquivo atual: <span className="font-mono">{basenameOf(job.currentFile)}</span>
                  </p>
                )}
              </div>

              {speechNote && (
                <div
                  role={speechNote.tone === 'error' ? 'alert' : 'note'}
                  className={`flex items-start gap-2 px-3 py-2 rounded-lg border text-xs ${NOTE_CLASS[speechNote.tone]}`}
                >
                  <SpeechNoteIcon className="w-4 h-4 mt-0.5 shrink-0" aria-hidden="true" />
                  <span>{speechNote.text}</span>
                </div>
              )}

              {postAnalysis && (
                <section
                  aria-labelledby="import-post-analysis-title"
                  className="rounded-xl border border-slate-800 p-3 space-y-2"
                >
                  <div className="flex items-center justify-between gap-2">
                    <h3
                      id="import-post-analysis-title"
                      className="text-[11px] font-bold uppercase tracking-wider text-slate-400"
                    >
                      Pós-análise automática
                    </h3>
                    <span className="text-[11px] font-semibold text-slate-200">
                      {POST_STATE_LABEL[postAnalysis.state]}
                      {postAnalysis.state === 'running' && postAnalysis.stage
                        ? ` (${POST_STAGE_LABEL[postAnalysis.stage]})`
                        : ''}
                    </span>
                  </div>
                  {postAnalysis.state === 'skipped' ? (
                    <p className="text-[11px] text-slate-400">
                      Motivo:{' '}
                      {postAnalysis.reason ? (POST_SKIP_LABEL[postAnalysis.reason] ?? postAnalysis.reason) : 'não informado'}
                    </p>
                  ) : (
                    <dl className="grid grid-cols-2 sm:grid-cols-4 gap-2 text-[11px]">
                      {(
                        [
                          { label: 'Cortes aplicados', value: postAnalysis.autoCutsApplied },
                          { label: 'Pendentes de revisão', value: postAnalysis.pendingReview },
                          { label: 'Proxies na fila', value: postAnalysis.proxiesQueued },
                          { label: 'Falhas', value: postAnalysis.failures }
                        ] as const
                      ).map((metric) => (
                        <div key={metric.label} className="rounded-lg bg-slate-900/60 px-2.5 py-1.5">
                          <dt className="text-slate-500">{metric.label}</dt>
                          <dd className="font-mono text-white">{metric.value}</dd>
                        </div>
                      ))}
                    </dl>
                  )}
                  {postAnalysis.issues.length > 0 && (
                    <ul aria-label="Pendências da pós-análise" className="flex flex-wrap gap-1.5">
                      {postAnalysis.issues.map((code, index) => (
                        <li
                          key={`${index}-${code}`}
                          className="px-2 py-0.5 rounded-md bg-amber-500/10 border border-amber-500/30 font-mono text-[10px] text-amber-200"
                        >
                          {code}
                        </li>
                      ))}
                    </ul>
                  )}
                </section>
              )}

              {failedMessage && (
                <div role="alert" className="flex items-start gap-2 px-3 py-2 rounded-lg bg-rose-500/10 border border-rose-500/30 text-xs text-rose-200">
                  <CircleX className="w-4 h-4 mt-0.5 shrink-0 text-rose-400" aria-hidden="true" />
                  <span>Mensagem do backend: {failedMessage}</span>
                </div>
              )}
              {neutralMessage && (
                <div className="flex items-start gap-2 px-3 py-2 rounded-lg bg-slate-800/60 border border-slate-700 text-xs text-slate-300">
                  <Info className="w-4 h-4 mt-0.5 shrink-0 text-cyan-400" aria-hidden="true" />
                  <span>{neutralMessage}</span>
                </div>
              )}

              {terminal && (
                <div className="flex flex-wrap gap-2 text-[11px]">
                  <span className="px-2 py-1 rounded-lg bg-emerald-500/10 border border-emerald-500/30 text-emerald-300">
                    {successCount} registrados
                  </span>
                  {(counts.failed ?? 0) > 0 && (
                    <span className="px-2 py-1 rounded-lg bg-rose-500/10 border border-rose-500/30 text-rose-300">
                      {counts.failed} com falha
                    </span>
                  )}
                  {(counts.canceled ?? 0) > 0 && (
                    <span className="px-2 py-1 rounded-lg bg-amber-500/10 border border-amber-500/30 text-amber-300">
                      {counts.canceled} cancelados
                    </span>
                  )}
                </div>
              )}

              {job.items.length > 0 && (
                <div>
                  <h3 className="text-[11px] font-bold uppercase tracking-wider text-slate-400 mb-2">Itens</h3>
                  <ul className="max-h-60 overflow-y-auto rounded-xl border border-slate-800 divide-y divide-slate-800/80" aria-label="Resultado por arquivo">
                    {job.items.map((item, index) => (
                      <li key={`${item.sourcePath}-${index}`} className="px-3 py-2 text-xs">
                        <div className="flex items-center justify-between gap-3">
                          <span className="font-mono text-slate-200 truncate" title={item.sourcePath}>
                            {basenameOf(item.sourcePath)}
                          </span>
                          <span className={`shrink-0 font-semibold ${resultTone(item.result)}`}>
                            {IMPORT_RESULT_LABEL[item.result]}
                          </span>
                        </div>
                        {(item.destinationRelativePath || item.message) && (
                          <p className="mt-0.5 text-[10px] text-slate-500 truncate" title={item.message ?? item.destinationRelativePath ?? ''}>
                            {item.destinationRelativePath ?? ''}
                            {item.destinationRelativePath && item.message ? ' • ' : ''}
                            {item.message ?? ''}
                          </p>
                        )}
                      </li>
                    ))}
                  </ul>
                </div>
              )}
            </>
          )}
        </div>

        <footer className="flex flex-wrap items-center justify-end gap-2 px-5 py-3 border-t border-slate-800">
          {!terminal && job && (
            <button
              type="button"
              onClick={onCancel}
              disabled={cancelRequested || overlayError !== null}
              className="px-3 py-1.5 rounded-xl bg-rose-600/80 hover:bg-rose-500 text-white text-xs font-semibold disabled:opacity-50 disabled:cursor-not-allowed focus-visible:ring-2 focus-visible:ring-cyan-400 outline-none"
            >
              {cancelRequested ? 'Cancelamento solicitado…' : 'Cancelar importação'}
            </button>
          )}
          {terminal && (
            <button
              type="button"
              onClick={onOpenLibrary}
              className="px-3 py-1.5 rounded-xl bg-cyan-600 hover:bg-cyan-500 text-white text-xs font-semibold flex items-center gap-1.5 focus-visible:ring-2 focus-visible:ring-cyan-400 outline-none"
            >
              <Library className="w-3.5 h-3.5" aria-hidden="true" /> Ver biblioteca
            </button>
          )}
          {canClose && (
            <button
              type="button"
              onClick={onClose}
              className="px-3 py-1.5 rounded-xl bg-slate-800 hover:bg-slate-700 text-slate-200 text-xs font-medium border border-slate-700 focus-visible:ring-2 focus-visible:ring-cyan-400 outline-none"
            >
              Fechar
            </button>
          )}
          {terminal && job?.state === 'completed' && (
            <span className="mr-auto flex items-center gap-1.5 text-[11px] text-emerald-300">
              <CircleCheck className="w-3.5 h-3.5" aria-hidden="true" /> Concluída
            </span>
          )}
        </footer>
      </div>
    </div>
  );
};
