import React, { useEffect, useRef } from 'react';
import { AlertTriangle } from 'lucide-react';
import type { EditSuggestion } from '../../types/ipc';
import { confidenceText, confidenceTone, formatClock, formatSeconds } from '../analysis/analysisFormat';
import {
  effectiveInterval,
  isAdjusted,
  requiresHumanReview,
  reviewKindLabel,
  statusLabelOf,
  type ReviewConfidenceFilter,
  type ReviewFilters,
  type ReviewStatusFilter,
  type StatusCounts
} from './reviewModel';

interface ReviewQueueProps {
  items: EditSuggestion[];
  counts: StatusCounts;
  kindOptions: string[];
  filters: ReviewFilters;
  onFiltersChange: (next: ReviewFilters) => void;
  activeId: string | null;
  selectedIds: ReadonlySet<string>;
  onSelect: (suggestionId: string) => void;
  onToggleSelected: (suggestionId: string) => void;
  onToggleAllVisible: (suggestionIds: string[], select: boolean) => void;
  mediaName: (mediaId: string) => string;
  /** Faixa de lote (fica logo abaixo dos filtros). */
  batchBar?: React.ReactNode;
  /** Rodapé (legenda de atalhos). */
  footer?: React.ReactNode;
}

const STATUS_OPTIONS: { value: ReviewStatusFilter; label: string; count: (c: StatusCounts) => number }[] = [
  { value: 'pending', label: 'Pendentes', count: (c) => c.pending },
  { value: 'approved', label: 'Aprovados', count: (c) => c.approved },
  { value: 'rejected', label: 'Rejeitados', count: (c) => c.rejected },
  { value: 'deferred', label: 'Adiados', count: (c) => c.deferred },
  { value: 'all', label: 'Todos', count: (c) => c.all }
];

const CONFIDENCE_OPTIONS: { value: ReviewConfidenceFilter; label: string }[] = [
  { value: 'all', label: 'Qualquer confiança' },
  { value: 'low', label: 'Confiança baixa' },
  { value: 'medium', label: 'Confiança média' },
  { value: 'high', label: 'Confiança alta' }
];

const selectClass =
  'w-full rounded-lg bg-[#070d18] border border-slate-700 px-2 py-1.5 text-[11px] text-slate-200 focus-visible:outline focus-visible:outline-2 focus-visible:outline-cyan-400';

export const ReviewQueue: React.FC<ReviewQueueProps> = ({
  items,
  counts,
  kindOptions,
  filters,
  onFiltersChange,
  activeId,
  selectedIds,
  onSelect,
  onToggleSelected,
  onToggleAllVisible,
  mediaName,
  batchBar,
  footer
}) => {
  const itemRefs = useRef(new Map<string, HTMLElement>());

  // Mantém o item ativo visível quando a navegação por ↑/↓ muda a seleção.
  useEffect(() => {
    if (!activeId) return;
    itemRefs.current.get(activeId)?.scrollIntoView({ block: 'nearest' });
  }, [activeId]);

  const visibleIds = items.map((s) => s.suggestionId);
  const selectedVisible = visibleIds.filter((id) => selectedIds.has(id)).length;
  const allVisibleSelected = visibleIds.length > 0 && selectedVisible === visibleIds.length;

  const setFilter = <K extends keyof ReviewFilters>(key: K, value: ReviewFilters[K]) =>
    onFiltersChange({ ...filters, [key]: value });

  return (
    <aside
      aria-label="Fila de revisão"
      className="w-full lg:w-80 xl:w-96 shrink-0 min-h-0 max-h-[45vh] lg:max-h-none flex flex-col border-b lg:border-b-0 lg:border-r border-slate-800 bg-[#080e1b]"
    >
      <div className="p-3 border-b border-slate-800 bg-[#0a1424] space-y-2.5">
        <div className="flex items-center justify-between text-xs font-bold text-white">
          <span>Fila de revisão</span>
          <span className="text-[10px] text-cyan-400 font-mono" aria-live="polite">
            {items.length} {items.length === 1 ? 'item' : 'itens'}
          </span>
        </div>

        <div role="group" aria-label="Filtrar por status" className="flex flex-wrap gap-1">
          {STATUS_OPTIONS.map((opt) => {
            const pressed = filters.status === opt.value;
            return (
              <button
                key={opt.value}
                type="button"
                aria-pressed={pressed}
                onClick={() => setFilter('status', opt.value)}
                className={`px-2 py-1 rounded-lg text-[11px] border transition-all focus-visible:outline focus-visible:outline-2 focus-visible:outline-cyan-400 ${
                  pressed
                    ? 'bg-cyan-500/20 text-cyan-300 border-cyan-500/40 font-semibold'
                    : 'text-slate-400 border-transparent hover:bg-slate-800'
                }`}
              >
                {opt.label} <span className="font-mono text-[10px] opacity-80">{opt.count(counts)}</span>
              </button>
            );
          })}
        </div>

        <div className="grid grid-cols-2 gap-2">
          <label className="block min-w-0 text-[10px] font-bold uppercase tracking-wider text-slate-400">
            Categoria
            <select
              className={`${selectClass} mt-1 normal-case tracking-normal font-normal`}
              value={filters.kind}
              onChange={(e) => setFilter('kind', e.target.value)}
            >
              <option value="all">Todas</option>
              {kindOptions.map((kind) => (
                <option key={kind} value={kind}>
                  {reviewKindLabel(kind)}
                </option>
              ))}
            </select>
          </label>
          <label className="block min-w-0 text-[10px] font-bold uppercase tracking-wider text-slate-400">
            Confiança
            <select
              className={`${selectClass} mt-1 normal-case tracking-normal font-normal`}
              value={filters.confidence}
              onChange={(e) => setFilter('confidence', e.target.value as ReviewConfidenceFilter)}
            >
              {CONFIDENCE_OPTIONS.map((opt) => (
                <option key={opt.value} value={opt.value}>
                  {opt.label}
                </option>
              ))}
            </select>
          </label>
        </div>

        <div className="flex flex-wrap items-center gap-x-4 gap-y-1.5">
          <label className="flex items-center gap-2 text-[11px] text-slate-300 cursor-pointer">
            <input
              type="checkbox"
              className="accent-cyan-500 w-3.5 h-3.5"
              checked={filters.humanOnly}
              onChange={(e) => setFilter('humanOnly', e.target.checked)}
            />
            Revisão humana obrigatória
          </label>
          {visibleIds.length > 0 && (
            <label className="flex items-center gap-2 text-[11px] text-slate-400 cursor-pointer">
              <input
                type="checkbox"
                className="accent-cyan-500 w-3.5 h-3.5"
                checked={allVisibleSelected}
                ref={(el) => {
                  if (el) el.indeterminate = selectedVisible > 0 && !allVisibleSelected;
                }}
                onChange={(e) => onToggleAllVisible(visibleIds, e.target.checked)}
                aria-label="Selecionar todos os itens visíveis"
              />
              Selecionar visíveis ({selectedVisible}/{visibleIds.length})
            </label>
          )}
        </div>

        {batchBar}
      </div>

      <div className="flex-1 min-h-0 overflow-y-auto p-3 space-y-2.5">
        {items.length === 0 && (
          <p className="text-xs text-slate-500 text-center py-6">Nenhum candidato com estes filtros.</p>
        )}
        {items.map((sug, index) => {
          const isActive = sug.suggestionId === activeId;
          const eff = effectiveInterval(sug);
          const human = requiresHumanReview(sug);
          const selected = selectedIds.has(sug.suggestionId);
          const approved = sug.status === 'Approved';
          const rejected = sug.status === 'Rejected';
          const label = `${String(index + 1).padStart(2, '0')} · ${reviewKindLabel(sug.kind)}`;

          return (
            <article
              key={sug.suggestionId}
              ref={(el) => {
                if (el) itemRefs.current.set(sug.suggestionId, el);
                else itemRefs.current.delete(sug.suggestionId);
              }}
              className={`rounded-xl border transition-colors ${
                isActive
                  ? 'bg-[#0d1d36] border-cyan-500 shadow-md shadow-cyan-950'
                  : approved
                    ? 'bg-[#0a1622] border-emerald-900/40'
                    : rejected
                      ? 'bg-[#140a12] border-rose-900/40 opacity-80'
                      : 'bg-[#0a1222] border-slate-800 hover:border-slate-700'
              }`}
            >
              <div className="flex items-start gap-2 p-3 pb-2">
                <input
                  type="checkbox"
                  className="mt-1 accent-cyan-500 w-3.5 h-3.5 shrink-0"
                  checked={selected}
                  onChange={() => onToggleSelected(sug.suggestionId)}
                  aria-label={`Selecionar ${label} para lote`}
                />
                <button
                  type="button"
                  onClick={() => onSelect(sug.suggestionId)}
                  aria-current={isActive ? 'true' : undefined}
                  className="flex-1 min-w-0 text-left rounded-lg focus-visible:outline focus-visible:outline-2 focus-visible:outline-cyan-400"
                >
                  <div className="flex items-center justify-between gap-2 mb-1.5">
                    <span className="text-xs font-bold text-white truncate">{label}</span>
                    <span className="text-[10px] font-mono px-1.5 py-0.5 rounded bg-slate-800 text-slate-300 font-bold shrink-0">
                      {formatSeconds(eff.end - eff.start)}
                    </span>
                  </div>
                  <p className="text-[11px] text-slate-300 leading-snug line-clamp-2">{sug.rationale}</p>
                  <div className="flex flex-wrap items-center justify-between gap-1 mt-2 text-[10px] text-slate-400">
                    <span className="font-mono truncate" title={mediaName(sug.mediaId)}>
                      {mediaName(sug.mediaId)} · {formatClock(eff.start)} → {formatClock(eff.end)}
                    </span>
                    <span className="flex items-center gap-1 shrink-0">
                      <span className={`px-1.5 py-0.5 rounded font-bold text-[9px] uppercase border ${confidenceTone(sug.confidenceBand)}`}>
                        {confidenceText(sug.confidenceBand)}
                      </span>
                    </span>
                  </div>
                  <div className="flex flex-wrap items-center gap-1 mt-1.5">
                    <span className="px-1.5 py-0.5 rounded text-[9px] font-bold uppercase text-slate-300 bg-slate-800 border border-slate-700">
                      {statusLabelOf(sug)}
                    </span>
                    {isAdjusted(sug) && (
                      <span className="px-1.5 py-0.5 rounded text-[9px] font-bold uppercase text-cyan-300 bg-cyan-950/60 border border-cyan-800/50">
                        Ajustado
                      </span>
                    )}
                    {human && (
                      <span className="flex items-center gap-1 px-1.5 py-0.5 rounded font-bold text-[9px] uppercase text-amber-300 bg-amber-950/60 border border-amber-800/40">
                        <AlertTriangle className="w-3 h-3" aria-hidden="true" />
                        Revisão humana
                      </span>
                    )}
                  </div>
                </button>
              </div>
            </article>
          );
        })}
      </div>

      {footer}
    </aside>
  );
};
