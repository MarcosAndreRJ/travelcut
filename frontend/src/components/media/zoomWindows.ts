/**
 * Zoom direto (edição por transcrição): helpers puros, sem DOM e sem IPC.
 * Tempos em segundos da FONTE. Escala e rampas espelham os limites do host (zoom.apply).
 * A prévia (CSS) é aproximada: o render final pode diferir levemente.
 */
import type { EditPlanDto, EditSuggestion } from '../../types/ipc';
import { zoomOriginCss } from './zoomPresentation';

/** Escalas oferecidas na UI (todas dentro de 1,01–1,12, limite do host). */
export const ZOOM_SCALE_OPTIONS: readonly number[] = [1.05, 1.08, 1.1, 1.12];
export const DEFAULT_ZOOM_SCALE = 1.08;
export const ZOOM_RAMP_MIN_SECONDS = 0.1;
export const ZOOM_RAMP_MAX_SECONDS = 1.5;
export const DEFAULT_ZOOM_RAMP_SECONDS = 0.35;

export type ZoomFocus = 'center' | 'top-third';

/** Foco enviado ao host (0–1). Centro = (0,5; 0,5); terço superior = (0,5; 0,33). */
export const zoomFocusPoint = (focus: ZoomFocus): { x: number; y: number } =>
  focus === 'top-third' ? { x: 0.5, y: 0.33 } : { x: 0.5, y: 0.5 };

/** Campos de tempo e efeito necessários para calcular a escala num instante. */
export interface ZoomTiming {
  startSec: number;
  endSec: number;
  scale: number;
  transitionInSeconds: number;
  transitionOutSeconds: number;
}

/** Zoom efetivo para a prévia: intervalo da fonte, escala, foco e rampas. */
export interface ZoomPreviewWindow extends ZoomTiming {
  focalPointX: number;
  focalPointY: number;
}

/** Zoom efetivo de um clipe, com a identidade do host (alvo de 'Remover zoom'). */
export interface SourceZoomWindow extends ZoomPreviewWindow {
  suggestionId: string;
  mediaId: string;
}

const EPS = 1e-6;
const isNum = (v: unknown): v is number => typeof v === 'number' && Number.isFinite(v);
const clamp = (v: number, lo: number, hi: number): number => Math.min(hi, Math.max(lo, v));

/** Escala exibida em PT-BR: 1,08×. */
export const formatZoomScale = (scale: number): string => `${scale.toFixed(2).replace('.', ',')}×`;

/** Escala suportada pela UI (lista fixa, tolerância numérica). */
export const isSupportedZoomScale = (scale: number): boolean =>
  ZOOM_SCALE_OPTIONS.some((option) => Math.abs(option - scale) < EPS);

/**
 * Valida a entrada do zoom antes de chamar o host. Retorna a mensagem de erro (PT-BR) ou null quando válida.
 * Rampas: cada uma entre 0,1 e 1,5 s e a soma não pode passar da duração da fala.
 */
export function validateZoomInput(input: {
  startSec: number;
  endSec: number;
  scale: number;
  transitionInSeconds: number;
  transitionOutSeconds: number;
}): string | null {
  if (!isSupportedZoomScale(input.scale)) return 'Escala não suportada: use 1,05×, 1,08×, 1,10× ou 1,12×.';
  const duration = input.endSec - input.startSec;
  if (!isNum(duration) || duration <= 0) return 'O trecho selecionado não tem duração.';
  const rampsOk = [input.transitionInSeconds, input.transitionOutSeconds].every(
    (r) => isNum(r) && r >= ZOOM_RAMP_MIN_SECONDS - EPS && r <= ZOOM_RAMP_MAX_SECONDS + EPS
  );
  if (!rampsOk) return 'As rampas de entrada e saída devem estar entre 0,1 s e 1,5 s.';
  const sum = input.transitionInSeconds + input.transitionOutSeconds;
  if (sum > duration + EPS) {
    return `As rampas (${sum.toFixed(2).replace('.', ',')} s) excedem a duração da fala (${duration.toFixed(2).replace('.', ',')} s).`;
  }
  return null;
}

/**
 * Escala do zoom no instante t (fonte). Fora do intervalo [start, end) = 1.
 * Rampa de entrada: linear de 1 até a escala em [start, start+rIn). Plato: escala.
 * Rampa de saída: linear de volta a 1 em [end-rOut, end). Rampas são limitadas à duração.
 */
export function zoomScaleAtSource(zoom: ZoomTiming, t: number): number {
  if (!isNum(t) || !isNum(zoom.startSec) || !isNum(zoom.endSec)) return 1;
  const duration = zoom.endSec - zoom.startSec;
  if (!(duration > 0) || !(zoom.scale > 1)) return 1;
  if (t < zoom.startSec || t >= zoom.endSec) return 1;
  const rIn = clamp(isNum(zoom.transitionInSeconds) ? zoom.transitionInSeconds : 0, 0, duration);
  const rOut = clamp(isNum(zoom.transitionOutSeconds) ? zoom.transitionOutSeconds : 0, 0, duration - rIn);
  const elapsed = t - zoom.startSec;
  const remaining = zoom.endSec - t;
  const delta = zoom.scale - 1;
  if (rIn > 0 && elapsed < rIn) return 1 + delta * (elapsed / rIn);
  if (rOut > 0 && remaining < rOut) return 1 + delta * (remaining / rOut);
  return zoom.scale;
}

/** Transform da prévia no instante t: escala e origem (foco) do primeiro zoom que contém t. */
export function zoomTransformAtSource(
  windows: readonly ZoomPreviewWindow[],
  t: number
): { scale: number; origin: string } {
  for (const w of windows) {
    if (isNum(t) && t >= w.startSec && t < w.endSec) {
      return { scale: zoomScaleAtSource(w, t), origin: zoomOriginCss(w.focalPointX, w.focalPointY) };
    }
  }
  return { scale: 1, origin: '50% 50%' };
}

/** Instante de busca para conferir o zoom: fim da rampa de entrada (ou o meio da fala, se a rampa couber mal). */
export function zoomPreviewSeekSeconds(startSec: number, endSec: number, transitionInSeconds: number): number {
  const afterRamp = startSec + Math.max(0, transitionInSeconds);
  return afterRamp < endSec ? afterRamp : (startSec + endSec) / 2;
}

/** Zoom efetivos do plano vigente (appliedEffects 'zoom' de origem humana com janela na fonte). */
export function zoomWindowsFromPlan(plan: EditPlanDto | null | undefined): SourceZoomWindow[] {
  return (plan?.appliedEffects ?? [])
    .filter(
      (e) =>
        e.kind === 'zoom' &&
        e.origin === 'human' &&
        isNum(e.sourceInUs) &&
        isNum(e.sourceOutUs) &&
        e.sourceOutUs > e.sourceInUs &&
        isNum(e.scale) &&
        e.scale > 1
    )
    .map((e) => {
      const startSec = (e.sourceInUs as number) / 1_000_000;
      const endSec = (e.sourceOutUs as number) / 1_000_000;
      return {
        suggestionId: e.suggestionId,
        mediaId: e.mediaId,
        startSec,
        endSec,
        scale: e.scale as number,
        focalPointX: isNum(e.focalPointX) ? e.focalPointX : 0.5,
        focalPointY: isNum(e.focalPointY) ? e.focalPointY : 0.5,
        transitionInSeconds: isNum(e.transitionInSeconds) ? e.transitionInSeconds : 0,
        transitionOutSeconds: isNum(e.transitionOutSeconds) ? e.transitionOutSeconds : 0
      };
    })
    .sort((a, b) => a.startSec - b.startSec);
}

/**
 * Zoom efetivos a partir de suggestions.list: suggestedAction 'zoom', status 'Approved', parameters.zoom.
 * Usado quando o plano não traz zoom humano (ex.: plano não regenerado). Intervalo efetivo quando existir.
 */
export function zoomWindowsFromSuggestions(list: readonly EditSuggestion[]): SourceZoomWindow[] {
  return list
    .filter((s) => s.suggestedAction === 'zoom' && s.status === 'Approved' && s.parameters?.zoom)
    .map((s) => {
      const z = s.parameters?.zoom;
      const startSec = s.effectiveStartSeconds ?? s.startSeconds;
      const endSec = s.effectiveEndSeconds ?? s.endSeconds;
      return {
        suggestionId: s.suggestionId,
        mediaId: s.mediaId,
        startSec,
        endSec,
        scale: z?.scale ?? 1,
        focalPointX: isNum(z?.focalPointX) ? (z?.focalPointX as number) : 0.5,
        focalPointY: isNum(z?.focalPointY) ? (z?.focalPointY as number) : 0.5,
        transitionInSeconds: isNum(z?.transitionInSeconds) ? (z?.transitionInSeconds as number) : 0,
        transitionOutSeconds: isNum(z?.transitionOutSeconds) ? (z?.transitionOutSeconds as number) : 0
      };
    })
    .filter((w) => isNum(w.startSec) && isNum(w.endSec) && w.endSec > w.startSec && w.scale > 1);
}

/** Zoom do plano quando há; senão, os aprovados em suggestions.list (fallback honesto). */
export function resolveZoomWindows(plan: EditPlanDto | null | undefined, suggestions: readonly EditSuggestion[]): SourceZoomWindow[] {
  const fromPlan = zoomWindowsFromPlan(plan);
  return fromPlan.length > 0 ? fromPlan : zoomWindowsFromSuggestions(suggestions);
}
