import React from 'react';
import { RotateCcw } from 'lucide-react';
import type { CutFeedback, CutFeedbackAction } from './directCutController';

interface CutFeedbackBannerProps {
  feedback: CutFeedback;
  busy?: boolean;
  onAction?: (action: CutFeedbackAction) => void;
}

/** Confirmação de corte/restauração com o botão Desfazer ou Refazer da própria mensagem. */
export const CutFeedbackBanner: React.FC<CutFeedbackBannerProps> = ({ feedback, busy = false, onAction }) => {
  const action = feedback.action ?? null;
  const toneClass =
    feedback.tone === 'success'
      ? 'bg-emerald-950/60 border-emerald-600/50 text-emerald-200'
      : feedback.tone === 'warning'
        ? 'bg-amber-950/60 border-amber-600/50 text-amber-200'
        : feedback.tone === 'error'
          ? 'bg-rose-950/60 border-rose-600/50 text-rose-200'
          : 'bg-slate-800 border-slate-700 text-slate-200';
  return (
    <div
      role={feedback.tone === 'error' ? 'alert' : 'status'}
      className={`p-2 rounded-xl border text-xs flex flex-wrap items-center justify-between gap-2 ${toneClass}`}
    >
      <span>{feedback.text}</span>
      {action && onAction && (
        <button
          type="button"
          onClick={() => onAction(action)}
          disabled={busy}
          className="px-2 py-0.5 rounded-lg border border-slate-500/60 bg-slate-900/70 hover:bg-slate-800 text-white font-semibold disabled:opacity-40 flex items-center gap-1"
        >
          <RotateCcw className="w-3 h-3" aria-hidden="true" />
          <span>{action.label}</span>
        </button>
      )}
    </div>
  );
};

interface CutRestoreButtonProps {
  /** Quantidade de cortes efetivos que a seleção atinge; sem cortes o botão não aparece. */
  count: number;
  busy?: boolean;
  onRestore: () => void;
}

/** Restaura o(s) corte(s) efetivo(s) sobrepostos à seleção (decision.submit reject no host). */
export const CutRestoreButton: React.FC<CutRestoreButtonProps> = ({ count, busy = false, onRestore }) => {
  if (count === 0) return null;
  return (
    <button
      type="button"
      onClick={onRestore}
      disabled={busy}
      title={`Restaura ${count === 1 ? 'o corte' : `os ${count} cortes`} sobreposto(s) à seleção; os demais cortes permanecem`}
      className="px-2.5 py-1 rounded-lg bg-emerald-950/80 hover:bg-emerald-900 disabled:opacity-40 text-emerald-200 border border-emerald-700/70 text-xs font-semibold flex items-center gap-1 shadow-sm"
    >
      <RotateCcw className="w-3 h-3 text-emerald-400" aria-hidden="true" />
      <span>Restaurar trecho</span>
    </button>
  );
};
