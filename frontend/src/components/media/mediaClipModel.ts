import type { PlaybackSourceDto } from '../../types/ipc';
import { mediaElementErrorText } from './mediaTime';
import type { PlaybackSourceState } from './usePlaybackSource';
import { isPlayableSource } from './usePlaybackSource';

// Regras puras do player de clipe completo. Tempos em segundos da fonte. Nunca frame-accurate.

/** Pedido de busca vindo de fora do player. O token permite repetir o mesmo instante. */
export interface ClipSeekRequest {
  seconds: number;
  token: number;
}

export type ClipPlayerNotice =
  | { kind: 'none' }
  | { kind: 'loading' }
  | { kind: 'source_error'; message: string }
  | { kind: 'unavailable'; message: string; offerProxy: boolean }
  | { kind: 'media_error'; message: string; offerProxy: boolean }
  | { kind: 'no_video_track'; message: string; offerProxy: boolean };

/** Intervalo mínimo entre avisos de tempo: a tela acompanha o vídeo sem renderizar a cada quadro. */
export const CLIP_TIME_NOTIFY_SECONDS = 0.25;

const NO_VIDEO_TRACK_MESSAGE =
  'O áudio carregou, mas o WebView2 não decodificou a imagem deste vídeo (codec sem suporte, como HEVC/H.265).';

const canOfferProxy = (source: PlaybackSourceDto | null): boolean =>
  source !== null && source.proxyStatus !== 'ffmpeg_unavailable';

/**
 * O que cobre a área de vídeo. Decodificador ausente pode não gerar erro: o elemento carrega só o áudio
 * com videoWidth 0, e sem este aviso o usuário veria tela preta.
 */
export function resolveClipNotice(input: {
  state: PlaybackSourceState;
  mediaErrorCode: number | null | undefined;
  hasMediaError: boolean;
  noVideoTrack: boolean;
}): ClipPlayerNotice {
  const { state, hasMediaError, mediaErrorCode, noVideoTrack } = input;
  if (state.kind === 'loading') return { kind: 'loading' };
  if (state.kind === 'error') return { kind: 'source_error', message: state.message };

  const { source } = state;
  if (!isPlayableSource(source)) {
    return {
      kind: 'unavailable',
      message: source.reason ?? 'Esta mídia não tem fonte reproduzível no momento.',
      offerProxy: canOfferProxy(source)
    };
  }
  if (hasMediaError) {
    return {
      kind: 'media_error',
      message: `Não foi possível reproduzir: ${mediaElementErrorText(mediaErrorCode ?? null)}.`,
      offerProxy: canOfferProxy(source) && source.status === 'original'
    };
  }
  if (noVideoTrack) {
    return { kind: 'no_video_track', message: NO_VIDEO_TRACK_MESSAGE, offerProxy: canOfferProxy(source) && source.status === 'original' };
  }
  return { kind: 'none' };
}

/** Texto do estado do proxy; null quando não há nada a informar. */
export function proxyIndicator(source: PlaybackSourceDto | null, proxyBusy: boolean): string | null {
  if (proxyBusy || source?.proxyStatus === 'generating') return 'Gerando proxy…';
  if (!source) return null;
  if (source.status === 'proxy') return 'Reproduzindo proxy (arquivo original intacto)';
  if (source.proxyStatus === 'ready') return 'Proxy disponível';
  if (source.proxyStatus === 'failed') return 'A última geração de proxy falhou';
  return null;
}

/** Posição clicada na faixa temporal, em segundos. null sem largura ou duração válidas. */
export function stripPositionToSeconds(clientX: number, left: number, width: number, durationSeconds: number): number | null {
  if (!(width > 0) || !(durationSeconds > 0)) return null;
  const ratio = Math.min(1, Math.max(0, (clientX - left) / width));
  return ratio * durationSeconds;
}

/** Limita a busca à duração (quando conhecida). Nunca devolve NaN nem valor negativo. */
export function clampSeek(seconds: number, durationSeconds: number | null): number {
  const value = Number.isFinite(seconds) ? Math.max(0, seconds) : 0;
  return durationSeconds !== null && durationSeconds > 0 ? Math.min(value, durationSeconds) : value;
}

const hasKind = (asset: { kind?: string | null }, kind: string): boolean => asset.kind?.toLowerCase() === kind;
export const isImageAsset = (asset: { kind?: string | null }): boolean => hasKind(asset, 'image');
export const isAudioAsset = (asset: { kind?: string | null }): boolean => hasKind(asset, 'audio');

/** Tempo mm:ss (ou h:mm:ss). Valor inválido vira "--:--". */
export function formatPlayerTime(seconds: number | null | undefined): string {
  if (seconds === null || seconds === undefined || !Number.isFinite(seconds) || seconds < 0) return '--:--';
  const total = Math.floor(seconds);
  const pad = (n: number): string => String(n).padStart(2, '0');
  const h = Math.floor(total / 3600);
  const m = Math.floor((total % 3600) / 60);
  return h > 0 ? `${h}:${pad(m)}:${pad(total % 60)}` : `${pad(m)}:${pad(total % 60)}`;
}
