import React, { useEffect, useState } from 'react';
import { Activity, AlertTriangle } from 'lucide-react';
import type { PipelineStatusDto } from '../../types/ipc';
import { formatDuration, pipelineActivityView, type PipelineActivityView } from './pipelineActivity';

const PHASE_TONE: Record<PipelineActivityView['phase'], string> = {
  waiting: 'text-slate-300 bg-slate-500/10 border-slate-500/30',
  active: 'text-cyan-300 bg-cyan-500/10 border-cyan-500/30',
  canceling: 'text-amber-300 bg-amber-500/10 border-amber-500/30',
  completed: 'text-emerald-300 bg-emerald-500/10 border-emerald-500/30',
  completed_with_issues: 'text-amber-300 bg-amber-500/10 border-amber-500/30',
  failed: 'text-rose-300 bg-rose-500/10 border-rose-500/30',
  canceled: 'text-amber-300 bg-amber-500/10 border-amber-500/30'
};

const telemetryText = (view: PipelineActivityView): string | null => {
  if (view.lastActivitySecondsAgo === null) return null;
  const ago = formatDuration(view.lastActivitySecondsAgo);
  return view.telemetry === 'stale' ? `Sem telemetria recente (último sinal há ${ago})` : `Último sinal do host há ${ago}`;
};

export const PipelineActivityCard: React.FC<{ view: PipelineActivityView; hostUnreachable: boolean }> = ({
  view,
  hostUnreachable
}) => {
  const running = view.phase === 'active' || view.phase === 'canceling';
  const indeterminate = running && view.percent === null;
  const signal = telemetryText(view);

  return (
    <div role="status" aria-label="Atividade da análise" className="mb-2 rounded-xl border border-slate-800/80 bg-[#0a1222] px-3 py-2 space-y-1.5">
      <div className="flex flex-wrap items-center gap-x-3 gap-y-1">
        <span className={`shrink-0 px-2 py-0.5 rounded border text-[10px] font-semibold ${PHASE_TONE[view.phase]}`}>
          {view.phaseLabel}
        </span>
        {view.clipName && (
          <span className="min-w-0 max-w-full truncate text-xs font-semibold text-white" title={view.clipName}>
            {view.clipName}
          </span>
        )}
        {view.stageLabel && <span className="text-[11px] text-slate-300">{view.stageLabel}</span>}
        {view.detail && <span className="text-[11px] text-cyan-300">{view.detail}</span>}
        <span className="ml-auto flex flex-wrap items-center gap-x-3 text-[10px] font-mono text-slate-400">
          {view.elapsedSeconds !== null && <span>Tempo decorrido: {formatDuration(view.elapsedSeconds)}</span>}
          <span>
            {view.mediaCompleted}/{view.mediaTotal} mídias concluídas
          </span>
        </span>
      </div>

      {running && (
        <div
          role="progressbar"
          aria-label="Progresso interno da mídia"
          aria-valuemin={0}
          aria-valuemax={100}
          aria-valuenow={view.percent ?? undefined}
          className="h-1.5 w-full bg-slate-800 rounded-full overflow-hidden"
        >
          <div
            className={`h-full rounded-full bg-cyan-500 ${indeterminate ? 'animate-pulse' : ''}`}
            style={{ width: indeterminate ? '100%' : `${view.percent}%` }}
          />
        </div>
      )}

      {running && (
        <p className="text-[10px] text-slate-400">
          {view.percent !== null
            ? `${view.percent.toFixed(1)}% desta etapa, informado pelo motor.`
            : 'Em andamento; o motor não informou percentual nesta etapa.'}
          {signal && ` ${signal}.`}
        </p>
      )}

      {running && view.telemetry === 'stale' && (
        <p className="flex items-start gap-1.5 text-[10px] text-amber-300">
          <AlertTriangle className="w-3 h-3 shrink-0 mt-px" />
          <span>
            Isso não indica travamento: o large-v3 em CPU pode levar muito tempo. O processo pode continuar ativo e o cancelamento segue disponível.
          </span>
        </p>
      )}

      {hostUnreachable && running && (
        <p className="flex items-start gap-1.5 text-[10px] text-amber-300">
          <Activity className="w-3 h-3 shrink-0 mt-px" />
          <span>Sem resposta do host agora; mostrando o último estado conhecido e tentando novamente.</span>
        </p>
      )}

      {view.errorMessage && <p className="text-[11px] text-rose-300">{view.errorMessage}</p>}
    </div>
  );
};

/** Mantém o tempo decorrido correndo mesmo sem novas respostas do host. */
export const PipelineActivitySummary: React.FC<{
  status: PipelineStatusDto | null;
  isCanceling: boolean;
  hostUnreachable: boolean;
}> = ({ status, isCanceling, hostUnreachable }) => {
  const [nowMs, setNowMs] = useState(() => Date.now());
  const view = pipelineActivityView(status, nowMs, isCanceling);
  const ticking = view?.phase === 'active' || view?.phase === 'canceling' || view?.phase === 'waiting';

  useEffect(() => {
    if (!ticking) return;
    setNowMs(Date.now());
    const timer = setInterval(() => setNowMs(Date.now()), 1000);
    return () => clearInterval(timer);
  }, [ticking]);

  return view ? <PipelineActivityCard view={view} hostUnreachable={hostUnreachable} /> : null;
};
