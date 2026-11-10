import type {
  AnalysisResultStatus,
  EditSuggestion,
  PipelineState,
  StageState,
  TranscriptWordDto
} from '../../types/ipc';

// Funções puras de apresentação. Nenhuma regra de negócio: rótulos e formatação apenas.
// Confiança é sempre qualitativa (baixa/média/alta) — nunca percentual.

const lookup = (map: Record<string, string>, key: string | null | undefined, fallback: string): string =>
  key && Object.prototype.hasOwnProperty.call(map, key) ? map[key] : fallback;

export const STAGE_STATE_LABEL: Record<StageState, string> = {
  pending: 'Pendente',
  running: 'Em andamento',
  completed: 'Concluído',
  completed_with_issues: 'Concluído com ressalvas',
  unavailable: 'Indisponível',
  failed: 'Falhou',
  canceled: 'Cancelado',
  skipped: 'Ignorado'
};

export const PIPELINE_STATE_LABEL: Record<PipelineState, string> = {
  idle: 'Sem análise em andamento',
  queued: 'Na fila',
  running: 'Em andamento',
  completed: 'Concluído',
  completed_with_issues: 'Concluído com ressalvas',
  failed: 'Falhou',
  canceled: 'Cancelado'
};

export const ANALYSIS_STATUS_LABEL: Record<AnalysisResultStatus, string> = {
  not_run: 'Ainda não analisado',
  completed: 'Concluído',
  unavailable: 'Indisponível',
  failed: 'Falhou'
};

export const stageStateLabel = (state: string | null | undefined): string =>
  lookup(STAGE_STATE_LABEL, state, state ?? 'Desconhecido');

export const stageStateTone = (state: string | null | undefined): string => {
  switch (state) {
    case 'completed':
      return 'text-emerald-300 bg-emerald-500/10 border-emerald-500/30';
    case 'completed_with_issues':
    case 'unavailable':
      return 'text-amber-300 bg-amber-500/10 border-amber-500/30';
    case 'running':
      return 'text-cyan-300 bg-cyan-500/10 border-cyan-500/30';
    case 'failed':
      return 'text-rose-300 bg-rose-500/10 border-rose-500/30';
    default:
      return 'text-slate-300 bg-slate-800 border-slate-700';
  }
};

export const barTone = (state: string | null | undefined): string => {
  switch (state) {
    case 'completed':
      return 'bg-emerald-500';
    case 'completed_with_issues':
      return 'bg-amber-500';
    case 'failed':
      return 'bg-rose-500';
    default:
      return 'bg-cyan-500';
  }
};

const CONFIDENCE_TEXT: Record<string, string> = { low: 'baixa', medium: 'média', high: 'alta' };

/** Faixa qualitativa de confiança: "baixa" | "média" | "alta" | "não informada". */
export const confidenceText = (band: string | null | undefined): string =>
  lookup(CONFIDENCE_TEXT, band, 'não informada');

export const confidenceTone = (band: string | null | undefined): string => {
  switch (band) {
    case 'high':
      return 'text-emerald-300 bg-emerald-950/60 border-emerald-800/50';
    case 'medium':
      return 'text-amber-300 bg-amber-950/60 border-amber-800/50';
    case 'low':
      return 'text-rose-300 bg-rose-950/60 border-rose-800/50';
    default:
      return 'text-slate-300 bg-slate-800 border-slate-700';
  }
};

const SCENE_CATEGORY_LABEL: Record<string, string> = {
  conversa: 'Conversa',
  estrada: 'Estrada',
  paisagem: 'Paisagem',
  loja: 'Loja',
  drone: 'Drone',
  outro: 'Outro',
  indeterminado: 'Indeterminado'
};

export const sceneCategoryLabel = (category: string): string =>
  lookup(SCENE_CATEGORY_LABEL, category.toLowerCase(), category);

const HESITATION_TYPE_LABEL: Record<string, string> = {
  hesitation: 'Hesitação',
  false_start: 'Início falso',
  filler: 'Muleta verbal',
  prolongation: 'Prolongamento',
  repetition: 'Repetição'
};

export const hesitationTypeLabel = (type: string): string =>
  lookup(HESITATION_TYPE_LABEL, type.toLowerCase(), type);

// Tipos de candidato vêm do backend em minúsculas (silence, pause, filler...). Comparação sempre case-insensitive.
const SUGGESTION_KIND_LABEL: Record<string, string> = {
  silence: 'Silêncio prolongado',
  pause: 'Pausa',
  hesitation: 'Hesitação verbal',
  filler: 'Muleta verbal',
  false_start: 'Início falso',
  prolongation: 'Prolongamento',
  repetition: 'Repetição',
  speedbroll: 'Aceleração B-roll',
  subtlezoom: 'Salto sutil'
};

export const suggestionKindKey = (kind: string): string => kind.toLowerCase();

export const suggestionKindLabel = (kind: string): string =>
  lookup(SUGGESTION_KIND_LABEL, suggestionKindKey(kind), kind);

const SUGGESTED_ACTION_LABEL: Record<string, string> = {
  cut: 'Cortar trecho',
  review: 'Revisão humana',
  speed2x: 'Acelerar 2x',
  zoom_105: 'Zoom sutil 1.05x'
};

export const suggestedActionLabel = (action: string): string => lookup(SUGGESTED_ACTION_LABEL, action, action);

/** Aprovado: 'Approved' é o status do domínio; 'Accepted' é aceito por compatibilidade com o mock antigo. */
export const isApprovedStatus = (status: string): boolean => status === 'Approved' || status === 'Accepted';

/**
 * Lote "aprovar silêncios confiáveis": somente kind "silence", confiança alta (heurística não calibrada),
 * pendente e sem revisão humana. Pausas de ritmo/humor e demais categorias nunca entram no lote.
 */
export const isBatchApprovable = (s: EditSuggestion): boolean => {
  // Flag do host (batchApprovable) substitui a regra local quando presente; sem ela, vale a regra abaixo.
  if (typeof s.batchApprovable === 'boolean') return s.batchApprovable;
  return localBatchRule(s);
};

const localBatchRule = (s: EditSuggestion): boolean =>
  suggestionKindKey(s.kind) === 'silence' &&
  s.confidenceBand === 'high' &&
  s.status === 'Proposed' &&
  !s.needsHumanReview;

export const needsHumanReviewFlag = (s: EditSuggestion): boolean =>
  s.needsHumanReview || s.suggestedAction === 'review';

export const isPipelineActive = (state: PipelineState | null | undefined): boolean =>
  state === 'queued' || state === 'running';

export const errorMessage = (err: unknown): string => (err instanceof Error ? err.message : String(err));

const pad = (n: number): string => String(n).padStart(2, '0');

/** Tempo de mídia no formato mm:ss.d (décimos). Valores inválidos viram "—". */
export const formatClock = (seconds: number | null | undefined): string => {
  if (seconds === null || seconds === undefined || !Number.isFinite(seconds) || seconds < 0) return '—';
  const tenths = Math.round(seconds * 10);
  const mins = Math.floor(tenths / 600);
  const secs = Math.floor((tenths % 600) / 10);
  return `${pad(mins)}:${pad(secs)}.${tenths % 10}`;
};

/** Duração inteira mm:ss. */
export const formatDuration = (seconds: number | null | undefined): string => {
  if (seconds === null || seconds === undefined || !Number.isFinite(seconds) || seconds < 0) return '—';
  const total = Math.round(seconds);
  return `${pad(Math.floor(total / 60))}:${pad(total % 60)}`;
};

export const formatSeconds = (seconds: number): string =>
  Number.isFinite(seconds) ? `${seconds.toFixed(1)} s` : '—';

export const intervalsOverlap = (aStart: number, aEnd: number, bStart: number, bEnd: number): boolean =>
  aStart < bEnd && aEnd > bStart;

/** Sequências (seq) das palavras que contêm o texto buscado. */
export const findWordMatches = (words: TranscriptWordDto[], query: string): number[] => {
  const q = query.trim().toLowerCase();
  if (!q) return [];
  return words.filter((w) => w.text.toLowerCase().includes(q)).map((w) => w.seq);
};
