import React from 'react';
import { CheckCircle2, Clock, Pencil, Redo2, Undo2, XCircle } from 'lucide-react';
import type { EditSuggestion } from '../../types/ipc';

interface DecisionActionsProps {
  suggestion: EditSuggestion;
  busy: boolean;
  modifyOpen: boolean;
  onApprove: () => void;
  onReject: () => void;
  onDefer: () => void;
  onToggleModify: () => void;
  onUndo: () => void;
  onRedo: () => void;
}

const base =
  'px-2.5 py-1.5 rounded-xl text-[11px] md:text-xs font-semibold whitespace-nowrap flex items-center gap-1 border transition-all disabled:opacity-50 disabled:cursor-not-allowed focus-visible:outline focus-visible:outline-2 focus-visible:outline-cyan-400';
const neutral = 'bg-[#070d18] text-slate-300 border-slate-700 hover:border-slate-500 hover:text-white';

const Kbd: React.FC<{ children: React.ReactNode }> = ({ children }) => (
  <kbd className="ml-0.5 px-1 rounded bg-slate-900 border border-slate-700 text-[9px] font-mono text-slate-400">{children}</kbd>
);

/** Ações do candidato ativo. Desfazer/refazer seguem canUndo/canRedo do host (sem cálculo local). */
export const DecisionActions: React.FC<DecisionActionsProps> = ({
  suggestion,
  busy,
  modifyOpen,
  onApprove,
  onReject,
  onDefer,
  onToggleModify,
  onUndo,
  onRedo
}) => {
  const approved = suggestion.status === 'Approved';
  const rejected = suggestion.status === 'Rejected';
  const deferred = suggestion.status === 'Deferred';
  const canUndo = suggestion.canUndo === true;
  const canRedo = suggestion.canRedo === true;

  return (
    <div role="toolbar" aria-label="Decisões do candidato" className="flex flex-wrap items-center gap-2">
      <button
        type="button"
        onClick={onApprove}
        disabled={busy}
        aria-pressed={approved}
        className={`${base} ${approved ? 'bg-emerald-600 text-white border-emerald-500' : 'bg-[#070d18] text-slate-200 border-slate-700 hover:border-emerald-500 hover:text-emerald-300'}`}
      >
        <CheckCircle2 className="w-3.5 h-3.5" aria-hidden="true" />
        Aprovar<Kbd>A</Kbd>
      </button>
      <button
        type="button"
        onClick={onReject}
        disabled={busy}
        aria-pressed={rejected}
        className={`${base} ${rejected ? 'bg-rose-900 text-rose-200 border-rose-700' : 'bg-[#070d18] text-slate-200 border-slate-700 hover:border-rose-500 hover:text-rose-300'}`}
      >
        <XCircle className="w-3.5 h-3.5" aria-hidden="true" />
        Rejeitar<Kbd>R</Kbd>
      </button>
      <button
        type="button"
        onClick={onDefer}
        disabled={busy}
        aria-pressed={deferred}
        className={`${base} ${deferred ? 'bg-slate-700 text-white border-slate-500' : 'bg-[#070d18] text-slate-200 border-slate-700 hover:border-slate-500'}`}
      >
        <Clock className="w-3.5 h-3.5" aria-hidden="true" />
        Adiar<Kbd>D</Kbd>
      </button>
      <button
        type="button"
        onClick={onToggleModify}
        disabled={busy}
        aria-expanded={modifyOpen}
        className={`${base} ${modifyOpen ? 'bg-cyan-600 text-white border-cyan-500' : 'bg-[#070d18] text-slate-200 border-slate-700 hover:border-cyan-500 hover:text-cyan-200'}`}
      >
        <Pencil className="w-3.5 h-3.5" aria-hidden="true" />
        Modificar<Kbd>M</Kbd>
      </button>

      <span className="flex-1" aria-hidden="true" />

      <button
        type="button"
        onClick={onUndo}
        disabled={busy || !canUndo}
        title={
          canUndo
            ? 'Desfazer última decisão: volta à decisão anterior deste candidato (ou a Pendente, se não houver outra)'
            : 'Nada para desfazer neste candidato'
        }
        className={`${base} ${neutral}`}
      >
        <Undo2 className="w-3.5 h-3.5" aria-hidden="true" />
        Desfazer última decisão<Kbd>Z</Kbd>
      </button>
      <button
        type="button"
        onClick={onRedo}
        disabled={busy || !canRedo}
        title={canRedo ? 'Refazer: reaplica a última decisão desfeita' : 'Nada para refazer neste candidato'}
        className={`${base} ${neutral}`}
      >
        <Redo2 className="w-3.5 h-3.5" aria-hidden="true" />
        Refazer<Kbd>Y</Kbd>
      </button>
    </div>
  );
};
