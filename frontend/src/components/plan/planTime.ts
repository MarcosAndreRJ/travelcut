import type { TimelineTimeBaseDto } from '../../types/ipc';

/**
 * Helpers puros de tempo do Plano editorial (F06).
 * Convenções: fonte em microssegundos (sourceInUs/sourceOutUs); timeline em frames do timeBase
 * (frameDurationNum/frameDurationDen segundos por frame). Nada aqui estima valores: entradas
 * inválidas viram '—'.
 */

const PLACEHOLDER = '—';

const pad2 = (n: number): string => String(n).padStart(2, '0');
const pad3 = (n: number): string => String(n).padStart(3, '0');

/** true quando o timeBase tem fração positiva segundos/frame. */
export const isValidTimeBase = (timeBase: TimelineTimeBaseDto | null | undefined): timeBase is TimelineTimeBaseDto =>
  !!timeBase &&
  Number.isFinite(timeBase.frameDurationNum) &&
  Number.isFinite(timeBase.frameDurationDen) &&
  timeBase.frameDurationNum > 0 &&
  timeBase.frameDurationDen > 0;

/** FPS nominal para rótulos NDF: 29,97 → 30; 23,976 → 24; 59,94 → 60. */
export const nominalFps = (timeBase: TimelineTimeBaseDto): number =>
  Math.max(1, Math.round(timeBase.frameDurationDen / timeBase.frameDurationNum));

/** Converte frames da timeline em segundos reais (num/den). NaN quando o timeBase é inválido. */
export const framesToSeconds = (frames: number, timeBase: TimelineTimeBaseDto): number => {
  if (!isValidTimeBase(timeBase) || !Number.isFinite(frames)) return NaN;
  return (frames * timeBase.frameDurationNum) / timeBase.frameDurationDen;
};

/** Converte frames em segundos reais arredondando para o frame mais próximo. */
export const secondsToFrames = (seconds: number, timeBase: TimelineTimeBaseDto): number => {
  if (!isValidTimeBase(timeBase) || !Number.isFinite(seconds)) return NaN;
  return Math.round((seconds * timeBase.frameDurationDen) / timeBase.frameDurationNum);
};

/** Microssegundos da fonte para segundos. */
export const usToSeconds = (us: number): number => (Number.isFinite(us) ? us / 1_000_000 : NaN);

/**
 * Timecode NDF (não drop-frame) HH:MM:SS:FF com fps nominal arredondado.
 * Obs.: em 29,97 o rótulo NDF avança 30 quadros por segundo de rótulo e diverge do tempo real;
 * por isso a UI mostra os segundos reais ao lado (framesToSeconds).
 */
export const formatTimecode = (frames: number, timeBase: TimelineTimeBaseDto): string => {
  if (!isValidTimeBase(timeBase) || !Number.isFinite(frames)) return PLACEHOLDER;
  const fps = nominalFps(timeBase);
  const total = Math.max(0, Math.round(frames));
  const ff = total % fps;
  const totalSeconds = Math.floor(total / fps);
  const ss = totalSeconds % 60;
  const mm = Math.floor(totalSeconds / 60) % 60;
  const hh = Math.floor(totalSeconds / 3600);
  return `${pad2(hh)}:${pad2(mm)}:${pad2(ss)}:${pad2(ff)}`;
};

/** Timecode de fonte baseado em tempo: HH:MM:SS.mmm a partir de microssegundos. */
export const formatSourceTimecode = (us: number): string => {
  if (!Number.isFinite(us)) return PLACEHOLDER;
  const totalMs = Math.max(0, Math.round(us / 1000));
  const ms = totalMs % 1000;
  const totalSeconds = Math.floor(totalMs / 1000);
  const ss = totalSeconds % 60;
  const mm = Math.floor(totalSeconds / 60) % 60;
  const hh = Math.floor(totalSeconds / 3600);
  return `${pad2(hh)}:${pad2(mm)}:${pad2(ss)}.${pad3(ms)}`;
};

/** Segundos reais com exatamente 3 casas decimais, separador PT-BR: 12,345 s. */
export const formatSeconds3 = (seconds: number): string => {
  if (!Number.isFinite(seconds)) return PLACEHOLDER;
  const ms = Math.round(Math.max(0, seconds) * 1000);
  return `${Math.floor(ms / 1000)},${pad3(ms % 1000)} s`;
};

/** Duração legível: 1h 02min 05s, 2min 05s, 5s. Valores inválidos viram '—'. */
export const formatDuration = (seconds: number): string => {
  if (!Number.isFinite(seconds) || seconds < 0) return PLACEHOLDER;
  const total = Math.round(seconds);
  const hh = Math.floor(total / 3600);
  const mm = Math.floor((total % 3600) / 60);
  const ss = total % 60;
  if (hh > 0) return `${hh}h ${pad2(mm)}min ${pad2(ss)}s`;
  if (mm > 0) return `${mm}min ${pad2(ss)}s`;
  return `${ss}s`;
};

/** Velocidade a partir de speedNum/speedDen: 2/1 → "2×", 1/2 → "0,5×". */
export const formatSpeed = (speedNum: number, speedDen: number): string => {
  if (!(speedNum > 0) || !(speedDen > 0)) return PLACEHOLDER;
  const ratio = speedNum / speedDen;
  const text = Number.isInteger(ratio)
    ? String(ratio)
    : ratio.toFixed(3).replace(/0+$/, '').replace(/\.$/, '');
  return `${text.replace('.', ',')}×`;
};

/** true quando a velocidade é a normal (1×). */
export const isNormalSpeed = (speedNum: number, speedDen: number): boolean =>
  speedNum > 0 && speedDen > 0 && speedNum === speedDen;

/** Zoom sugerido (fator): 1.05 → "1,05×". */
export const formatZoom = (zoom: number): string =>
  Number.isFinite(zoom) ? `${zoom.toFixed(2).replace('.', ',')}×` : PLACEHOLDER;
