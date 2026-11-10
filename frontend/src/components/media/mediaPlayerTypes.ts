// Contrato do player de mídia compartilhado (F07). Implementação: components/media/MediaIntervalPlayer.tsx.
// Reprodução real via <video> com URL de media.getPlaybackSource. Sem atributo crossOrigin
// (o host de mídia usa DenyCors). Nunca declarar frame-accurate.

import type { KeptSegment, SourceWindow } from './mediaTime';

export interface MediaIntervalPlayerProps {
  /**
   * Modo "after": trechos da fonte a pular (plano vigente). Sem valor, pula só [startSeconds, endSeconds).
   */
  removedIntervals?: SourceWindow[] | null;
  /**
   * Modo "after": trechos MANTIDOS do plano com taxa e zoom por clipe (prévia aproximada).
   * Ausente = taxa de `playbackRate` e sem zoom do plano.
   */
  keptSegments?: KeptSegment[] | null;
  projectId: string;
  mediaId: string;
  /** Intervalo de origem a destacar/reproduzir, em segundos da mídia fonte. */
  startSeconds: number;
  endSeconds: number;
  /** Segundos de contexto antes/depois do intervalo (padrão 2). */
  contextSeconds?: number;
  /**
   * "before" toca o trecho inteiro com contexto (o que existe hoje);
   * "after" toca o contexto pulando [startSeconds, endSeconds) (como ficaria com o corte).
   */
  mode?: 'before' | 'after';
  /** Velocidade de reprodução (ex.: 2 para B-roll 2×). Padrão 1. */
  playbackRate?: number;
  /** Notifica o tempo atual da mídia (segundos da fonte) para sincronizar transcrição/waveform. */
  onTimeUpdate?: (sourceSeconds: number) => void;
  /** Busca externa (ex.: clique numa palavra da transcrição). Mudança do valor dispara seek. */
  seekToSeconds?: number | null;
  className?: string;
  /** Altura da área de vídeo (padrão: aspect-video na largura toda). */
  videoAreaClassName?: string;
  /** Enquadramento / zoom editorial ao vivo (F14.1 Checkpoint E). */
  zoomTransform?: {
    scale: number;
    offsetX: number;
    offsetY: number;
    showSafeArea?: boolean;
  } | null;
}
