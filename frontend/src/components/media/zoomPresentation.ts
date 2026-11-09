/**
 * Derivação pura do zoom do plano para a prévia (CSS sobre o <video>). Aproximado: não é o render.
 * Foco ausente = centro (50%). Duração ausente = clipe inteiro. Curva (punch_in/push_in) é só rotulada.
 */

export interface PlanZoomFields {
  zoom?: number | null;
  zoomFocalPointX?: number | null;
  zoomFocalPointY?: number | null;
  /** Segundos na linha do tempo do clipe. null/ausente = clipe inteiro. */
  zoomDurationSeconds?: number | null;
  zoomCurve?: string | null;
}

export interface ZoomPresentation {
  scale: number;
  /** Origem do transform em porcentagem (0–100). */
  originX: number;
  originY: number;
}

const clamp01 = (v: number): number => Math.min(1, Math.max(0, v));

/** Foco 0–1 -> porcentagem para transform-origin. Ausente ou inválido = 50%. */
export const zoomFocusPercent = (value: number | null | undefined): number =>
  typeof value === 'number' && Number.isFinite(value) ? clamp01(value) * 100 : 50;

/** Valor CSS de transform-origin a partir do foco do plano. */
export const zoomOriginCss = (focalX: number | null | undefined, focalY: number | null | undefined): string =>
  `${zoomFocusPercent(focalX)}% ${zoomFocusPercent(focalY)}%`;

/**
 * Zoom ativo do clipe no instante `elapsedSeconds` (tempo decorrido no clipe, na linha do tempo).
 * Null quando não há zoom (ausente, escala <= 1, fora do instante ou duração já vencida).
 */
export const planZoomActiveAt = (fields: PlanZoomFields, elapsedSeconds: number): ZoomPresentation | null => {
  const scale = fields.zoom;
  if (typeof scale !== 'number' || !Number.isFinite(scale) || scale <= 1) return null;
  if (!Number.isFinite(elapsedSeconds) || elapsedSeconds < 0) return null;
  const duration = fields.zoomDurationSeconds;
  if (typeof duration === 'number' && Number.isFinite(duration) && elapsedSeconds >= duration) return null;
  return {
    scale,
    originX: zoomFocusPercent(fields.zoomFocalPointX),
    originY: zoomFocusPercent(fields.zoomFocalPointY)
  };
};

/** Rótulo honesto da curva: aproximada, nunca a curva do render. */
export const zoomCurveLabel = (curve: string | null | undefined): string =>
  curve === 'push_in' ? 'push-in aproximado (CSS linear)' : 'punch-in aproximado (CSS linear)';
