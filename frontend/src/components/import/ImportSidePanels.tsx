import React, { useState } from 'react';
import { AlertTriangle, Clock, Film, FolderOpen, HardDrive, Monitor, Tag, X } from 'lucide-react';
import type { ImportDestination } from '../../types/ipc';
import {
  formatBytes,
  formatDurationLong,
  formatResolutionGroups,
  IMPORT_DESTINATIONS,
  ImportOptions,
  ImportSummary,
  joinPath,
  kindPlural
} from './importUtils';

const CARD = 'rounded-2xl bg-[#0a1222] border border-slate-800 p-4';
const CARD_TITLE = 'text-sm font-bold text-white mb-3';

// ---- Destino -----------------------------------------------------------------

interface DestinationCardProps {
  rootPath: string;
  destination: ImportDestination;
  onChange: (destination: ImportDestination) => void;
}

export const DestinationCard: React.FC<DestinationCardProps> = ({ rootPath, destination, onChange }) => {
  const fullPath = joinPath(rootPath, destination);
  return (
    <section className={CARD} aria-labelledby="import-destination-title">
      <h2 id="import-destination-title" className={CARD_TITLE}>
        Destino da importação
      </h2>
      <div className="relative">
        <FolderOpen className="w-4 h-4 text-amber-400 absolute left-3 top-2.5 pointer-events-none" aria-hidden="true" />
        <select
          value={destination}
          onChange={(event) => onChange(event.target.value as ImportDestination)}
          aria-label="Pasta de destino"
          className="w-full pl-9 pr-3 py-2 bg-[#0a1424] border border-slate-700/80 rounded-xl text-xs text-white focus:outline-none focus:border-cyan-500"
        >
          {IMPORT_DESTINATIONS.map((option) => (
            <option key={option} value={option}>
              {option}
            </option>
          ))}
        </select>
      </div>
      <p className="mt-2 text-[10px] font-mono text-slate-400 break-all" title={fullPath}>
        {fullPath}
      </p>
    </section>
  );
};

// ---- Opções ------------------------------------------------------------------

interface ToggleSwitchProps {
  id: string;
  checked: boolean;
  onChange: (checked: boolean) => void;
  label: string;
  description: string;
}

export const ToggleSwitch: React.FC<ToggleSwitchProps> = ({ id, checked, onChange, label, description }) => (
  <div className="flex items-start justify-between gap-3">
    <div className="min-w-0">
      <p id={`${id}-label`} className="text-xs font-semibold text-white">
        {label}
      </p>
      <p id={`${id}-desc`} className="text-[10px] text-slate-400 mt-0.5">
        {description}
      </p>
    </div>
    <button
      type="button"
      id={id}
      role="switch"
      aria-checked={checked}
      aria-labelledby={`${id}-label`}
      aria-describedby={`${id}-desc`}
      onClick={() => onChange(!checked)}
      className={`relative shrink-0 w-9 h-5 rounded-full transition-colors focus-visible:ring-2 focus-visible:ring-cyan-400 outline-none ${
        checked ? 'bg-cyan-500' : 'bg-slate-700'
      }`}
    >
      <span
        className={`absolute top-0.5 left-0.5 w-4 h-4 rounded-full bg-white shadow transition-transform ${
          checked ? 'translate-x-4' : 'translate-x-0'
        }`}
      />
    </button>
  </div>
);

const OPTION_DEFS: ReadonlyArray<{ key: keyof ImportOptions; label: string; description: string }> = [
  {
    key: 'copyFiles',
    label: 'Copiar arquivos para o projeto',
    description: 'Mantém uma cópia organizada na pasta do projeto'
  },
  {
    key: 'generateThumbnails',
    label: 'Gerar miniaturas (thumbnails)',
    description: 'Cria pré-visualizações para navegação rápida'
  },
  {
    key: 'analyzeMetadata',
    label: 'Analisar metadados',
    description: 'Lê duração, resolução, frame rate e codec'
  },
  {
    key: 'detectSpeech',
    label: 'Detectar cenas com fala',
    description: 'Pré-análise para transcrição automática'
  }
];

interface OptionsCardProps {
  options: ImportOptions;
  onChange: (key: keyof ImportOptions, value: boolean) => void;
}

export const OptionsCard: React.FC<OptionsCardProps> = ({ options, onChange }) => (
  <section className={CARD} aria-labelledby="import-options-title">
    <h2 id="import-options-title" className={CARD_TITLE}>
      Opções de importação
    </h2>
    <div className="space-y-3">
      {OPTION_DEFS.map((def) => (
        <ToggleSwitch
          key={def.key}
          id={`import-opt-${def.key}`}
          label={def.label}
          description={def.description}
          checked={options[def.key]}
          onChange={(value) => onChange(def.key, value)}
        />
      ))}
    </div>
    {!options.copyFiles && (
      <div role="note" className="mt-3 flex items-start gap-2 px-3 py-2 rounded-lg bg-amber-500/10 border border-amber-500/30 text-[11px] text-amber-200">
        <AlertTriangle className="w-3.5 h-3.5 mt-0.5 shrink-0 text-amber-400" aria-hidden="true" />
        <span>Somente arquivos que já estão na pasta de destino serão registrados.</span>
      </div>
    )}
  </section>
);

// ---- Tags --------------------------------------------------------------------

interface TagsCardProps {
  tags: string[];
  onAdd: (raw: string) => void;
  onRemove: (tag: string) => void;
}

export const TagsCard: React.FC<TagsCardProps> = ({ tags, onAdd, onRemove }) => {
  const [draft, setDraft] = useState('');

  const commit = () => {
    if (draft.trim()) onAdd(draft);
    setDraft('');
  };

  return (
    <section className={CARD} aria-labelledby="import-tags-title">
      <h2 id="import-tags-title" className={CARD_TITLE}>
        Tags na importação
      </h2>
      <div className="relative">
        <Tag className="w-4 h-4 text-slate-400 absolute left-3 top-2.5 pointer-events-none" aria-hidden="true" />
        <input
          type="text"
          value={draft}
          onChange={(event) => setDraft(event.target.value)}
          onKeyDown={(event) => {
            if (event.key === 'Enter' || event.key === ',') {
              event.preventDefault();
              commit();
            } else if (event.key === 'Backspace' && draft === '' && tags.length > 0) {
              onRemove(tags[tags.length - 1]);
            }
          }}
          onBlur={commit}
          placeholder="Adicionar tags (ex.: viagem, drone, paisagem)"
          aria-label="Adicionar tags"
          className="w-full pl-9 pr-3 py-2 bg-[#0a1424] border border-slate-700/80 rounded-xl text-xs text-white placeholder-slate-500 focus:outline-none focus:border-cyan-500"
        />
      </div>
      {tags.length > 0 && (
        <ul className="mt-3 flex flex-wrap gap-1.5" aria-label="Tags adicionadas">
          {tags.map((tag) => (
            <li
              key={tag}
              className="flex items-center gap-1 pl-2.5 pr-1 py-0.5 rounded-full bg-cyan-500/15 border border-cyan-500/30 text-[11px] text-cyan-200"
            >
              {tag}
              <button
                type="button"
                onClick={() => onRemove(tag)}
                aria-label={`Remover tag ${tag}`}
                className="p-0.5 rounded-full hover:bg-cyan-500/30 focus-visible:ring-2 focus-visible:ring-cyan-400 outline-none"
              >
                <X className="w-3 h-3" aria-hidden="true" />
              </button>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
};

// ---- Resumo ------------------------------------------------------------------

interface SummaryCardProps {
  summary: ImportSummary;
}

export const SummaryCard: React.FC<SummaryCardProps> = ({ summary }) => {
  const kinds = (['Video', 'Audio', 'Image'] as const).filter((kind) => summary.counts[kind] > 0);
  const missing = summary.unknownDurationCount;

  return (
    <section className={CARD} aria-labelledby="import-summary-title">
      <h2 id="import-summary-title" className={CARD_TITLE}>
        Resumo da importação
      </h2>
      {summary.total === 0 ? (
        <p className="text-xs text-slate-500">Nenhum item selecionado.</p>
      ) : (
        <ul className="space-y-2.5 text-xs text-slate-300">
          {kinds.map((kind) => (
            <li key={kind} className="flex items-center gap-2.5">
              <Film className="w-4 h-4 text-cyan-400 shrink-0" aria-hidden="true" />
              <span>
                <strong className="text-white">{summary.counts[kind]}</strong> {kindPlural(kind, summary.counts[kind])}{' '}
                selecionado{summary.counts[kind] === 1 ? '' : 's'}
              </span>
            </li>
          ))}
          <li className="flex items-center gap-2.5">
            <Clock className="w-4 h-4 text-cyan-400 shrink-0" aria-hidden="true" />
            <span>
              Duração total:{' '}
              <strong className="text-white">
                {summary.hasKnownDuration ? formatDurationLong(summary.durationSecondsTotal) : '—'}
              </strong>
              {missing > 0 && <span className="text-slate-500"> • {missing} sem metadados</span>}
            </span>
          </li>
          <li className="flex items-center gap-2.5">
            <HardDrive className="w-4 h-4 text-cyan-400 shrink-0" aria-hidden="true" />
            <span>
              Tamanho total: <strong className="text-white">{formatBytes(summary.bytesTotal)}</strong>
            </span>
          </li>
          <li className="flex items-start gap-2.5">
            <Monitor className="w-4 h-4 text-cyan-400 shrink-0 mt-0.5" aria-hidden="true" />
            <span>
              Resoluções:{' '}
              <strong className="text-white">
                {summary.resolutionGroups.length > 0 ? formatResolutionGroups(summary.resolutionGroups) : '—'}
              </strong>
              {summary.unknownResolutionCount > 0 && (
                <span className="text-slate-500"> • {summary.unknownResolutionCount} sem resolução</span>
              )}
            </span>
          </li>
        </ul>
      )}
    </section>
  );
};
