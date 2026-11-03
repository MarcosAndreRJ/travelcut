import React, { useState } from 'react';
import { History, Loader2, RotateCcw, Sparkles } from 'lucide-react';
import type { PlanRevisionInfoDto } from '../../types/ipc';
import { formatDateTime } from './planFormat';
import type { PlanBusyAction } from './useEditPlan';

interface RevisionControlsProps {
  revisions: PlanRevisionInfoDto[];
  viewedRevision: number;
  latestRevision: number;
  busy: PlanBusyAction | null;
  mediaCount: number;
  onSelect: (revision: number) => void;
  onGenerate: () => void;
  onRevert: (toRevision: number) => void;
}

export const RevisionControls: React.FC<RevisionControlsProps> = ({
  revisions,
  viewedRevision,
  latestRevision,
  busy,
  mediaCount,
  onSelect,
  onGenerate,
  onRevert
}) => {
  const [confirmingRevert, setConfirmingRevert] = useState(false);
  const sorted = [...revisions].sort((a, b) => b.revision - a.revision);
  const isOlder = viewedRevision !== latestRevision;
  const anyBusy = busy !== null;
  const generateDisabled = anyBusy || mediaCount === 0;

  return (
    <section aria-labelledby="plan-revisions-title" className="space-y-2">
      <div className="flex items-center justify-between">
        <h3 id="plan-revisions-title" className="text-xs font-bold text-white flex items-center gap-1.5">
          <History className="w-3.5 h-3.5 text-cyan-400" aria-hidden="true" /> Revisões
        </h3>
        <span className="text-[10px] font-mono text-slate-500">{revisions.length} no histórico</span>
      </div>

      <label htmlFor="plan-revision-select" className="block text-[11px] text-slate-400">
        Revisão exibida
      </label>
      <select
        id="plan-revision-select"
        value={viewedRevision}
        disabled={anyBusy}
        onChange={(event) => {
          setConfirmingRevert(false);
          onSelect(Number(event.target.value));
        }}
        className="w-full rounded-lg bg-[#0a1222] border border-slate-700 px-2 py-1.5 text-xs text-slate-100 disabled:opacity-50 focus:outline-none focus-visible:ring-2 focus-visible:ring-cyan-400"
      >
        {sorted.map((rev) => (
          <option key={rev.revision} value={rev.revision}>
            {`R${rev.revision} · ${formatDateTime(rev.createdAtUtc)} · ${rev.clipCount} clipes · ${
              rev.isValid ? 'válido' : 'com erros'
            }${rev.revision === latestRevision ? ' (atual)' : ''}`}
          </option>
        ))}
      </select>

      {isOlder && !confirmingRevert && (
        <button
          type="button"
          onClick={() => setConfirmingRevert(true)}
          disabled={anyBusy}
          className="w-full py-1.5 rounded-lg border border-amber-500/40 text-[11px] font-semibold text-amber-200 hover:bg-amber-950/40 disabled:opacity-50 flex items-center justify-center gap-1.5 focus:outline-none focus-visible:ring-2 focus-visible:ring-cyan-400"
        >
          <RotateCcw className="w-3.5 h-3.5" aria-hidden="true" /> Reverter para esta revisão
        </button>
      )}

      {isOlder && confirmingRevert && (
        <div role="alertdialog" aria-labelledby="plan-revert-title" aria-describedby="plan-revert-desc" className="rounded-lg border border-amber-500/40 bg-amber-950/30 p-2.5 space-y-2">
          <p id="plan-revert-title" className="text-xs font-bold text-amber-200">
            Reverter para R{viewedRevision}?
          </p>
          <p id="plan-revert-desc" className="text-[11px] text-slate-300">
            Isso cria a revisão R{latestRevision + 1} com o conteúdo de R{viewedRevision}. O histórico é mantido. Nada é enviado ao DaVinci.
          </p>
          <div className="flex gap-2">
            <button
              type="button"
              onClick={() => {
                setConfirmingRevert(false);
                onRevert(viewedRevision);
              }}
              disabled={anyBusy}
              className="flex-1 py-1.5 rounded-lg bg-amber-600 hover:bg-amber-500 text-white text-[11px] font-semibold disabled:opacity-50 focus:outline-none focus-visible:ring-2 focus-visible:ring-cyan-400"
            >
              Confirmar reversão
            </button>
            <button
              type="button"
              onClick={() => setConfirmingRevert(false)}
              className="flex-1 py-1.5 rounded-lg border border-slate-700 text-[11px] text-slate-200 hover:bg-slate-800 focus:outline-none focus-visible:ring-2 focus-visible:ring-cyan-400"
            >
              Cancelar
            </button>
          </div>
        </div>
      )}

      <button
        type="button"
        onClick={onGenerate}
        disabled={generateDisabled}
        title={mediaCount === 0 ? 'Importe e analise mídias antes de gerar um plano.' : 'Cria uma nova revisão a partir das mídias analisadas.'}
        className="w-full py-2 rounded-lg bg-gradient-to-r from-cyan-500 to-blue-600 hover:from-cyan-400 hover:to-blue-500 text-white text-xs font-semibold flex items-center justify-center gap-1.5 disabled:opacity-50 disabled:cursor-not-allowed focus:outline-none focus-visible:ring-2 focus-visible:ring-cyan-300"
      >
        {busy === 'generate' ? (
          <Loader2 className="w-3.5 h-3.5 animate-spin" aria-hidden="true" />
        ) : (
          <Sparkles className="w-3.5 h-3.5" aria-hidden="true" />
        )}
        {busy === 'generate' ? 'Gerando nova revisão…' : 'Gerar nova revisão'}
      </button>
    </section>
  );
};
