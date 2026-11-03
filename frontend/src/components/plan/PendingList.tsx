import React from 'react';
import { CheckCircle2, ListChecks } from 'lucide-react';
import type { PlanPendingItemDto } from '../../types/ipc';
import { pendingKindLabel } from './planFormat';

/** Pendências do plano (EditPlanDto.pending): sugestões e mídias que não entraram na timeline. */
export const PendingList: React.FC<{ pending: PlanPendingItemDto[] }> = ({ pending }) => (
  <section aria-labelledby="plan-pending-title" className="rounded-xl bg-[#0a1222] border border-slate-800 p-4 space-y-3">
    <div className="flex items-center justify-between">
      <h3 id="plan-pending-title" className="text-xs font-bold text-white flex items-center gap-1.5">
        <ListChecks className="w-3.5 h-3.5 text-amber-300" aria-hidden="true" /> Pendências
      </h3>
      <span className="text-[10px] font-mono text-slate-400">{pending.length}</span>
    </div>
    {pending.length === 0 ? (
      <p className="text-[11px] text-slate-400 flex items-center gap-1.5">
        <CheckCircle2 className="w-3.5 h-3.5 text-emerald-400" aria-hidden="true" />
        Nenhuma pendência neste plano.
      </p>
    ) : (
      <ul className="space-y-2">
        {pending.map((item, index) => (
          <li
            key={`${item.kind}-${item.suggestionId ?? item.mediaId ?? 'x'}-${index}`}
            className="rounded-lg border border-amber-500/20 bg-amber-950/20 px-2.5 py-2"
          >
            <span className="text-[10px] font-bold uppercase tracking-wide text-amber-300">
              {pendingKindLabel(item.kind)}
            </span>
            <span className="block text-[11px] text-slate-200 mt-0.5">{item.message}</span>
          </li>
        ))}
      </ul>
    )}
  </section>
);
