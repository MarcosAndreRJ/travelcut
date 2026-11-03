import React, { useCallback, useEffect, useRef, useState } from 'react';
import { AlertCircle, AlertTriangle, Ban, Loader2, Pause, Play, RefreshCw, Wrench } from 'lucide-react';
import type { MediaIntervalPlayerProps } from './mediaPlayerTypes';
import { isPlayableSource, usePlaybackSource } from './usePlaybackSource';
import { useProxyGeneration } from './useProxyGeneration';
import {
  afterModeSkipTarget,
  clampToWindow,
  contextWindow,
  formatSourceSeconds,
  mediaElementErrorText,
  WINDOW_EDGE_EPSILON_S
} from './mediaTime';
import { ipc } from '../../services/ipcBridge';

const DEFAULT_CONTEXT_SECONDS = 2;
const KEYBOARD_STEP_SECONDS = 1;
const NOTIFY_MIN_DELTA_SECONDS = 0.05;

const rateText = (rate: number): string => `${Number.isInteger(rate) ? rate : rate.toFixed(2).replace(/0+$/, '')}×`.replace('.', ',');

/**
 * Player de intervalo de UMA mídia (F07). Reprodução real via <video> com a URL de media.getPlaybackSource.
 * Tempos são SEGUNDOS DA FONTE; não há conversão para timecode de timeline aqui.
 * Prévia aproximada: nunca declarada frame-accurate.
 */
export const MediaIntervalPlayer: React.FC<MediaIntervalPlayerProps> = ({
  projectId,
  mediaId,
  startSeconds,
  endSeconds,
  contextSeconds = DEFAULT_CONTEXT_SECONDS,
  mode = 'before',
  playbackRate = 1,
  onTimeUpdate,
  seekToSeconds = null,
  className = ''
}) => {
  const { state, reload } = usePlaybackSource(projectId, mediaId);
  const readySource = state.kind === 'ready' ? state.source : null;
  const playable = readySource !== null && isPlayableSource(readySource);
  const url = playable && readySource ? readySource.url : null;
  const proxy = useProxyGeneration(projectId, mediaId, reload);

  const videoRef = useRef<HTMLVideoElement>(null);
  const pendingSeekRef = useRef<number | null>(null);
  const lastNotifiedRef = useRef<number>(Number.NaN);
  const onTimeUpdateRef = useRef(onTimeUpdate);
  onTimeUpdateRef.current = onTimeUpdate;

  const [duration, setDuration] = useState<number | null>(null);
  const [current, setCurrent] = useState<number>(0);
  const [playing, setPlaying] = useState<boolean>(false);
  const [mediaError, setMediaError] = useState<{ code: number | null } | null>(null);
  const [playError, setPlayError] = useState<string | null>(null);

  const durationRef = useRef<number | null>(null);
  durationRef.current = duration;

  const rate = Number.isFinite(playbackRate) && playbackRate > 0 ? playbackRate : 1;
  const win = contextWindow(startSeconds, endSeconds, contextSeconds, duration);
  const winSpan = win.end - win.start;

  const notify = useCallback((seconds: number, force = false) => {
    const last = lastNotifiedRef.current;
    if (!force && Number.isFinite(last) && Math.abs(seconds - last) < NOTIFY_MIN_DELTA_SECONDS) return;
    lastNotifiedRef.current = seconds;
    onTimeUpdateRef.current?.(seconds);
  }, []);

  // Nova fonte (mídia ou URL, ex.: após proxy): reinicia estado do elemento.
  useEffect(() => {
    setDuration(null);
    setCurrent(0);
    setPlaying(false);
    setMediaError(null);
    setPlayError(null);
    pendingSeekRef.current = null;
    lastNotifiedRef.current = Number.NaN;
  }, [projectId, mediaId, url]);

  // Velocidade aplicada ao elemento quando muda.
  useEffect(() => {
    const v = videoRef.current;
    if (v) v.playbackRate = rate;
  }, [rate]);

  // Novo intervalo ou contexto: pausa e volta para o início da janela (sem reproduzir sozinho).
  // A duração não é dependência: ao chegar os metadados este efeito não deve pausar nem sobrescrever o seek inicial.
  useEffect(() => {
    const v = videoRef.current;
    if (!v || v.readyState < 1) return;
    v.pause();
    const w = contextWindow(startSeconds, endSeconds, contextSeconds, durationRef.current);
    v.currentTime = w.start;
    setCurrent(w.start);
    notify(w.start, true);
  }, [startSeconds, endSeconds, contextSeconds, notify]);

  // Busca externa (ex.: palavra da transcrição): dispara seek ao mudar o valor.
  useEffect(() => {
    if (seekToSeconds === null || !Number.isFinite(seekToSeconds)) return;
    const v = videoRef.current;
    const target = Math.max(0, seekToSeconds);
    if (!v || v.readyState < 1) {
      pendingSeekRef.current = target;
      return;
    }
    v.currentTime = target;
    setCurrent(target);
    notify(target, true);
  }, [seekToSeconds, notify]);

  // Laço de reprodução: lê o tempo do próprio elemento; no modo "after" salta o trecho removido.
  useEffect(() => {
    if (!playing) return;
    let frameId = 0;
    const tick = () => {
      const v = videoRef.current;
      if (!v || v.paused) return;
      let now = v.currentTime;
      const skip = mode === 'after' ? afterModeSkipTarget(now, startSeconds, endSeconds) : null;
      if (skip !== null) {
        v.currentTime = skip;
        now = skip;
      }
      const w = contextWindow(startSeconds, endSeconds, contextSeconds, duration);
      if (now >= w.end - WINDOW_EDGE_EPSILON_S) {
        v.pause();
        setCurrent(w.end);
        notify(w.end, true);
        return;
      }
      setCurrent(now);
      notify(now, skip !== null);
      frameId = window.requestAnimationFrame(tick);
    };
    frameId = window.requestAnimationFrame(tick);
    return () => window.cancelAnimationFrame(frameId);
  }, [playing, mode, startSeconds, endSeconds, contextSeconds, duration, notify]);

  const handleLoadedMetadata = () => {
    const v = videoRef.current;
    if (!v) return;
    const d = Number.isFinite(v.duration) && v.duration > 0 ? v.duration : null;
    setDuration(d);
    v.playbackRate = rate;
    const w = contextWindow(startSeconds, endSeconds, contextSeconds, d);
    const target = pendingSeekRef.current ?? w.start;
    pendingSeekRef.current = null;
    const clamped = clampToWindow(target, w);
    v.currentTime = clamped;
    setCurrent(clamped);
    notify(clamped, true);
  };

  const handleVideoError = () => {
    const v = videoRef.current;
    setMediaError({ code: v?.error?.code ?? null });
    setPlaying(false);
  };

  const seekWindow = (target: number) => {
    const v = videoRef.current;
    if (!v || v.readyState < 1) return;
    const clamped = clampToWindow(target, win);
    v.currentTime = clamped;
    setCurrent(clamped);
    notify(clamped, true);
  };

  const togglePlay = () => {
    const v = videoRef.current;
    if (!v || !url || mediaError) return;
    if (!v.paused) {
      v.pause();
      return;
    }
    if (v.currentTime < win.start || v.currentTime >= win.end - WINDOW_EDGE_EPSILON_S) {
      seekWindow(win.start);
    }
    setPlayError(null);
    v.play().catch((err: unknown) => {
      if (err instanceof DOMException && err.name === 'AbortError') return;
      setPlayError(err instanceof Error ? err.message : 'Não foi possível iniciar a reprodução.');
    });
  };

  const handleScrub = (event: React.ChangeEvent<HTMLInputElement>) => {
    const v = videoRef.current;
    if (v && !v.paused) v.pause();
    seekWindow(Number(event.target.value));
  };

  const handleKeyDown = (event: React.KeyboardEvent<HTMLElement>) => {
    if (event.target !== event.currentTarget) return;
    switch (event.key) {
      case ' ':
      case 'k':
      case 'K':
        event.preventDefault();
        togglePlay();
        break;
      case 'ArrowLeft':
        event.preventDefault();
        seekWindow(current - KEYBOARD_STEP_SECONDS);
        break;
      case 'ArrowRight':
        event.preventDefault();
        seekWindow(current + KEYBOARD_STEP_SECONDS);
        break;
      case 'Home':
        event.preventDefault();
        seekWindow(win.start);
        break;
      case 'End':
        event.preventDefault();
        seekWindow(win.end);
        break;
      default:
        break;
    }
  };

  const proxyAvailable = readySource !== null && readySource.proxyStatus !== 'ffmpeg_unavailable';
  const proxyGenerating = readySource?.proxyStatus === 'generating';
  const showProxyButton = proxyAvailable;

  const proxyButton = showProxyButton ? (
    <button
      type="button"
      onClick={proxy.start}
      disabled={proxy.busy || proxyGenerating}
      className="px-3 py-1.5 rounded-lg bg-cyan-600 hover:bg-cyan-500 disabled:opacity-50 disabled:cursor-not-allowed text-[11px] font-bold text-white flex items-center gap-1.5 focus:outline-none focus-visible:ring-2 focus-visible:ring-cyan-300"
    >
      {proxy.busy ? <Loader2 className="w-3.5 h-3.5 animate-spin" aria-hidden="true" /> : <Wrench className="w-3.5 h-3.5" aria-hidden="true" />}
      {proxy.busy ? 'Gerando proxy…' : 'Gerar proxy'}
    </button>
  ) : null;

  const proxyFeedback = proxy.message && (
    <p role="status" className="text-[11px] text-slate-300 mt-2">
      {proxy.message}
    </p>
  );

  const showOverlay = state.kind !== 'ready' || !playable || mediaError !== null;

  const overlay = (() => {
    if (state.kind === 'loading') {
      return (
        <div role="status" className="absolute inset-0 flex flex-col items-center justify-center gap-2 text-center p-4 text-slate-300">
          <Loader2 className="w-6 h-6 text-cyan-400 animate-spin" aria-hidden="true" />
          <p className="text-xs">Carregando fonte de reprodução…</p>
        </div>
      );
    }
    if (state.kind === 'error') {
      return (
        <div role="alert" className="absolute inset-0 flex flex-col items-center justify-center gap-2 text-center p-4">
          <AlertCircle className="w-6 h-6 text-rose-400" aria-hidden="true" />
          <p className="text-xs text-rose-200 max-w-md">Não foi possível obter a fonte de reprodução: {state.message}</p>
          <button
            type="button"
            onClick={reload}
            className="px-3 py-1.5 rounded-lg bg-slate-800 hover:bg-slate-700 text-[11px] font-semibold text-slate-200 flex items-center gap-1.5 focus:outline-none focus-visible:ring-2 focus-visible:ring-cyan-400"
          >
            <RefreshCw className="w-3.5 h-3.5" aria-hidden="true" /> Tentar novamente
          </button>
        </div>
      );
    }
    if (!playable) {
      return (
        <div role="status" data-testid="player-unavailable" className="absolute inset-0 flex flex-col items-center justify-center gap-2 text-center p-4">
          <Ban className="w-6 h-6 text-amber-400" aria-hidden="true" />
          <p className="text-xs text-slate-200 max-w-md">
            {readySource?.reason ?? 'Esta mídia não tem fonte reproduzível no momento.'}
          </p>
          {proxyButton}
          {proxyFeedback}
        </div>
      );
    }
    if (mediaError) {
      return (
        <div role="alert" data-testid="player-media-error" className="absolute inset-0 flex flex-col items-center justify-center gap-2 text-center p-4">
          <AlertCircle className="w-6 h-6 text-rose-400" aria-hidden="true" />
          <p className="text-xs text-rose-200 max-w-md">Não foi possível reproduzir: {mediaElementErrorText(mediaError.code)}.</p>
          {proxyButton}
          {proxyFeedback}
        </div>
      );
    }
    return null;
  })();

  const trackLeft = winSpan > 0 ? ((startSeconds - win.start) / winSpan) * 100 : 0;
  const trackWidth = winSpan > 0 ? ((endSeconds - startSeconds) / winSpan) * 100 : 0;
  const markerLeft = Math.min(100, Math.max(0, trackLeft));
  const markerWidth = Math.min(100 - markerLeft, Math.max(0, trackWidth));
  const hasMarker = winSpan > 0 && endSeconds > startSeconds && markerWidth > 0;

  return (
    <section
      aria-label="Player de intervalo da mídia"
      data-media-player="interval"
      tabIndex={0}
      onKeyDown={handleKeyDown}
      className={`rounded-xl bg-[#060b14] border border-slate-800 p-3 flex flex-col gap-2 focus:outline-none focus-visible:ring-2 focus-visible:ring-cyan-400 ${className}`}
    >
      <div className="flex flex-wrap items-center justify-between gap-2">
        <span className="px-2 py-0.5 rounded bg-amber-950/40 border border-amber-500/30 text-[10px] font-semibold text-amber-200">
          Prévia aproximada — não frame-accurate
        </span>
        <div className="flex items-center gap-1.5">
          {ipc.isMock && (
            <span className="px-2 py-0.5 rounded-full text-[10px] font-bold uppercase tracking-wider bg-amber-500/10 text-amber-300 border border-amber-500/30">
              DEMONSTRAÇÃO
            </span>
          )}
          <span className="text-[10px] text-slate-400 font-mono">
            {mode === 'after' ? 'Pós-corte: trecho removido é pulado' : 'Antes do corte: trecho inteiro com contexto'}
          </span>
        </div>
      </div>

      {playable && readySource?.likelyPlayable === false && readySource.status === 'original' && (
        <div role="status" className="flex flex-wrap items-center gap-2 px-3 py-2 rounded-lg bg-amber-950/50 border border-amber-500/30 text-[11px] text-amber-200">
          <AlertTriangle className="w-3.5 h-3.5 shrink-0" aria-hidden="true" />
          <span className="flex-1">Codec provavelmente não suportado pelo WebView2 — gere proxy.</span>
          {proxyButton}
        </div>
      )}
      {playable && readySource?.likelyPlayable === null && (
        <p className="text-[10px] text-slate-500">Suporte ao codec não confirmado neste ambiente.</p>
      )}

      <div className="relative aspect-video rounded-lg bg-black border border-slate-800/80 overflow-hidden">
        <video
          ref={videoRef}
          src={url ?? undefined}
          preload="metadata"
          playsInline
          aria-label="Vídeo da fonte (prévia)"
          onLoadedMetadata={handleLoadedMetadata}
          onPlay={() => setPlaying(true)}
          onPause={() => setPlaying(false)}
          onEnded={() => setPlaying(false)}
          onError={handleVideoError}
          className={`absolute inset-0 w-full h-full object-contain ${showOverlay ? 'opacity-0' : 'opacity-100'}`}
        />
        {showOverlay && overlay}
      </div>

      <div className="relative h-7 rounded bg-slate-950 border border-slate-800 overflow-hidden" aria-hidden="true">
        {hasMarker && (
          <div
            data-testid={mode === 'after' ? 'removed-segment-marker' : 'highlight-segment-marker'}
            className={`absolute top-0 bottom-0 ${
              mode === 'after'
                ? 'bg-[repeating-linear-gradient(45deg,rgba(244,63,94,0.35)_0,rgba(244,63,94,0.35)_4px,transparent_4px,transparent_8px)]'
                : 'bg-amber-400/20'
            }`}
            style={{ left: `${markerLeft}%`, width: `${markerWidth}%` }}
          />
        )}
      </div>
      {hasMarker && (
        <p className="text-[10px] text-slate-400 -mt-1">
          {mode === 'after' ? 'Trecho removido' : 'Trecho destacado'}: {formatSourceSeconds(startSeconds)} a{' '}
          {formatSourceSeconds(endSeconds)} da fonte
        </p>
      )}

      <div className="flex items-center gap-2">
        <button
          type="button"
          onClick={togglePlay}
          disabled={!playable || mediaError !== null}
          aria-label={playing ? 'Pausar prévia' : 'Reproduzir prévia'}
          className="p-2 rounded-xl bg-cyan-600 hover:bg-cyan-500 disabled:opacity-40 disabled:cursor-not-allowed text-white flex items-center justify-center focus:outline-none focus-visible:ring-2 focus-visible:ring-cyan-300"
        >
          {playing ? <Pause className="w-4 h-4" aria-hidden="true" /> : <Play className="w-4 h-4 fill-white" aria-hidden="true" />}
        </button>
        <input
          type="range"
          aria-label="Posição na fonte dentro da janela"
          min={win.start}
          max={win.end}
          step={0.01}
          value={clampToWindow(current, win)}
          onChange={handleScrub}
          disabled={!playable || mediaError !== null || winSpan <= 0}
          className="flex-1 h-1.5 bg-slate-800 rounded-lg appearance-none cursor-pointer accent-cyan-400 disabled:opacity-40 focus:outline-none"
        />
      </div>

      <div className="flex flex-wrap items-center justify-between gap-2 text-[10px] font-mono text-slate-400">
        <span data-testid="player-source-time">Fonte: {formatSourceSeconds(current)}</span>
        <span>
          Janela: {formatSourceSeconds(win.start)} – {formatSourceSeconds(win.end)}
        </span>
        <span>Velocidade {rateText(rate)}</span>
      </div>
      {playError && (
        <p role="alert" className="text-[11px] text-rose-300">
          {playError}
        </p>
      )}
    </section>
  );
};
