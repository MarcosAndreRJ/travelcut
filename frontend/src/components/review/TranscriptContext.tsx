import React from 'react';
import type { MediaAnalysisDto } from '../../types/ipc';
import { ANALYSIS_STATUS_LABEL, formatClock } from '../analysis/analysisFormat';
import type { AsyncState } from './useReviewData';
import { REVIEW_WINDOW_SECONDS, wordInInterval, wordsInWindow, type Interval } from './reviewModel';

interface TranscriptContextProps {
  state: AsyncState<MediaAnalysisDto>;
  /** Intervalo efetivo do candidato (destaque). */
  interval: Interval;
  /** Janela de revisão (± REVIEW_WINDOW_SECONDS do intervalo original). */
  reviewWindow: Interval;
  /** true quando o candidato está aprovado como corte: palavras do intervalo ficam riscadas. */
  approved: boolean;
  onSeek: (sourceSeconds: number) => void;
}

const StatusMessage: React.FC<{ tone?: 'muted' | 'error'; children: React.ReactNode }> = ({ tone = 'muted', children }) => (
  <p
    role={tone === 'error' ? 'alert' : 'status'}
    className={`text-xs leading-relaxed ${tone === 'error' ? 'text-rose-300' : 'text-slate-400'}`}
  >
    {children}
  </p>
);

/** Transcrição contextual: palavras com timestamp Whisper, sem inventar texto quando a análise não existe. */
export const TranscriptContext: React.FC<TranscriptContextProps> = ({ state, interval, reviewWindow, approved, onSeek }) => {
  const header = (
    <h3 className="text-[11px] font-bold uppercase tracking-wider text-slate-300">Transcrição com marcação de cortes</h3>
  );

  let body: React.ReactNode;
  if (state.status === 'idle') {
    body = <StatusMessage>Selecione um candidato para ver a transcrição.</StatusMessage>;
  } else if (state.status === 'loading') {
    body = <StatusMessage>Carregando transcrição da mídia...</StatusMessage>;
  } else if (state.status === 'error') {
    body = <StatusMessage tone="error">Não foi possível ler a análise desta mídia: {state.message}</StatusMessage>;
  } else {
    const analysis = state.data;
    const transcript = analysis.transcript;
    if (analysis.transcriptionStatus !== 'completed' || !transcript) {
      const reason =
        analysis.transcriptionStatus === 'not_run'
          ? 'Ainda não analisado: rode a análise na Biblioteca para obter a transcrição.'
          : analysis.transcriptionReason ?? 'Motivo não informado pelo host.';
      body = (
        <div className="space-y-1">
          <p className="text-[11px] font-semibold text-slate-300">
            Transcrição: {ANALYSIS_STATUS_LABEL[analysis.transcriptionStatus] ?? analysis.transcriptionStatus}
          </p>
          <StatusMessage tone={analysis.transcriptionStatus === 'failed' ? 'error' : 'muted'}>{reason}</StatusMessage>
        </div>
      );
    } else {
      const words = wordsInWindow(transcript.words, reviewWindow);
      body = words.length === 0 ? (
        <StatusMessage>Nenhuma palavra com timestamp nesta janela de {REVIEW_WINDOW_SECONDS} s.</StatusMessage>
      ) : (
        <p className="text-sm leading-[2.1] text-slate-200" aria-label="Palavras da janela de revisão">
          {words.map((w) => {
            const inside = wordInInterval(w, interval);
            const removed = inside && approved;
            const cls = removed
              ? 'line-through decoration-rose-400 text-rose-300 bg-rose-950/50'
              : inside
                ? 'text-emerald-200 bg-emerald-950/50'
                : 'text-slate-400';
            return (
              <React.Fragment key={w.seq}>
                <button
                  type="button"
                  onClick={() => onSeek(w.start)}
                  title={`${formatClock(w.start)} → ${formatClock(w.end)} · ir para esta palavra`}
                  aria-label={`${w.text.trim()}, ${formatClock(w.start)}${removed ? ', removida pelo corte aprovado' : ''}`}
                  className={`rounded px-0.5 font-medium hover:text-white focus-visible:outline focus-visible:outline-2 focus-visible:outline-cyan-400 ${cls}`}
                >
                  {w.text.trim()}
                </button>{' '}
              </React.Fragment>
            );
          })}
        </p>
      );
    }
  }

  return (
    <section aria-label="Transcrição contextual" className="rounded-2xl border border-slate-800 bg-[#0a1222] p-4 space-y-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        {header}
        <span className="text-[10px] font-mono text-slate-500">
          Janela {formatClock(reviewWindow.start)} → {formatClock(reviewWindow.end)}
        </span>
      </div>
      {body}
      <p className="text-[10px] text-slate-500">
        Verde: dentro do intervalo · Riscado: removido pelo corte aprovado · Clique numa palavra para ir ao trecho.
        Timestamps por palavra (Whisper).
      </p>
    </section>
  );
};
