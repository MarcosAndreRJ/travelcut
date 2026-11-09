import React from 'react';
import type { ReviewStatusFilter } from './reviewModel';
import { PREEDIT_OUTCOME_LABEL, formatRemovedSeconds, type PreeditSummary } from './preeditSummary';

export interface PreeditControlPanelProps {
  summary: PreeditSummary;
  hasPlan: boolean;
  activeStatus: ReviewStatusFilter;
  onSelectStatus: (status: ReviewStatusFilter) => void;
}

interface Tile {
  label: string;
  value: number;
  status: ReviewStatusFilter | null;
  tone: string;
}

/** Cabeçalho-resumo da pré-edição: contagens reais; cada contagem filtra a fila quando possível. */
export const PreeditControlPanel: React.FC<PreeditControlPanelProps> = ({ summary, hasPlan, activeStatus, onSelectStatus }) => {
  const tiles: Tile[] = [
    { label: PREEDIT_OUTCOME_LABEL.autoApplied, value: summary.autoApplied, status: 'applied', tone: 'border-emerald-500/30 text-emerald-300' },
    { label: PREEDIT_OUTCOME_LABEL.userApplied, value: summary.userApplied, status: 'applied', tone: 'border-cyan-500/30 text-cyan-300' },
    { label: PREEDIT_OUTCOME_LABEL.pending, value: summary.pending, status: 'pending', tone: 'border-amber-500/30 text-amber-300' },
    { label: PREEDIT_OUTCOME_LABEL.notApplied, value: summary.notApplied, status: 'rejected', tone: 'border-rose-500/30 text-rose-300' },
    { label: 'Restauradas', value: summary.restored, status: null, tone: 'border-slate-700 text-slate-300' },
    { label: PREEDIT_OUTCOME_LABEL.conflict, value: summary.conflict, status: 'conflict', tone: 'border-orange-500/30 text-orange-300' }
  ];

  return (
    <section aria-label="Painel de controle da pré-edição" className="px-4 md:px-6 py-3 border-b border-slate-800 bg-[#080d1a] shrink-0">
      <div className="grid grid-cols-2 md:grid-cols-3 xl:grid-cols-6 gap-2">
        {tiles.map((tile) => {
          const pressed = tile.status !== null && activeStatus === tile.status;
          const body = (
            <>
              <span className="block text-[10px] uppercase font-bold tracking-wider text-left">{tile.label}</span>
              <span className="block text-lg font-bold font-mono text-white mt-0.5 text-left">{tile.value}</span>
            </>
          );
          const base = `p-2.5 rounded-xl bg-[#0a1424] border ${tile.tone}`;
          return tile.status === null ? (
            <div key={tile.label} className={base}>
              {body}
            </div>
          ) : (
            <button
              key={tile.label}
              type="button"
              aria-pressed={pressed}
              onClick={() => onSelectStatus(tile.status as ReviewStatusFilter)}
              className={`${base} text-left hover:border-cyan-500/50 focus-visible:outline focus-visible:outline-2 focus-visible:outline-cyan-400 ${
                pressed ? 'ring-1 ring-cyan-500' : ''
              }`}
            >
              {body}
            </button>
          );
        })}
      </div>
      <p className="mt-2 text-[11px] text-slate-400">
        <span className="text-slate-500">Tempo removido:</span>{' '}
        <span className="font-mono text-slate-200">{formatRemovedSeconds(summary.removedSourceSeconds)}</span>
        <span className="mx-2 text-slate-700" aria-hidden="true">|</span>
        <span className="text-slate-500">Efeitos (zooms) aplicados:</span>{' '}
        <span className="font-mono text-slate-200">{summary.zoomsApplied ?? '—'}</span>
        {!hasPlan && <span className="ml-2 text-slate-500">Gere o plano na Tela 03 para medir tempo removido e zooms.</span>}
      </p>
    </section>
  );
};
