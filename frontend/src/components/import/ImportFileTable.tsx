import React, { useEffect, useMemo, useRef, useState } from 'react';
import { ArrowDown, ArrowUp, Ellipsis, ExternalLink, Folder, Loader2, Trash2, X } from 'lucide-react';
import type { ImportCandidateDto } from '../../types/ipc';
import {
  dirnameOf,
  formatDurationClock,
  formatResolution,
  kindLabel,
  METADATA_STATUS_REASON,
  pluralItems,
  type ImportRow
} from './importUtils';
import { ThumbnailCell } from './ThumbnailCell';

interface ImportFileTableProps {
  projectId: string;
  rows: ImportRow[];
  isInspecting: boolean;
  onToggleRow: (key: string) => void;
  onToggleAll: (checked: boolean) => void;
  onRemoveRow: (key: string) => void;
  onRemoveSelected: () => void;
  onClearAll: () => void;
  onOpenFolder: (fullPath: string) => void;
}

// Larguras fixas das colunas (table-fixed): a coluna Nome absorve o restante e trunca.
const COL_WIDTH = {
  check: 28,
  thumb: 64,
  type: 88,
  origin: 84,
  duration: 62,
  resolution: 118,
  menu: 28
} as const;

const durationTitle = (item: ImportCandidateDto): string | undefined => {
  if (item.kind === 'Image') return 'Imagens não têm duração';
  if (item.durationSeconds !== null && item.durationSeconds !== undefined) return undefined;
  return `Duração indisponível: ${METADATA_STATUS_REASON[item.metadataStatus]}`;
};

const resolutionTitle = (item: ImportCandidateDto): string | undefined => {
  if (item.kind !== 'Video') return undefined;
  if (formatResolution(item.width, item.height) !== null) return undefined;
  return `Resolução indisponível: ${METADATA_STATUS_REASON[item.metadataStatus]}`;
};

interface RowMenuProps {
  label: string;
  onRemove: () => void;
  onOpenFolder: () => void;
}

const RowMenu: React.FC<RowMenuProps> = ({ label, onRemove, onOpenFolder }) => {
  const [open, setOpen] = useState(false);
  const wrapperRef = useRef<HTMLDivElement | null>(null);

  useEffect(() => {
    if (!open) return;
    const onPointerDown = (event: MouseEvent) => {
      if (!wrapperRef.current?.contains(event.target as Node)) setOpen(false);
    };
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') setOpen(false);
    };
    document.addEventListener('mousedown', onPointerDown);
    document.addEventListener('keydown', onKey);
    return () => {
      document.removeEventListener('mousedown', onPointerDown);
      document.removeEventListener('keydown', onKey);
    };
  }, [open]);

  return (
    <div ref={wrapperRef} className="relative inline-block">
      <button
        type="button"
        aria-label={`Ações para ${label}`}
        aria-haspopup="menu"
        aria-expanded={open}
        onClick={() => setOpen((value) => !value)}
        className="p-1 rounded-md text-slate-400 hover:text-white hover:bg-slate-800 focus-visible:ring-2 focus-visible:ring-cyan-500 outline-none"
      >
        <Ellipsis className="w-4 h-4" aria-hidden="true" />
      </button>
      {open && (
        <div
          role="menu"
          className="absolute right-0 top-full mt-1 z-20 min-w-[180px] rounded-lg border border-slate-700 bg-[#0d1626] shadow-xl shadow-black/60 py-1 text-xs"
        >
          <button
            type="button"
            role="menuitem"
            onClick={() => {
              setOpen(false);
              onRemove();
            }}
            className="w-full flex items-center gap-2 px-3 py-2 text-left text-slate-200 hover:bg-slate-800 focus-visible:bg-slate-800 outline-none"
          >
            <Trash2 className="w-3.5 h-3.5 text-rose-400" aria-hidden="true" /> Remover da lista
          </button>
          <button
            type="button"
            role="menuitem"
            onClick={() => {
              setOpen(false);
              onOpenFolder();
            }}
            className="w-full flex items-center gap-2 px-3 py-2 text-left text-slate-200 hover:bg-slate-800 focus-visible:bg-slate-800 outline-none"
          >
            <ExternalLink className="w-3.5 h-3.5 text-cyan-400" aria-hidden="true" /> Abrir pasta
          </button>
        </div>
      )}
    </div>
  );
};

export const ImportFileTable: React.FC<ImportFileTableProps> = ({
  projectId,
  rows,
  isInspecting,
  onToggleRow,
  onToggleAll,
  onRemoveRow,
  onRemoveSelected,
  onClearAll,
  onOpenFolder
}) => {
  const [sortDirection, setSortDirection] = useState<'asc' | 'desc'>('asc');
  const headerCheckboxRef = useRef<HTMLInputElement | null>(null);

  const selectedCount = rows.filter((row) => row.selected).length;
  const allSelected = rows.length > 0 && selectedCount === rows.length;

  useEffect(() => {
    if (headerCheckboxRef.current) {
      headerCheckboxRef.current.indeterminate = selectedCount > 0 && !allSelected;
    }
  }, [selectedCount, allSelected]);

  const sortedRows = useMemo(() => {
    const factor = sortDirection === 'asc' ? 1 : -1;
    return [...rows].sort(
      (a, b) =>
        factor *
        a.item.fileName.localeCompare(b.item.fileName, 'pt-BR', { numeric: true, sensitivity: 'base' })
    );
  }, [rows, sortDirection]);

  const headCell = 'px-1.5 py-1.5 border-b border-slate-800 text-[10px] font-semibold uppercase tracking-wide text-slate-400 whitespace-nowrap';
  const bodyCell = 'px-1.5 py-1 border-t border-slate-800/70 align-middle';

  return (
    <section className="rounded-2xl bg-[#0a1222] border border-slate-800 flex flex-col min-w-0" aria-labelledby="import-selected-title">
      <div className="flex flex-wrap items-center justify-between gap-2 px-4 py-2.5 border-b border-slate-800">
        <div className="flex items-center gap-2">
          <h2 id="import-selected-title" className="text-sm font-bold text-white">
            Arquivos selecionados
          </h2>
          <span className="px-2 py-0.5 rounded-full text-[10px] font-bold bg-cyan-500/20 text-cyan-300 border border-cyan-500/30">
            {rows.length} {pluralItems(rows.length)}
          </span>
        </div>
        <div className="flex items-center gap-2">
          <button
            type="button"
            onClick={onRemoveSelected}
            disabled={selectedCount === 0}
            className="px-2.5 py-1 rounded-lg bg-slate-800 hover:bg-slate-700 text-slate-200 text-xs font-medium flex items-center gap-1.5 border border-slate-700 disabled:opacity-40 disabled:cursor-not-allowed focus-visible:ring-2 focus-visible:ring-cyan-500 outline-none"
          >
            <Trash2 className="w-3.5 h-3.5 text-rose-400" aria-hidden="true" /> Remover selecionados
          </button>
          <button
            type="button"
            onClick={onClearAll}
            disabled={rows.length === 0}
            className="px-2.5 py-1 rounded-lg bg-slate-800 hover:bg-slate-700 text-slate-200 text-xs font-medium flex items-center gap-1.5 border border-slate-700 disabled:opacity-40 disabled:cursor-not-allowed focus-visible:ring-2 focus-visible:ring-cyan-500 outline-none"
          >
            <X className="w-3.5 h-3.5" aria-hidden="true" /> Limpar lista
          </button>
        </div>
      </div>

      <div className="overflow-y-auto overflow-x-hidden max-h-[420px]">
        <table className="w-full table-fixed text-[11px] text-left border-separate border-spacing-0">
          <colgroup>
            <col style={{ width: COL_WIDTH.check }} />
            <col style={{ width: COL_WIDTH.thumb }} />
            <col />
            <col style={{ width: COL_WIDTH.type }} />
            <col style={{ width: COL_WIDTH.origin }} />
            <col style={{ width: COL_WIDTH.duration }} />
            <col style={{ width: COL_WIDTH.resolution }} />
            <col style={{ width: COL_WIDTH.menu }} />
          </colgroup>
          <thead className="sticky top-0 z-10 bg-[#0a1222]">
            <tr>
              <th scope="col" className={headCell}>
                <input
                  ref={headerCheckboxRef}
                  type="checkbox"
                  className="accent-cyan-500 w-3.5 h-3.5 cursor-pointer"
                  checked={allSelected}
                  disabled={rows.length === 0}
                  onChange={(event) => onToggleAll(event.target.checked)}
                  aria-label="Selecionar todos os arquivos"
                />
              </th>
              <th scope="col" className={headCell}>Miniatura</th>
              <th scope="col" className={headCell} aria-sort={sortDirection === 'asc' ? 'ascending' : 'descending'}>
                <button
                  type="button"
                  onClick={() => setSortDirection((value) => (value === 'asc' ? 'desc' : 'asc'))}
                  className="flex items-center gap-1 font-semibold text-slate-300 hover:text-white focus-visible:ring-2 focus-visible:ring-cyan-500 outline-none rounded"
                >
                  Nome
                  {sortDirection === 'asc' ? (
                    <ArrowUp className="w-3 h-3 text-cyan-400" aria-hidden="true" />
                  ) : (
                    <ArrowDown className="w-3 h-3 text-cyan-400" aria-hidden="true" />
                  )}
                </button>
              </th>
              <th scope="col" className={headCell}>Tipo</th>
              <th scope="col" className={headCell}>Origem</th>
              <th scope="col" className={headCell}>Duração</th>
              <th scope="col" className={headCell}>Resolução</th>
              <th scope="col" className={headCell}>
                <span className="sr-only">Ações</span>
              </th>
            </tr>
          </thead>
          <tbody>
            {sortedRows.length === 0 && (
              <tr>
                <td colSpan={8} className="px-4 py-8 text-center text-slate-500">
                  Nenhum arquivo selecionado. Arraste arquivos para a área acima, use os botões ou navegue pelo painel à esquerda.
                </td>
              </tr>
            )}
            {sortedRows.map((row) => {
              const item = row.item;
              const durationLabel = item.kind === 'Image' ? null : formatDurationClock(item.durationSeconds);
              const resolutionLabel = item.kind === 'Video' ? formatResolution(item.width, item.height) : null;
              const origin = item.originFolder || dirnameOf(item.fullPath);
              const type = kindLabel(item.kind, item.container, item.fileName);
              return (
                <tr
                  key={row.key}
                  className={`h-11 hover:bg-slate-800/30 transition-colors ${row.selected ? '' : 'opacity-60'}`}
                >
                  <td className={bodyCell}>
                    <input
                      type="checkbox"
                      className="accent-cyan-500 w-3.5 h-3.5 cursor-pointer"
                      checked={row.selected}
                      onChange={() => onToggleRow(row.key)}
                      aria-label={`Selecionar ${item.fileName}`}
                    />
                  </td>
                  <td className={bodyCell}>
                    <ThumbnailCell
                      projectId={projectId}
                      path={item.fullPath}
                      fileName={item.fileName}
                      durationSeconds={item.kind === 'Image' ? null : item.durationSeconds}
                      compact
                    />
                  </td>
                  <td className={bodyCell}>
                    <div className="flex items-center gap-1.5 min-w-0">
                      <span className="font-semibold text-white truncate min-w-0" title={item.fullPath}>
                        {item.fileName}
                      </span>
                      {item.alreadyInLibrary && (
                        <span className="shrink-0 px-1 py-px rounded text-[8px] font-bold uppercase bg-emerald-500/15 text-emerald-300 border border-emerald-500/30 whitespace-nowrap">
                          Já na biblioteca
                        </span>
                      )}
                    </div>
                  </td>
                  <td className={`${bodyCell} text-slate-300`}>
                    <span className="block truncate" title={type}>{type}</span>
                  </td>
                  <td className={`${bodyCell} text-slate-300`}>
                    <span className="flex items-center gap-1 min-w-0" title={dirnameOf(item.fullPath)}>
                      <Folder className="w-3 h-3 text-amber-400 shrink-0" aria-hidden="true" />
                      <span className="truncate">{origin || '—'}</span>
                    </span>
                  </td>
                  <td className={`${bodyCell} font-mono text-slate-200`} title={durationTitle(item)}>
                    <span className="block truncate">{durationLabel ?? '—'}</span>
                  </td>
                  <td className={`${bodyCell} font-mono text-slate-200`} title={resolutionTitle(item)}>
                    <span className="block truncate">{resolutionLabel ?? '—'}</span>
                  </td>
                  <td className={bodyCell}>
                    <RowMenu
                      label={item.fileName}
                      onRemove={() => onRemoveRow(row.key)}
                      onOpenFolder={() => onOpenFolder(item.fullPath)}
                    />
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>

      <div className="flex items-center justify-between gap-2 px-4 py-2 border-t border-slate-800 text-[11px] text-slate-400 min-h-[34px]">
        <span>
          {selectedCount} de {rows.length} selecionados
        </span>
        {isInspecting && (
          <span className="flex items-center gap-1.5 text-cyan-300" role="status">
            <Loader2 className="w-3.5 h-3.5 animate-spin" aria-hidden="true" /> Analisando arquivos…
          </span>
        )}
      </div>
    </section>
  );
};
