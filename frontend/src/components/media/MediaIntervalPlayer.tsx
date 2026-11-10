import React, { useCallback, useEffect, useRef, useState } from 'react';
import { AlertTriangle, Pause, Play } from 'lucide-react';
import type { MediaIntervalPlayerProps } from './mediaPlayerTypes';
import { isPlayableSource, usePlaybackSource } from './usePlaybackSource';
import { useProxyGeneration } from './useProxyGeneration';
import {
  afterModeSkipTarget,
  afterModeSkipTargets,
  clampToWindow,
  contextWindow,
  formatSourceSeconds,
  keptSegmentAt,
  WINDOW_EDGE_EPSILON_S
} from './mediaTime';
import { planZoomActiveAt, zoomCurveLabel } from './zoomPresentation';
import { ipc } from '../../services/ipcBridge';
import { compatibilityHint, proxyIndicator, resolveClipNotice } from './mediaClipModel';
import { PlaybackNoticeOverlay, ProxyButton, type ProxyControl } from './PlaybackNoticeOverlay';
import { usePlayerDiagnostics } from './usePlayerDiagnostics';
import { useReviewPlaybackRate, REVIEW_PLAYBACK_RATES } from '../../services/reviewPlaybackSpeed';

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
  className = '',
  videoAreaClassName = 'aspect-video',
  zoomTransform = null,
  removedIntervals = null,
  keptSegments = null
}) => {
  const { state, reload } = usePlaybackSource(projectId, mediaId);
  const readySource = state.kind === 'ready' ? state.source : null;
  const playable = readySource !== null && isPlayableSource(readySource);
  const url = playable && readySource ? readySource.url : null;

  const videoRef = useRef<HTMLVideoElement>(null);
  const pendingSeekRef = useRef<number | null>(null);
  // Proxy pronto: recarrega a fonte voltando ao mesmo ponto (o intervalo em revisão não muda).
  const reloadKeepingPosition = useCallback(() => {
    const v = videoRef.current;
    if (v && v.currentTime > 0) pendingSeekRef.current = v.currentTime;
    reload();
  }, [reload]);
  const proxy = useProxyGeneration(projectId, mediaId, reloadKeepingPosition);
  const { diagnostic, copyDiagnostic, openLogsFolder } = usePlayerDiagnostics(videoRef, mediaId, readySource);
  const [reviewRate, setReviewRate] = useReviewPlaybackRate();
  const lastNotifiedRef = useRef<number>(Number.NaN);
  const onTimeUpdateRef = useRef(onTimeUpdate);
  onTimeUpdateRef.current = onTimeUpdate;

  const [duration, setDuration] = useState<number | null>(null);
  const [current, setCurrent] = useState<number>(0);
  const [playing, setPlaying] = useState<boolean>(false);
  const [mediaError, setMediaError] = useState<{ code: number | null } | null>(null);
  // HEVC sem decodificador: o elemento carrega só o áudio, sem erro, com videoWidth 0 (tela preta).
  const [noVideoTrack, setNoVideoTrack] = useState(false);
  const [playError, setPlayError] = useState<string | null>(null);

  const durationRef = useRef<number | null>(null);
  durationRef.current = duration;

  const rate = Number.isFinite(playbackRate) && playbackRate > 0 ? playbackRate : 1;
  const win = contextWindow(startSeconds, endSeconds, contextSeconds, duration);
  const winSpan = win.end - win.start;
  // Modo "after" com plano: taxa e zoom vêm do trecho mantido sob o tempo atual (derivado, sem estado extra).
  const keptAfter = mode === 'after' && keptSegments ? keptSegments : null;
  const activeKept = keptAfter ? keptSegmentAt(current, keptAfter) : null;
  // Tempo decorrido no clipe (linha do tempo) = tempo da fonte desde o início do trecho / taxa.
  const planZoom =
    activeKept && activeKept.zoom !== null
      ? planZoomActiveAt(
          {
            zoom: activeKept.zoom,
            zoomFocalPointX: activeKept.zoomFocalPointX,
            zoomFocalPointY: activeKept.zoomFocalPointY,
            zoomDurationSeconds: activeKept.zoomDurationSeconds,
            zoomCurve: activeKept.zoomCurve
          },
          (current - activeKept.start) / activeKept.rate
        )
      : null;
  // Zoom do plano é aproximado (CSS sobre o <video>); o zoom manual de enquadramento tem precedência.
  const manualZoom = zoomTransform && zoomTransform.scale > 1.0 ? zoomTransform : null;
  const effectiveZoom = manualZoom ?? (planZoom ? { scale: planZoom.scale, offsetX: 0, offsetY: 0 } : null);
  const effectiveOrigin = manualZoom ? 'center center' : planZoom ? `${planZoom.originX}% ${planZoom.originY}%` : 'center center';

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
    setNoVideoTrack(false);
    setPlayError(null);
    lastNotifiedRef.current = Number.NaN;
  }, [projectId, mediaId, url]);

  // Troca de mídia descarta busca pendente; troca só de URL (proxy) a mantém.
  useEffect(() => {
    pendingSeekRef.current = null;
  }, [projectId, mediaId]);

  // Desmontagem: libera o arquivo no host de mídia.
  useEffect(() => {
    const v = videoRef.current;
    return () => {
      if (!v) return;
      v.pause();
      v.removeAttribute('src');
      v.load();
    };
  }, []);

  // Velocidade aplicada ao elemento quando muda (combina taxa do clipe com velocidade de revisão e preserva tom).
  useEffect(() => {
    const v = videoRef.current;
    if (v) {
      v.playbackRate = rate * reviewRate;
      v.preservesPitch = true;
      (v as unknown as { mozPreservesPitch?: boolean }).mozPreservesPitch = true;
      (v as unknown as { webkitPreservesPitch?: boolean }).webkitPreservesPitch = true;
    }
  }, [rate, reviewRate]);

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
      const skipWindows = removedIntervals ?? [{ start: startSeconds, end: endSeconds }];
      const skip = mode === 'after' ? afterModeSkipTargets(now, skipWindows) : null;
      if (skip !== null) {
        v.currentTime = skip;
        now = skip;
      }
      // Taxa por trecho mantido (modo "after" com plano); senão, a taxa do clipe. Sempre combinada com a revisão.
      const seg = keptAfter ? keptSegmentAt(now, keptAfter) : null;
      const wantRate = (seg ? seg.rate : rate) * reviewRate;
      if (v.playbackRate !== wantRate) v.playbackRate = wantRate;
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
  }, [playing, mode, startSeconds, endSeconds, contextSeconds, duration, notify, removedIntervals, keptAfter, rate, reviewRate]);

  const handleLoadedMetadata = () => {
    const v = videoRef.current;
    if (!v) return;
    const d = Number.isFinite(v.duration) && v.duration > 0 ? v.duration : null;
    setDuration(d);
    setNoVideoTrack(v.videoWidth === 0);
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
    if (!v?.error) return;
    setMediaError({ code: v.error.code });
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

  // Mesmo diagnóstico do player de clipe (resolveClipNotice + overlay compartilhado).
  const notice = resolveClipNotice({ state, hasMediaError: mediaError !== null, mediaErrorCode: mediaError?.code, noVideoTrack });
  const showOverlay = notice.kind !== 'none';
  const proxyControl: ProxyControl = {
    generating: proxy.busy || readySource?.proxyStatus === 'generating',
    onStart: proxy.start,
    message: proxy.message
  };
  const proxyStatusText = proxyIndicator(readySource, proxy.busy, playing, mediaError !== null);
  const hint = compatibilityHint(readySource, notice);
  const overlay = (
    <PlaybackNoticeOverlay
      notice={notice}
      proxy={proxyControl}
      onReload={reload}
      diagnostic={diagnostic}
      onCopyDiagnostic={copyDiagnostic}
      onOpenLogsFolder={openLogsFolder}
    />
  );

  // Proxy já em geração (job anterior) e nada tocável: reconsulta a fonte até ficar pronto.
  const waitingForProxy = readySource?.proxyStatus === 'generating' && !proxy.busy && showOverlay;
  useEffect(() => {
    if (!waitingForProxy) return;
    const timer = setTimeout(reload, 3000);
    return () => clearTimeout(timer);
  }, [waitingForProxy, reload]);

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
          {mode === 'after' && activeKept && (activeKept.rate !== 1 || activeKept.zoom !== null) && (
            <span data-testid="plan-segment-approx" className="px-2 py-0.5 rounded bg-sky-950/60 border border-sky-700/50 text-[10px] text-sky-200">
              {activeKept.rate !== 1 ? `Velocidade do plano ${rateText(activeKept.rate)} (aproximada)` : ''}
              {activeKept.rate !== 1 && activeKept.zoom !== null ? ' · ' : ''}
              {activeKept.zoom !== null ? `Zoom ${rateText(activeKept.zoom)} ${zoomCurveLabel(activeKept.zoomCurve)}, não é o render` : ''}
            </span>
          )}
        </div>
      </div>

      {hint && (
        <div role="status" className="flex flex-wrap items-center gap-2 px-3 py-2 rounded-lg bg-amber-950/50 border border-amber-500/30 text-[11px] text-amber-200">
          <AlertTriangle className="w-3.5 h-3.5 shrink-0" aria-hidden="true" />
          <span className="flex-1">{hint.text}</span>
          {hint.offerProxy && <ProxyButton proxy={proxyControl} />}
        </div>
      )}
      {proxyStatusText && (
        <p role="status" className="text-[10px] text-cyan-300">{proxyStatusText}</p>
      )}

      <div className={`relative rounded-lg bg-black border border-slate-800/80 overflow-hidden ${videoAreaClassName}`}>
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
          style={{
            transform:
              effectiveZoom && effectiveZoom.scale > 1.0
                ? `scale(${effectiveZoom.scale}) translate(${effectiveZoom.offsetX}%, ${effectiveZoom.offsetY}%)`
                : undefined,
            transformOrigin: effectiveOrigin,
            transition: 'transform 0.15s ease-out'
          }}
          className={`absolute inset-0 w-full h-full object-contain ${showOverlay ? 'opacity-0' : 'opacity-100'}`}
        />
        {/* Guias visuais de Safe Area (F14.1 Checkpoint E) */}
        {zoomTransform?.showSafeArea && (
          <>
            <div className="absolute inset-[5%] border border-dashed border-amber-400/50 pointer-events-none z-10 flex items-start justify-end p-1 text-[8px] font-mono text-amber-400/80">
              Action Safe 90%
            </div>
            <div className="absolute inset-[10%] border border-dashed border-cyan-400/50 pointer-events-none z-10 flex items-start justify-end p-1 text-[8px] font-mono text-cyan-400/80">
              Title Safe 80%
            </div>
          </>
        )}
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

        {/* Velocidade de revisão compartilhada */}
        <div className="flex items-center gap-0.5 bg-slate-900 border border-slate-700/80 rounded-lg p-0.5 text-[10px]" title="Velocidade de reprodução para revisão (não afeta o corte nem a timeline)">
          {REVIEW_PLAYBACK_RATES.map((r) => (
            <button
              key={r}
              type="button"
              onClick={() => setReviewRate(r)}
              disabled={!playable || mediaError !== null}
              className={`px-1.5 py-0.5 rounded font-mono font-medium transition-colors ${
                reviewRate === r
                  ? 'bg-cyan-600 text-white shadow-sm'
                  : 'text-slate-400 hover:text-slate-200 hover:bg-slate-800 disabled:opacity-40'
              }`}
            >
              {r}×
            </button>
          ))}
        </div>

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
