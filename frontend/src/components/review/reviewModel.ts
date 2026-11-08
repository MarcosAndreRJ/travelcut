import type { EditSuggestion, TranscriptWordDto } from '../../types/ipc';

// Funções puras da Tela 03 (Revisão de Cortes & Efeitos). Sem React e sem IPC.
// Regra de negócio fica no host: aqui só há filtros de visualização, janelas de apresentação
// e validação local do editor de intervalo (o host continua validando e responde INVALID_INTERVAL).

/** Janela de revisão em torno do candidato: transcrição e intervalo editável (± segundos). */
export const REVIEW_WINDOW_SECONDS = 3;
/** Contexto antes/depois do player de comparação (contextSeconds). */
export const PLAYER_CONTEXT_SECONDS = 2;
/** Passo de quadro para o ajuste fino (30 fps de referência da janela de revisão). */
export const FRAME_STEP_SECONDS = 1 / 30;
/** Passo do campo numérico e dos botões de ±0,1 s. */
export const TENTH_STEP_SECONDS = 0.1;

export type ReviewStatusFilter = 'pending' | 'approved' | 'rejected' | 'deferred' | 'all';
export type ReviewConfidenceFilter = 'all' | 'low' | 'medium' | 'high';
export type ReviewDecisionAction = 'approve' | 'reject' | 'defer' | 'reopen';
export type EditorialCategoryFilter = 'all' | 'cuts' | 'zooms' | 'transitions';

export interface ReviewFilters {
  status: ReviewStatusFilter;
  kind: string; // 'all' ou chave em minúsculas do kind
  confidence: ReviewConfidenceFilter;
  humanOnly: boolean;
  category?: EditorialCategoryFilter;
}

export const DEFAULT_REVIEW_FILTERS: ReviewFilters = {
  status: 'pending',
  kind: 'all',
  confidence: 'all',
  humanOnly: false,
  category: 'all'
};

const CUT_KINDS = new Set(['silence', 'pause', 'filler', 'prolongation', 'repetition', 'false_start', 'hesitation', 'speedbroll', 'cut', 'trim']);
const ZOOM_KINDS = new Set(['subtlezoom', 'zoom', 'punch_in', 'push_in']);
const TRANSITION_KINDS = new Set(['transition', 'video_fade_in', 'video_fade_out', 'video_dissolve', 'audio_fade_in', 'audio_fade_out', 'audio_crossfade']);

export const isCutKind = (kind: string): boolean => CUT_KINDS.has(kindKey(kind));
export const isZoomKind = (kind: string): boolean => ZOOM_KINDS.has(kindKey(kind));
export const isTransitionKind = (kind: string): boolean => TRANSITION_KINDS.has(kindKey(kind));

export interface Interval {
  start: number;
  end: number;
}

export interface ReviewErrorInfo {
  code: string | null;
  message: string;
}

export interface ReviewNotice {
  tone: 'info' | 'warning';
  text: string;
}

const KIND_LABEL: Record<string, string> = {
  silence: 'Silêncio',
  pause: 'Pausa',
  filler: 'Vício de fala',
  prolongation: 'Prolongamento',
  repetition: 'Repetição',
  false_start: 'Falso início',
  hesitation: 'Hesitação'
};

const KIND_ORDER = Object.keys(KIND_LABEL);

export const kindKey = (kind: string): string => kind.toLowerCase();

/** Rótulo PT-BR da categoria; categorias desconhecidas aparecem como o valor recebido. */
export const reviewKindLabel = (kind: string): string => {
  const key = kindKey(kind);
  return Object.prototype.hasOwnProperty.call(KIND_LABEL, key) ? KIND_LABEL[key] : kind;
};

/** Ordena categorias conhecidas primeiro, depois as desconhecidas em ordem alfabética. */
export const kindOptionsOf = (suggestions: EditSuggestion[]): string[] => {
  const keys = Array.from(new Set(suggestions.map((s) => kindKey(s.kind))));
  return keys.sort((a, b) => {
    const ia = KIND_ORDER.indexOf(a);
    const ib = KIND_ORDER.indexOf(b);
    if (ia !== -1 || ib !== -1) return (ia === -1 ? 999 : ia) - (ib === -1 ? 999 : ib);
    return a.localeCompare(b, 'pt-BR');
  });
};

export const STATUS_LABEL: Record<string, string> = {
  Proposed: 'Pendente',
  Approved: 'Aprovado',
  Rejected: 'Rejeitado',
  Deferred: 'Adiado'
};

/** Aprovado = status 'Approved' (inclui "modify", que aprova com intervalo ajustado). */
export const isApproved = (s: EditSuggestion): boolean => s.status === 'Approved';
export const isPending = (s: EditSuggestion): boolean => s.status === 'Proposed';

/** Intervalo efetivo: ajustado por "modify" quando existe, senão o original. */
export const effectiveInterval = (s: EditSuggestion): Interval => ({
  start: s.effectiveStartSeconds ?? s.startSeconds,
  end: s.effectiveEndSeconds ?? s.endSeconds
});

/** true quando há intervalo efetivo diferente do original. */
export const isAdjusted = (s: EditSuggestion): boolean => {
  const eff = effectiveInterval(s);
  return eff.start !== s.startSeconds || eff.end !== s.endSeconds;
};

/** Selo "Revisão humana obrigatória": flag do host ou ação sugerida 'review'. */
export const requiresHumanReview = (s: EditSuggestion): boolean =>
  s.needsHumanReview || s.suggestedAction === 'review';

export const statusLabelOf = (s: EditSuggestion): string => {
  if (isApproved(s) && isAdjusted(s)) return 'Aprovado com ajuste';
  return STATUS_LABEL[s.status] ?? s.status;
};

export interface StatusCounts {
  all: number;
  pending: number;
  approved: number;
  rejected: number;
  deferred: number;
}

/** Contadores reais por status, calculados sobre a lista recebida do host. */
export const countByStatus = (suggestions: EditSuggestion[]): StatusCounts => {
  const counts: StatusCounts = { all: suggestions.length, pending: 0, approved: 0, rejected: 0, deferred: 0 };
  for (const s of suggestions) {
    if (s.status === 'Proposed') counts.pending += 1;
    else if (s.status === 'Approved') counts.approved += 1;
    else if (s.status === 'Rejected') counts.rejected += 1;
    else if (s.status === 'Deferred') counts.deferred += 1;
  }
  return counts;
};

export const filterSuggestions = (suggestions: EditSuggestion[], filters: ReviewFilters): EditSuggestion[] =>
  suggestions.filter((s) => {
    if (filters.category === 'cuts' && !isCutKind(s.kind)) return false;
    if (filters.category === 'zooms' && !isZoomKind(s.kind)) return false;
    if (filters.category === 'transitions' && !isTransitionKind(s.kind)) return false;

    if (filters.status === 'pending' && !isPending(s)) return false;
    if (filters.status === 'approved' && !isApproved(s)) return false;
    if (filters.status === 'rejected' && s.status !== 'Rejected') return false;
    if (filters.status === 'deferred' && s.status !== 'Deferred') return false;
    if (filters.kind !== 'all' && kindKey(s.kind) !== filters.kind) return false;
    if (filters.confidence !== 'all' && s.confidenceBand !== filters.confidence) return false;
    if (filters.humanOnly && !requiresHumanReview(s)) return false;
    return true;
  });

/**
 * Tempo removido vem SEMPRE do plano do host (summary.removedSourceUs da revisão mais recente).
 * Nunca é somado localmente a partir das sugestões aprovadas: o plano aplica regras que a lista não conhece.
 */
export type PlanRemovedState =
  | { status: 'loading' }
  | { status: 'none' }
  | { status: 'error' }
  | { status: 'ready'; revision: number; removedSourceUs: number };

/** Texto do tempo removido no plano (rótulo mostrado sob o contador de aprovados). */
export const planRemovedDetail = (state: PlanRemovedState): string => {
  switch (state.status) {
    case 'loading':
      return 'Lendo o plano para medir o tempo removido...';
    case 'none':
      return 'Gere o plano para ver o tempo removido';
    case 'error':
      return 'Não foi possível ler o plano para medir o tempo removido';
    case 'ready': {
      const seconds = Number.isFinite(state.removedSourceUs) ? state.removedSourceUs / 1_000_000 : NaN;
      return `Removido no plano r${state.revision}: ${Number.isFinite(seconds) ? `${seconds.toFixed(1).replace('.', ',')} s` : '—'}`;
    }
  }
};

/** Mensagem PT-BR para planError da decisão (a decisão continua gravada no host). */
export const decisionPlanErrorMessage = (code: string): string =>
  code === 'PLAN_REGENERATION_FAILED'
    ? 'Decisão gravada, mas o plano não pôde ser atualizado — gere nova revisão na Tela 03.'
    : `Decisão gravada, mas o plano não foi regenerado (código ${code}).`;

/**
 * Janela de revisão de um candidato, em segundos da mídia fonte (início limitado a zero).
 * Ancorada no intervalo ORIGINAL do candidato: um ajuste não desloca a própria janela.
 */
export const reviewWindowOf = (s: EditSuggestion): Interval => ({
  start: Math.max(0, s.startSeconds - REVIEW_WINDOW_SECONDS),
  end: s.endSeconds + REVIEW_WINDOW_SECONDS
});

/** Janela mostrada pelo waveform e pelo player (contexto de PLAYER_CONTEXT_SECONDS). */
export const playerWindowOf = (interval: Interval): Interval => ({
  start: Math.max(0, interval.start - PLAYER_CONTEXT_SECONDS),
  end: interval.end + PLAYER_CONTEXT_SECONDS
});

export const roundMs = (value: number): number => Math.round(value * 1000) / 1000;

/** Texto de campo numérico ("12.5", "12.367"); aceita vírgula ao ler. */
export const formatDraft = (value: number): string => String(roundMs(value));

export const parseDraft = (text: string): number | null => {
  const normalized = text.trim().replace(',', '.');
  if (normalized === '') return null;
  const value = Number(normalized);
  return Number.isFinite(value) ? roundMs(value) : null;
};

/** Valida o rascunho do editor de intervalo. Só habilita envio quando a janela e a ordem estão corretas. */
export const validateModifyDraft = (
  inText: string,
  outText: string,
  window: Interval
): { ok: true; start: number; end: number } | { ok: false; message: string } => {
  const start = parseDraft(inText);
  const end = parseDraft(outText);
  if (start === null || end === null) {
    return { ok: false, message: 'Informe entrada e saída como números em segundos.' };
  }
  if (start < 0 || end < 0) {
    return { ok: false, message: 'Entrada e saída não podem ser negativas.' };
  }
  if (end <= start) {
    return { ok: false, message: 'A saída deve ser maior que a entrada.' };
  }
  if (start < window.start || end > window.end) {
    return { ok: false, message: 'O intervalo deve ficar dentro da janela de revisão.' };
  }
  return { ok: true, start, end };
};

/** Desloca um valor em passos e mantém duas casas dentro da precisão de milissegundos. */
export const stepDraft = (text: string, delta: number): string => {
  const current = parseDraft(text);
  const base = current ?? 0;
  return formatDraft(Math.max(0, base + delta));
};

/** Palavras com algum trecho dentro da janela (início e fim das palavras com timestamp). */
export const wordsInWindow = (words: TranscriptWordDto[], window: Interval): TranscriptWordDto[] =>
  words.filter((w) => w.end > window.start && w.start < window.end);

/** Palavra que se sobrepõe ao intervalo (destaque). */
export const wordInInterval = (word: TranscriptWordDto, interval: Interval): boolean =>
  word.start < interval.end && word.end > interval.start;

/** Converte erro (IpcError com code ou Error comum) em mensagem visível em PT-BR. */
export const describeDecisionError = (err: unknown): ReviewErrorInfo => {
  const code =
    typeof err === 'object' && err !== null && 'code' in err && typeof (err as { code: unknown }).code === 'string'
      ? ((err as { code: string }).code)
      : null;
  const raw = err instanceof Error ? err.message : typeof err === 'string' ? err : '';
  const known: Record<string, string> = {
    INVALID_ACTION: 'Ação inválida para esta decisão.',
    INVALID_INTERVAL: 'Intervalo inválido: a entrada deve ser menor que a saída e ficar dentro da duração da mídia (mídia sem duração conhecida não pode ser ajustada).',
    SUGGESTION_NOT_FOUND: 'Candidato não encontrado. A lista será recarregada.',
    NOTHING_TO_UNDO: 'Nada para desfazer neste candidato.',
    NOTHING_TO_REDO: 'Nada para refazer neste candidato.'
  };
  if (code && known[code]) return { code, message: known[code] };
  return { code, message: raw || 'Não foi possível registrar a decisão.' };
};
