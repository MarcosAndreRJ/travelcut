import type { PipelineStatusDto } from '../../types/ipc';

// Derivação pura do que acontece dentro da mídia em análise.
// Percentual só com telemetria real; sem ela a barra é indeterminada e nada avança por tempo decorrido.
// Sem sinal recente não significa travamento: large-v3 em CPU pode ficar muito tempo numa etapa.

export type PipelinePhase =
  | 'waiting'
  | 'active'
  | 'canceling'
  | 'completed'
  | 'completed_with_issues'
  | 'failed'
  | 'canceled';

export type PipelineTelemetry = 'recent' | 'stale' | 'unknown';

export interface PipelineActivityView {
  phase: PipelinePhase;
  phaseLabel: string;
  clipName: string | null;
  stageLabel: string | null;
  detail: string | null;
  elapsedSeconds: number | null;
  lastActivitySecondsAgo: number | null;
  telemetry: PipelineTelemetry;
  percent: number | null;
  mediaCompleted: number;
  mediaTotal: number;
  errorMessage: string | null;
}

/** O worker emite heartbeat a cada 10 s; acima disto o sinal é considerado antigo. */
export const STALE_TELEMETRY_SECONDS = 45;

const PHASE_LABEL: Record<PipelinePhase, string> = {
  waiting: 'Aguardando início',
  active: 'Em andamento',
  canceling: 'Cancelando…',
  completed: 'Concluído',
  completed_with_issues: 'Concluído com ressalvas',
  failed: 'Falhou',
  canceled: 'Cancelado'
};

const TERMINAL_STAGE_STATES = new Set(['completed', 'completed_with_issues', 'unavailable', 'failed', 'canceled', 'skipped']);

const parseMs = (value: string | null | undefined): number | null => {
  if (!value) return null;
  const ms = Date.parse(value);
  return Number.isNaN(ms) ? null : ms;
};

const secondsBetween = (fromMs: number, toMs: number): number => Math.max(0, Math.floor((toMs - fromMs) / 1000));

export const validPercent = (value: number | null | undefined): number | null =>
  typeof value === 'number' && Number.isFinite(value) && value >= 0 && value <= 100 ? value : null;

export const formatDuration = (seconds: number): string => {
  if (seconds < 60) return `${seconds} s`;
  const minutes = Math.floor(seconds / 60);
  if (minutes < 60) return `${minutes} min ${String(seconds % 60).padStart(2, '0')} s`;
  return `${Math.floor(minutes / 60)} h ${String(minutes % 60).padStart(2, '0')} min`;
};

function resolvePhase(status: PipelineStatusDto, isCanceling: boolean): PipelinePhase | null {
  switch (status.state) {
    case 'queued':
      return isCanceling ? 'canceling' : 'waiting';
    case 'running':
      return isCanceling || status.activity?.cancelRequested ? 'canceling' : 'active';
    case 'completed':
    case 'completed_with_issues':
    case 'failed':
    case 'canceled':
      return status.state;
    default:
      return null;
  }
}

function firstProblem(status: PipelineStatusDto): string | null {
  const stage = status.stages.find((s) => s.state === 'failed') ?? status.stages.find((s) => s.state === 'unavailable');
  if (stage?.reason) return stage.reason;
  return status.media.find((m) => m.message)?.message ?? null;
}

/** null quando não há job a resumir (idle). */
export function pipelineActivityView(
  status: PipelineStatusDto | null,
  nowMs: number,
  isCanceling = false
): PipelineActivityView | null {
  if (!status) return null;
  const phase = resolvePhase(status, isCanceling);
  if (!phase) return null;

  const running = phase === 'active' || phase === 'canceling';
  const activity = status.activity ?? null;

  const startedMs = parseMs(status.startedAtUtc);
  const endMs = running || phase === 'waiting' ? nowMs : parseMs(status.finishedAtUtc);
  const lastMs = parseMs(activity?.lastActivityAtUtc);
  const lastAgo = lastMs === null ? null : secondsBetween(lastMs, nowMs);

  const mediaCompleted =
    activity?.mediaCompleted ??
    status.media.filter((m) => Object.values(m.stageStates).every((s) => s && TERMINAL_STAGE_STATES.has(s))).length;

  const clip = status.currentMedia;
  return {
    phase,
    phaseLabel: PHASE_LABEL[phase],
    clipName: clip ? clip.split(/[/\\]/).pop() || clip : null,
    stageLabel: running
      ? activity?.currentStageLabel ?? status.stages.find((s) => s.state === 'running')?.label ?? null
      : null,
    detail: running ? activity?.detail ?? null : null,
    elapsedSeconds: startedMs !== null && endMs !== null ? secondsBetween(startedMs, endMs) : null,
    lastActivitySecondsAgo: running ? lastAgo : null,
    telemetry: !running || lastAgo === null ? 'unknown' : lastAgo > STALE_TELEMETRY_SECONDS ? 'stale' : 'recent',
    percent: running ? validPercent(activity?.percent) : null,
    mediaCompleted,
    mediaTotal: activity?.mediaTotal ?? status.media.length,
    errorMessage: phase === 'failed' || phase === 'completed_with_issues' ? firstProblem(status) : null
  };
}

/** Resumo curto para o painel do clipe. */
export function activitySummaryLine(view: PipelineActivityView | null): string | null {
  if (!view) return null;
  const parts = [view.stageLabel, view.detail, view.percent !== null ? `${view.percent.toFixed(1)}%` : null].filter(Boolean);
  if (view.elapsedSeconds !== null) parts.push(`há ${formatDuration(view.elapsedSeconds)}`);
  return parts.length > 0 ? parts.join(' · ') : null;
}
