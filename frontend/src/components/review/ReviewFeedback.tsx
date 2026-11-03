import React from 'react';
import { AlertTriangle, Info, X } from 'lucide-react';
import type { ReviewErrorInfo, ReviewNotice } from './reviewModel';

interface ReviewFeedbackProps {
  error: ReviewErrorInfo | null;
  notice: ReviewNotice | null;
  onDismiss: () => void;
}

/** Erro de ação e aviso não intrusivo (ex.: "Plano atualizado: revisão rN") visíveis na própria Revisão. */
export const ReviewFeedback: React.FC<ReviewFeedbackProps> = ({ error, notice, onDismiss }) => {
  if (!error && !notice) return null;

  return (
    <div className="px-4 pt-3 space-y-2 shrink-0">
      {error && (
        <div
          role="alert"
          className="flex items-start gap-2 px-3 py-2 rounded-xl border border-rose-500/40 bg-rose-950/40 text-xs text-rose-100"
        >
          <AlertTriangle className="w-4 h-4 text-rose-400 shrink-0 mt-0.5" aria-hidden="true" />
          <div className="flex-1 min-w-0">
            <p className="font-semibold">Decisão não registrada</p>
            <p className="text-rose-200/90">
              {error.message}
              {error.code && <span className="font-mono text-[10px] text-rose-300/80"> (código {error.code})</span>}
            </p>
          </div>
        </div>
      )}
      {notice && (
        <div
          role="status"
          className={`flex items-start gap-2 px-3 py-2 rounded-xl border text-xs ${
            notice.tone === 'warning'
              ? 'border-amber-500/40 bg-amber-950/30 text-amber-100'
              : 'border-cyan-500/30 bg-cyan-950/30 text-cyan-100'
          }`}
        >
          <Info className="w-4 h-4 shrink-0 mt-0.5" aria-hidden="true" />
          <p className="flex-1 min-w-0">{notice.text}</p>
        </div>
      )}
      <div className="flex justify-end">
        <button
          type="button"
          onClick={onDismiss}
          className="text-[10px] text-slate-400 hover:text-white flex items-center gap-1 focus-visible:outline focus-visible:outline-2 focus-visible:outline-cyan-400 rounded"
        >
          <X className="w-3 h-3" aria-hidden="true" /> Dispensar aviso
        </button>
      </div>
    </div>
  );
};
