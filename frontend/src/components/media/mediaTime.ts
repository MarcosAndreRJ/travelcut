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
