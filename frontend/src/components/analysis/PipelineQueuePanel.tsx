import React from 'react';
import { Play, RefreshCw, Square } from 'lucide-react';
import type { PipelineStageStatusDto, PipelineStatusDto } from '../../types/ipc';
import {
  PIPELINE_STATE_LABEL,
  barTone,
  isPipelineActive,
  stageStateLabel,
  stageStateTone
} from './analysisFormat';
import { PipelineActivitySummary } from './PipelineActivitySummary';

interface PipelineQueuePanelProps {
  status: PipelineStatusDto | null;
  statusError: string | null;
  actionError: string | null;
  isStarting: boolean;
  isCanceling: boolean;
  hasMedia: boolean;
  isMock: boolean;
  force: boolean;
  onForceChange: (value: boolean) => void;
  onStart: () => void;
  onCancel: () => void;
  onRetry: () => void;
}

const StageRow: React.FC<{ index: number; stage: PipelineStageStatusDto }> = ({ index, stage }) => {
  const hasTotal = stage.totalItems > 0;
  const ratio = hasTotal ? Math.min(1, Math.max(0, stage.processedItems / stage.totalItems)) : 0;
  // A barra conta mídias concluídas: em 0/1 a etapa ativa não pode parecer parada.
  const runningWithoutItems = stage.state === 'running' && stage.processedItems === 0;

  return (
    <li
      className="flex flex-wrap items-center gap-x-4 gap-y-1 px-3 py-2 rounded-xl bg-[#0a1222] border border-slate-800/80"
      title={stage.reason ?? undefined}
    >
      <span className="w-6 h-6 rounded-full bg-slate-800 border border-slate-700 text-[11px] font-bold text-slate-300 flex items-center justify-center shrink-0" aria-hidden="true">
        {index}
      </span>

      <div className="min-w-0 flex-1 basis-56">
        <p className="text-xs font-semibold text-white truncate">{stage.label}</p>
        {stage.reason && <p className="text-[10px] text-slate-500 truncate">{stage.reason}</p>}
      </div>

      <div className="w-36 shrink-0">
        {hasTotal ? (
          <div
            role="progressbar"
            aria-label={`Progresso: ${stage.label}`}
            aria-valuemin={0}
            aria-valuemax={stage.totalItems}
            aria-valuenow={stage.processedItems}
            className="h-1.5 w-full bg-slate-800 rounded-full overflow-hidden"
          >
            <div
              className={`h-full rounded-full ${barTone(stage.state)} ${runningWithoutItems ? 'animate-pulse' : ''}`}
              style={{ width: runningWithoutItems ? '100%' : `${ratio * 100}%` }}
            />
          </div>
        ) : (
          <span className="text-[10px] text-slate-600">Sem itens</span>
        )}
      </div>

      <span className="w-28 shrink-0 text-[11px] font-mono text-slate-300 text-right">
        {hasTotal ? `${stage.processedItems}/${stage.totalItems} concluídas` : '—'}
      </span>

      <span className={`shrink-0 px-2 py-0.5 rounded border text-[10px] font-semibold ${stageStateTone(stage.state)}`}>
        {stageStateLabel(stage.state)}
      </span>
    </li>
  );
};

export const PipelineQueuePanel: React.FC<PipelineQueuePanelProps> = ({
  status,
  statusError,
  actionError,
  isStarting,
  isCanceling,
  hasMedia,
  isMock,
  force,
  onForceChange,
  onStart,
  onCancel,
  onRetry
}) => {
  const active = isPipelineActive(status?.state);
  const canStart = hasMedia && !active && !isStarting;
  const canCancel = active && !!status?.jobId && !isCanceling;

  return (
    <section aria-labelledby="pipeline-queue-title" className="shrink-0 border-t border-slate-800 bg-[#080e1b] px-6 py-3 flex flex-col min-h-0 max-h-80">
      <header className="flex flex-wrap items-center justify-between gap-3 mb-2">
        <div className="flex items-center gap-2 min-w-0">
          <h3 id="pipeline-queue-title" className="text-xs font-bold text-white">Fila de Processamento</h3>
          {isMock && (
            <span className="px-1.5 py-0.5 rounded bg-amber-500/15 border border-amber-500/40 text-amber-300 text-[9px] font-bold tracking-wider">
              DEMO
            </span>
          )}
          {status && <span className="text-[10px] text-slate-400">{PIPELINE_STATE_LABEL[status.state]}</span>}
        </div>

        <div className="flex flex-wrap items-center gap-2">
          <label className="flex items-center gap-1.5 text-[11px] text-slate-300 cursor-pointer select-none">
            <input
              type="checkbox"
              checked={force}
              onChange={(e) => onForceChange(e.target.checked)}
              className="accent-cyan-500"
            />
            Reprocessar ignorando cache
          </label>
          {active && (
            <button
              type="button"
              onClick={onCancel}
              disabled={!canCancel}
              title={status?.jobId ? undefined : 'Nenhum job identificado pelo host'}
              className="px-3 py-1.5 rounded-xl bg-slate-800 hover:bg-slate-700 text-slate-200 text-xs font-medium flex items-center gap-1.5 border border-slate-700 disabled:opacity-50"
            >
              <Square className="w-3 h-3" /> {isCanceling ? 'Cancelando…' : 'Cancelar'}
            </button>
          )}
          <button
            type="button"
            onClick={onStart}
            disabled={!canStart}
            title={
              !hasMedia
                ? 'Importe mídias antes de iniciar a análise'
                : active
                  ? 'Já existe uma análise em andamento'
                  : undefined
            }
            className="px-3 py-1.5 rounded-xl bg-cyan-600 hover:bg-cyan-500 text-white text-xs font-semibold flex items-center gap-1.5 shadow-md shadow-cyan-950 disabled:opacity-50 disabled:cursor-not-allowed"
          >
            <Play className="w-3 h-3 fill-white" /> {isStarting ? 'Iniciando…' : 'Iniciar análise'}
          </button>
        </div>
      </header>

      {!hasMedia && <p className="text-[11px] text-slate-500 mb-2">Biblioteca vazia: importe mídias para analisar.</p>}

      {actionError && (
        <p role="alert" className="text-[11px] text-rose-300 mb-2">
          {actionError}
        </p>
      )}

      {statusError && (
        <div role="alert" className="flex items-center gap-2 text-[11px] text-rose-300 mb-2">
          <span>Não foi possível ler a fila: {statusError}</span>
          <button type="button" onClick={onRetry} className="px-2 py-0.5 rounded-lg border border-slate-700 text-slate-200 hover:bg-slate-800 flex items-center gap-1">
            <RefreshCw className="w-3 h-3" /> Tentar novamente
          </button>
        </div>
      )}

      {!status && !statusError && <p role="status" className="text-[11px] text-slate-400">Carregando fila…</p>}

      <PipelineActivitySummary status={status} isCanceling={isCanceling} hostUnreachable={!!statusError} />

      {status && status.stages.length === 0 && (
        <p className="text-[11px] text-slate-500">O host não informou etapas de processamento.</p>
      )}

      {status && status.stages.length > 0 && (
        <ol className="space-y-1.5 overflow-y-auto min-h-0">
          {status.stages.map((stage, idx) => (
            <StageRow key={stage.stage} index={idx + 1} stage={stage} />
          ))}
        </ol>
      )}
    </section>
  );
};
