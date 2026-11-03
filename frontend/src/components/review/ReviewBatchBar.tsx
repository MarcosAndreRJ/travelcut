import React from 'react';
import { CheckCircle2, Clock, XCircle } from 'lucide-react';
import type { ReviewDecisionAction } from './reviewModel';

export interface PendingBatch {
  ids: string[];
  action: ReviewDecisionAction;
  /** Aviso extra da confirmação (ex.: lote de silêncios e limites da heurística). */
  note?: string;
}

interface ReviewBatchBarProps {
  selectedCount: number;
  pending: PendingBatch | null;
  busy: boolean;
  onRequest: (action: ReviewDecisionAction) => void;
  onConfirm: () => void;
  onCancelConfirm: () => void;
  onClearSelection: () => void;
}

const VERB: Record<ReviewDecisionAction, string> = {
  approve: 'Aprovar',
  reject: 'Rejeitar',
  defer: 'Adiar'
};

const PLURAL_NOUN = (n: number): string => (n === 1 ? 'candidato' : 'candidatos');

/** Lote: ação sobre os itens marcados, sempre com confirmação mostrando a quantidade. */
export const ReviewBatchBar: React.FC<ReviewBatchBarProps> = ({
  selectedCount,
  pending,
  busy,
  onRequest,
  onConfirm,
  onCancelConfirm,
  onClearSelection
}) => {
  if (pending) {
    const n = pending.ids.length;
    return (
      <div
        role="alertdialog"
        aria-labelledby="review-batch-confirm-title"
        className="rounded-xl border border-cyan-500/40 bg-cyan-950/30 p-3 space-y-2"
      >
        <p id="review-batch-confirm-title" className="text-[11px] text-cyan-100 leading-relaxed">
          <strong>{VERB[pending.action]} {n} {PLURAL_NOUN(n)}?</strong> Cada item recebe uma decisão no histórico.
          Desfazer é feito item a item.
        </p>
        {pending.note && <p className="text-[11px] text-amber-200 leading-relaxed" data-testid="batch-note">{pending.note}</p>}
        <div className="flex gap-2">
          <button
            type="button"
            onClick={onConfirm}
            disabled={busy}
            autoFocus
            className="flex-1 py-1.5 rounded-lg bg-cyan-600 hover:bg-cyan-500 disabled:opacity-50 text-white text-[11px] font-semibold focus-visible:outline focus-visible:outline-2 focus-visible:outline-white"
          >
            {busy ? 'Gravando...' : `Confirmar (${n})`}
          </button>
          <button
            type="button"
            onClick={onCancelConfirm}
            disabled={busy}
            className="flex-1 py-1.5 rounded-lg bg-[#070d18] border border-slate-700 text-slate-200 text-[11px] font-semibold hover:border-slate-500 disabled:opacity-50"
          >
            Cancelar
          </button>
        </div>
      </div>
    );
  }

  if (selectedCount === 0) return null;

  return (
    <div className="rounded-xl border border-slate-700 bg-[#070d18] p-2.5 space-y-2" role="group" aria-label="Ações em lote">
      <div className="flex items-center justify-between text-[11px] text-slate-300">
        <span>
          <strong className="text-white">{selectedCount}</strong> {PLURAL_NOUN(selectedCount)} selecionado(s)
        </span>
        <button
          type="button"
          onClick={onClearSelection}
          disabled={busy}
          className="text-[10px] text-slate-400 hover:text-white underline underline-offset-2 disabled:opacity-50"
        >
          Limpar seleção
        </button>
      </div>
      <div className="grid grid-cols-3 gap-1.5">
        <button
          type="button"
          onClick={() => onRequest('approve')}
          disabled={busy}
          className="py-1 rounded-lg text-[11px] font-semibold flex items-center justify-center gap-1 border border-slate-700 text-slate-200 hover:border-emerald-500 hover:text-emerald-300 disabled:opacity-50 focus-visible:outline focus-visible:outline-2 focus-visible:outline-cyan-400"
        >
          <CheckCircle2 className="w-3 h-3" aria-hidden="true" /> Aprovar
        </button>
        <button
          type="button"
          onClick={() => onRequest('reject')}
          disabled={busy}
          className="py-1 rounded-lg text-[11px] font-semibold flex items-center justify-center gap-1 border border-slate-700 text-slate-200 hover:border-rose-500 hover:text-rose-300 disabled:opacity-50 focus-visible:outline focus-visible:outline-2 focus-visible:outline-cyan-400"
        >
          <XCircle className="w-3 h-3" aria-hidden="true" /> Rejeitar
        </button>
        <button
          type="button"
          onClick={() => onRequest('defer')}
          disabled={busy}
          className="py-1 rounded-lg text-[11px] font-semibold flex items-center justify-center gap-1 border border-slate-700 text-slate-200 hover:border-slate-500 hover:text-white disabled:opacity-50 focus-visible:outline focus-visible:outline-2 focus-visible:outline-cyan-400"
        >
          <Clock className="w-3 h-3" aria-hidden="true" /> Adiar
        </button>
      </div>
    </div>
  );
};
