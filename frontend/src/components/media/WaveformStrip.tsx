import React, { useEffect, useState } from 'react';
import { AlertCircle, Loader2 } from 'lucide-react';
import type { WaveformDto } from '../../types/ipc';
import { getWaveform, previewErrorMessage } from '../../services/previewApi';

export interface WaveformStripProps {
  projectId: string;
  mediaId: string;
  /** Intervalo da FONTE em segundos. */
  startSeconds: number;
  endSeconds: number;
  /** Trecho destacado (mesma base de tempo da fonte). */
  highlight?: { start: number; end: number };
  /** Posição atual da FONTE, desenhada como agulha. */
  currentSeconds?: number | null;
  buckets?: number;
  className?: string;
}

export const DEFAULT_WAVEFORM_BUCKETS = 240;

type WaveformState =
  | { kind: 'idle' }
  | { kind: 'loading' }
  | { kind: 'ok'; data: WaveformDto }
  | { kind: 'unavailable'; reason: string }
  | { kind: 'error'; message: string };

/** Texto PT-BR para status sem picos. A razão do host prevalece quando existir. */
export const waveformUnavailableText = (data: WaveformDto): string => {
  if (data.reason) return data.reason;
  switch (data.status) {
    case 'ffmpeg_unavailable':
      return 'Forma de onda indisponível: FFmpeg não encontrado';
    case 'no_audio':
      return 'Forma de onda indisponível: a mídia não tem faixa de áudio';
    case 'failed':
      return 'Forma de onda indisponível: falha ao analisar o áudio';
    default:
      return 'Forma de onda indisponível';
  }
};

const clampPeak = (value: number): number => (Number.isFinite(value) ? Math.min(1, Math.max(0, value)) : 0);

/**
 * Faixa de forma de onda (SVG) com picos REAIS de media.getWaveform. Sem picos reais não desenha
 * nada: mostra o motivo retornado pelo host. Nunca gera picos sintéticos.
 */
export const WaveformStrip: React.FC<WaveformStripProps> = ({
  projectId,
  mediaId,
  startSeconds,
  endSeconds,
  highlight,
  currentSeconds = null,
  buckets = DEFAULT_WAVEFORM_BUCKETS,
  className = ''
}) => {
  const [state, setState] = useState<WaveformState>({ kind: 'idle' });
  const span = endSeconds - startSeconds;
  const validSpan = Number.isFinite(span) && span > 0;

  useEffect(() => {
    if (!validSpan) {
      setState({ kind: 'unavailable', reason: 'Intervalo inválido: fim não é posterior ao início' });
      return;
    }
    let active = true;
    setState({ kind: 'loading' });
    getWaveform({ projectId, mediaId, startSeconds, endSeconds, buckets })
      .then((data) => {
        if (!active) return;
        setState(data.status === 'ok' ? { kind: 'ok', data } : { kind: 'unavailable', reason: waveformUnavailableText(data) });
      })
      .catch((err: unknown) => {
        if (active) setState({ kind: 'error', message: previewErrorMessage(err) });
      });
    return () => {
      active = false;
    };
    // A agulha (currentSeconds) e o destaque não disparam nova leitura: só o intervalo e a mídia.
  }, [projectId, mediaId, startSeconds, endSeconds, buckets, validSpan]);

  const frame = (ratio: number): number => Math.min(100, Math.max(0, ratio * 100));
  const container = `relative h-14 rounded-lg bg-slate-950 border border-slate-800 overflow-hidden ${className}`;

  if (state.kind === 'loading' || state.kind === 'idle') {
    return (
      <div className={`${container} flex items-center justify-center gap-2 text-[11px] text-slate-500`} role="status">
        <Loader2 className="w-3.5 h-3.5 animate-spin text-cyan-400" aria-hidden="true" />
        Carregando forma de onda…
      </div>
    );
  }

  if (state.kind === 'error') {
    return (
      <div role="alert" className={`${container} flex items-center gap-2 px-3 text-[11px] text-rose-300`}>
        <AlertCircle className="w-3.5 h-3.5 shrink-0" aria-hidden="true" />
        <span className="truncate">{state.message}</span>
      </div>
    );
  }

  if (state.kind === 'unavailable') {
    return (
      <div
        role="status"
        data-testid="waveform-unavailable"
        className={`${container} flex items-center justify-center px-3 text-center text-[11px] text-slate-500`}
      >
        {state.reason}
      </div>
    );
  }

  const peaks = state.data.peaks;
  if (peaks.length === 0) {
    return (
      <div role="status" className={`${container} flex items-center justify-center text-[11px] text-slate-500`}>
        O host não retornou picos para este intervalo.
      </div>
    );
  }

  const highlightStart = highlight && validSpan ? frame((highlight.start - startSeconds) / span) : null;
  const highlightEnd = highlight && validSpan ? frame((highlight.end - startSeconds) / span) : null;
  const playhead =
    currentSeconds !== null && validSpan && currentSeconds >= startSeconds && currentSeconds <= endSeconds
      ? frame((currentSeconds - startSeconds) / span)
      : null;

  return (
    <div className={container} data-testid="waveform-strip">
      <svg
        viewBox={`0 0 ${peaks.length} 100`}
        preserveAspectRatio="none"
        role="img"
        aria-label={`Forma de onda de ${startSeconds.toFixed(3)} s a ${endSeconds.toFixed(3)} s da fonte`}
        className="absolute inset-0 w-full h-full"
      >
        {highlightStart !== null && highlightEnd !== null && highlightEnd > highlightStart && (
          <rect
            x={(highlightStart / 100) * peaks.length}
            y={0}
            width={((highlightEnd - highlightStart) / 100) * peaks.length}
            height={100}
            fill="rgba(251, 191, 36, 0.18)"
          />
        )}
        {peaks.map((peak, index) => {
          const value = clampPeak(peak);
          const h = Math.max(1, value * 90);
          return (
            <rect key={index} x={index + 0.1} y={50 - h / 2} width={0.8} height={h} fill="#22d3ee" fillOpacity={0.85} />
          );
        })}
        {playhead !== null && (
          <rect x={(playhead / 100) * peaks.length - 0.25} y={0} width={0.5} height={100} fill="#f87171" />
        )}
      </svg>
    </div>
  );
};
