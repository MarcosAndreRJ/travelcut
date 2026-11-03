import { IpcError, ipc } from './ipcBridge';
import type { PlaybackSourceDto, ProxyJobStatusDto, WaveformDto } from '../types/ipc';

/**
 * Camada tipada da prévia (F07): media.getPlaybackSource, media.getWaveform, media.proxy.start/status.
 * Nenhuma regra de negócio aqui. Fora do WebView2 (ipc.isMock) devolve respostas de DEMONSTRAÇÃO
 * honestas: sem URL de vídeo, sem picos e sem job de proxy real.
 */

/** Backend calcula a waveform em até 60 s; o timeout do cliente precisa ser maior. */
const WAVEFORM_TIMEOUT_MS = 65000;
const PROXY_START_TIMEOUT_MS = 15000;

export const DEMO_PLAYBACK_REASON = 'Modo demonstração: sem arquivos de mídia';
export const DEMO_WAVEFORM_REASON = 'Modo demonstração: forma de onda indisponível (FFmpeg não encontrado)';
export const DEMO_PROXY_REASON = 'Modo demonstração: proxy indisponível (FFmpeg não encontrado)';
const DEMO_PROXY_JOB_ID = 'demo-proxy-indisponivel';

export interface WaveformRequest {
  projectId: string;
  mediaId: string;
  startSeconds: number;
  endSeconds: number;
  buckets: number;
}

export interface ProxyStartResponse {
  jobId: string;
}

/** Fonte de reprodução de uma mídia (URL somente leitura servida pelo host). */
export async function getPlaybackSource(projectId: string, mediaId: string): Promise<PlaybackSourceDto> {
  if (ipc.isMock) {
    return {
      mediaId,
      status: 'unavailable',
      url: null,
      mimeType: null,
      codec: null,
      likelyPlayable: null,
      proxyStatus: 'ffmpeg_unavailable',
      reason: DEMO_PLAYBACK_REASON
    };
  }
  return ipc.send<PlaybackSourceDto>('media.getPlaybackSource', { projectId, mediaId });
}

/** Picos reais de áudio do intervalo. Em demonstração nunca há picos. */
export async function getWaveform(request: WaveformRequest): Promise<WaveformDto> {
  if (ipc.isMock) {
    return {
      status: 'ffmpeg_unavailable',
      startSeconds: request.startSeconds,
      bucketSeconds: 0,
      peaks: [],
      reason: DEMO_WAVEFORM_REASON
    };
  }
  return ipc.send<WaveformDto>('media.getWaveform', request, { timeoutMs: WAVEFORM_TIMEOUT_MS });
}

/** Inicia a geração de proxy para as mídias informadas. Erro PROXY_IN_PROGRESS se já houver job ativo. */
export async function startProxy(projectId: string, mediaIds: string[]): Promise<ProxyStartResponse> {
  if (ipc.isMock) {
    return { jobId: DEMO_PROXY_JOB_ID };
  }
  return ipc.send<ProxyStartResponse>(
    'media.proxy.start',
    { projectId, mediaIds },
    { timeoutMs: PROXY_START_TIMEOUT_MS }
  );
}

/** Estado do job de proxy (media.proxy.status exige projectId e jobId). Em demonstração termina como ffmpeg_unavailable. */
export async function getProxyStatus(projectId: string, jobId: string): Promise<ProxyJobStatusDto> {
  if (ipc.isMock) {
    return {
      jobId,
      state: 'ffmpeg_unavailable',
      processed: 0,
      total: 0,
      currentMedia: null,
      items: []
    };
  }
  return ipc.send<ProxyJobStatusDto>('media.proxy.status', { projectId, jobId });
}

/** Mensagem PT-BR para erros da prévia. Códigos estáveis do host têm texto próprio. */
export function previewErrorMessage(err: unknown): string {
  if (err instanceof IpcError) {
    switch (err.code) {
      case 'PROXY_IN_PROGRESS':
        return 'Já existe um proxy em geração neste projeto. Aguarde o término e tente novamente.';
      case 'PROJECT_NOT_FOUND':
        return 'Projeto não encontrado no host. Reabra o projeto e tente novamente.';
      default:
        break;
    }
  }
  return err instanceof Error ? err.message : String(err);
}
