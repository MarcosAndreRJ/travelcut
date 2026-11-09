/**
 * Helpers puros do player de mídia (F07). Sem DOM e sem IPC: tempos em segundos da FONTE.
 * Nada aqui converte para timecode de timeline. Entradas inválidas viram valores neutros.
 */

export interface SourceWindow {
  start: number;
  end: number;
}

/** Tolerância (s) para considerar que o elemento chegou ao fim de uma janela. */
export const WINDOW_EDGE_EPSILON_S = 0.02;

const finiteOr = (value: number, fallback: number): number => (Number.isFinite(value) ? value : fallback);

/**
 * Janela de reprodução [start−ctx, end+ctx], limitada em 0 e na duração do elemento
 * quando ela é conhecida (duration finita e positiva). Sem duração, o fim não é limitado.
 */
export const contextWindow = (
  startSeconds: number,
  endSeconds: number,
  contextSeconds: number,
  durationSeconds: number | null
): SourceWindow => {
  const start = finiteOr(startSeconds, 0);
  const end = Math.max(start, finiteOr(endSeconds, start));
  const ctx = Math.max(0, finiteOr(contextSeconds, 0));
  const hasDuration = durationSeconds !== null && Number.isFinite(durationSeconds) && durationSeconds > 0;
  let windowStart = Math.max(0, start - ctx);
  let windowEnd = end + ctx;
  if (hasDuration) {
    windowEnd = Math.min(windowEnd, durationSeconds);
    windowStart = Math.min(windowStart, durationSeconds);
  }
  return { start: windowStart, end: Math.max(windowStart, windowEnd) };
};

/** Limita um valor à janela e à duração (quando conhecida). Nunca retorna NaN. */
export const clampToWindow = (value: number, window: SourceWindow): number => {
  const v = finiteOr(value, window.start);
  return Math.min(window.end, Math.max(window.start, v));
};

/**
 * Modo "after": durante a reprodução, [start, end) é removido. Retorna o instante para o qual
 * saltar (end) quando o tempo atual está dentro do trecho removido; senão null.
 */
export const afterModeSkipTarget = (currentSeconds: number, startSeconds: number, endSeconds: number): number | null => {
  if (!Number.isFinite(currentSeconds) || !Number.isFinite(startSeconds) || !Number.isFinite(endSeconds)) return null;
  if (endSeconds <= startSeconds) return null;
  return currentSeconds >= startSeconds && currentSeconds < endSeconds ? endSeconds : null;
};

/** Fim "de cauda" finito para trechos removidos até o fim da mídia (o elemento limita ao duration). */
const TRAILING_END_S = 1e12;

/**
 * Trechos da FONTE removidos pelo plano vigente para uma mídia: complemento dos trechos mantidos
 * (SourceInUs/SourceOutUs em microssegundos). Inclui o início antes do 1º trecho e as lacunas entre trechos.
 * Mídia sem nenhum trecho no plano resulta em [0, fim): nada dela entra no "Depois".
 */
export const removedIntervalsFromPlan = (
  clips: ReadonlyArray<{ mediaId: string; sourceInUs: number; sourceOutUs: number }>,
  mediaId: string
): SourceWindow[] => {
  const kept = clips
    .filter((c) => c.mediaId === mediaId && Number.isFinite(c.sourceInUs) && Number.isFinite(c.sourceOutUs) && c.sourceOutUs > c.sourceInUs)
    .map((c) => ({ start: c.sourceInUs / 1_000_000, end: c.sourceOutUs / 1_000_000 }))
    .sort((a, b) => a.start - b.start);
  const removed: SourceWindow[] = [];
  let cursor = 0;
  for (const k of kept) {
    if (k.start > cursor) removed.push({ start: cursor, end: k.start });
    cursor = Math.max(cursor, k.end);
  }
  removed.push({ start: cursor, end: TRAILING_END_S });
  return removed;
};

/**
 * Modo "after" com vários trechos removidos: se o tempo atual cai em um deles, retorna o fim desse trecho
 * (para onde saltar). Trechos inválidos são ignorados. Null quando o tempo já está em trecho mantido.
 */
export const afterModeSkipTargets = (currentSeconds: number, windows: readonly SourceWindow[]): number | null => {
  if (!Number.isFinite(currentSeconds)) return null;
  for (const w of windows) {
    if (!Number.isFinite(w.start) || !Number.isFinite(w.end) || w.end <= w.start) continue;
    if (currentSeconds >= w.start && currentSeconds < w.end) return w.end;
  }
  return null;
};

/**
 * Trecho MANTIDO da fonte pelo plano vigente, com a taxa e o zoom do clipe que o contém.
 * rate = speedNum/speedDen (1 quando inválido). zoom = escala do clipe quando > 1; senão null.
 * Prévia aproximada: taxa e zoom são reproduzidos pelo HTML5, não são o render final.
 */
export interface KeptSegment {
  start: number;
  end: number;
  rate: number;
  zoom: number | null;
}

/** Trechos mantidos de uma mídia no plano, ordenados por início da fonte. Mídias de outros clipes são ignoradas. */
export const keptSegmentsFromPlan = (
  clips: ReadonlyArray<{
    mediaId: string;
    sourceInUs: number;
    sourceOutUs: number;
    speedNum: number;
    speedDen: number;
    zoom?: number | null;
  }>,
  mediaId: string
): KeptSegment[] =>
  clips
    .filter((c) => c.mediaId === mediaId && Number.isFinite(c.sourceInUs) && Number.isFinite(c.sourceOutUs) && c.sourceOutUs > c.sourceInUs)
    .map((c) => ({
      start: c.sourceInUs / 1_000_000,
      end: c.sourceOutUs / 1_000_000,
      rate: c.speedNum > 0 && c.speedDen > 0 && Number.isFinite(c.speedNum / c.speedDen) ? c.speedNum / c.speedDen : 1,
      zoom: typeof c.zoom === 'number' && Number.isFinite(c.zoom) && c.zoom > 1 ? c.zoom : null
    }))
    .sort((a, b) => a.start - b.start);

/** Trecho mantido que contém o instante (fonte, segundos), ou null quando o instante está num trecho removido. */
export const keptSegmentAt = (seconds: number, segments: readonly KeptSegment[]): KeptSegment | null => {
  if (!Number.isFinite(seconds)) return null;
  for (const s of segments) {
    if (seconds >= s.start && seconds < s.end) return s;
  }
  return null;
};

/** Segundos da fonte com 3 casas e separador PT-BR: "12,345 s". */
export const formatSourceSeconds = (seconds: number): string => {
  if (!Number.isFinite(seconds)) return '—';
  const ms = Math.round(Math.max(0, seconds) * 1000);
  const whole = Math.floor(ms / 1000);
  const frac = String(ms % 1000).padStart(3, '0');
  return `${whole},${frac} s`;
};

/** Códigos de MediaError do HTML (valores fixos da especificação). */
export const MEDIA_ERR_NETWORK = 2;
export const MEDIA_ERR_DECODE = 3;
export const MEDIA_ERR_SRC_NOT_SUPPORTED = 4;

/** Texto PT-BR para o código do erro do elemento <video>. Nunca inventa causa. */
export const mediaElementErrorText = (code: number | null): string => {
  switch (code) {
    case MEDIA_ERR_SRC_NOT_SUPPORTED:
      return 'formato ou codec não suportado pelo WebView2 (MEDIA_ERR_SRC_NOT_SUPPORTED)';
    case MEDIA_ERR_DECODE:
      return 'erro ao decodificar o vídeo';
    case MEDIA_ERR_NETWORK:
      return 'erro de leitura do arquivo pelo host de mídia';
    default:
      return 'erro desconhecido ao carregar o vídeo';
  }
};
