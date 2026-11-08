import React, { useEffect, useMemo, useRef, useState } from 'react';
import {
  AlertCircle,
  Ban,
  Film,
  Loader2,
  Pause,
  Play,
  RefreshCw,
  RotateCcw,
  Scissors,
  SkipBack,
  SkipForward,
  Wrench
} from 'lucide-react';
import type { EditPlanDto, PlanClipDto, PlaybackSourceDto } from '../../types/ipc';
import { formatDuration, formatSpeed, formatTimecode, framesToSeconds, isValidTimeBase, nominalFps } from './planTime';
import { basename } from './planFormat';
import { clipEndFrame, timelineEndFrame } from './planModel';
import {
  activeClipAt,
  clipSpeedRatio,
  frameForSourceSeconds,
  nextClipStartingAfter,
  programClips,
  secondsPerFrame,
  SOURCE_END_EPSILON_S,
  sourceInSeconds,
  sourceOutSeconds,
  sourceSecondsForFrame,
  transitionAfter
} from './programPlayback';
import { useReviewPlaybackRate, REVIEW_PLAYBACK_RATES } from '../../services/reviewPlaybackSpeed';
import { ipc } from '../../services/ipcBridge';
import { getPlaybackSource, previewErrorMessage } from '../../services/previewApi';
import { isPlayableSource } from '../media/usePlaybackSource';
import { useProxyGeneration } from '../media/useProxyGeneration';
import { WaveformStrip } from '../media/WaveformStrip';
import { formatSourceSeconds, mediaElementErrorText } from '../media/mediaTime';

interface PlanPreviewPlayerProps {
  plan: EditPlanDto | null;
  selectedClip: PlanClipDto | null;
  currentFrame: number;
  onSeekFrame: (frame: number | ((prev: number) => number)) => void;
}

/**
 * Prévia de PROGRAMA (Tela 03): percorre os clipes de V1 do plano. O frame atual é a agulha; a
 * fonte de cada clipe vem de media.getPlaybackSource (cache por mídia). Durante a reprodução o frame
 * é lido do currentTime do <video>. Lacunas sem clipe V1 avançam por relógio, rotuladas.
 * Prévia aproximada: nunca frame-accurate.
 */

type SourceEntry = { kind: 'loading' } | { kind: 'ready'; source: PlaybackSourceDto } | { kind: 'error'; message: string };
interface ClipPlaybackError {
  clipId: string;
  message: string;
}
interface LoadedClip {
  clipId: string;
  url: string;
}

const DEFAULT_TIMEBASE = { frameDurationNum: 1001, frameDurationDen: 30000, label: '29.97 NDF' };
const WAVEFORM_BUCKETS = 160;
/** Sem metadados do <video> após este prazo, o clipe vira erro visível (evita espera infinita). */
const MEDIA_LOAD_TIMEOUT_MS = 15000;

const sourceKey = (projectId: string, mediaId: string): string => `${projectId}|${mediaId}`;

/** URL reproduzível da entrada de cache, ou null quando não há fonte utilizável. */
const readyUrlOf = (entry: SourceEntry | undefined): string | null =>
  entry?.kind === 'ready' && isPlayableSource(entry.source) ? entry.source.url : null;

const canPlayFrame = (
  clips: PlanClipDto[],
  sources: Record<string, SourceEntry>,
  projectId: string,
  clipError: ClipPlaybackError | null,
  frame: number
): boolean => {
  const clip = activeClipAt(clips, frame);
  if (!clip) return true; // lacuna: avanço por relógio, sem vídeo
  if (clipError?.clipId === clip.clipId) return false;
  return readyUrlOf(sources[sourceKey(projectId, clip.mediaId)]) !== null;
};

export const PlanPreviewPlayer: React.FC<PlanPreviewPlayerProps> = ({ plan, selectedClip, currentFrame, onSeekFrame }) => {
  const tb = plan?.timeBase ?? DEFAULT_TIMEBASE;
  const projectId = plan?.projectId ?? '';
  const timeBaseValid = isValidTimeBase(tb);
  const spf = secondsPerFrame(tb);
  const fps = nominalFps(tb);
  // Mesma fonte de fim de timeline do PlanTimelineCanvas (não o resumo isolado).
  const totalFrames = plan ? timelineEndFrame(plan) : 0;
  const totalSeconds = framesToSeconds(totalFrames, tb);
  const clips = useMemo(() => (plan ? programClips(plan) : []), [plan]);

  const activeClip = timeBaseValid ? activeClipAt(clips, currentFrame) : null;
  const activeClipId = activeClip?.clipId ?? null;
  const activeMediaId = activeClip?.mediaId ?? null;
  const activeKey = activeMediaId ? sourceKey(projectId, activeMediaId) : null;

  const [isPlaying, setIsPlaying] = useState<boolean>(false);
  const [sources, setSources] = useState<Record<string, SourceEntry>>({});
  const [clipError, setClipError] = useState<ClipPlaybackError | null>(null);
  const [playError, setPlayError] = useState<string | null>(null);

  const videoRef = useRef<HTMLVideoElement>(null);
  const playingRef = useRef<boolean>(false);
  const frameRef = useRef<number>(currentFrame);
  const lastEmitRef = useRef<number>(currentFrame);
  const gapFloatRef = useRef<number | null>(null);
  const loadedRef = useRef<LoadedClip | null>(null);
  const pendingSeekRef = useRef<number | null>(null);
  const loadTimerRef = useRef<number | null>(null);

  const clearLoadTimer = () => {
    if (loadTimerRef.current !== null) {
      window.clearTimeout(loadTimerRef.current);
      loadTimerRef.current = null;
    }
  };

  // Desmontar o player cancela a espera pelo carregamento.
  useEffect(() => clearLoadTimer, []);

  // Valores lidos por callbacks e pelo laço de reprodução. Atualizados a cada render.
  const [reviewRate, setReviewRate] = useReviewPlaybackRate();
  const latest = useRef({ clips, spf, sources, projectId, clipError, onSeekFrame, reviewRate });
  latest.current = { clips, spf, sources, projectId, clipError, onSeekFrame, reviewRate };

  const activeEntry = activeKey ? sources[activeKey] : undefined;
  const activeEntryTag =
    activeEntry === undefined
      ? 'none'
      : activeEntry.kind === 'ready'
        ? `ready|${activeEntry.source.url ?? ''}|${activeEntry.source.status}`
        : activeEntry.kind;

  const forgetSource = (mediaId: string) => {
    const key = sourceKey(latest.current.projectId, mediaId);
    setSources((prev) => {
      if (!(key in prev)) return prev;
      const next = { ...prev };
      delete next[key];
      return next;
    });
  };

  const proxyMediaRef = useRef<string | null>(null);
  proxyMediaRef.current = activeMediaId;
  const proxy = useProxyGeneration(projectId, activeMediaId ?? '', () => {
    const mediaId = proxyMediaRef.current;
    if (mediaId) forgetSource(mediaId);
    setClipError(null);
  });

  const setPlayingState = (value: boolean) => {
    playingRef.current = value;
    setIsPlaying(value);
  };

  const playSafely = (video: HTMLVideoElement) => {
    const result = video.play();
    if (result && typeof result.catch === 'function') {
      result.catch((err: unknown) => {
        if (err instanceof DOMException && err.name === 'AbortError') return;
        setPlayingState(false);
        setPlayError(err instanceof Error ? err.message : 'Não foi possível iniciar a reprodução.');
      });
    }
  };

  /**
   * Aponta o <video> para o clipe sob `frame`: carrega a fonte se mudou, posiciona no tempo da fonte
   * e aplica a velocidade. Sem clipe ou sem fonte pronta, pausa (o cartão explica o motivo).
   */
  const syncVideo = (frame: number, autoplay: boolean) => {
    const video = videoRef.current;
    if (!video) return;
    const state = latest.current;
    const clip = activeClipAt(state.clips, frame);
    if (!clip || !(state.spf > 0)) {
      video.pause();
      return;
    }
    const url = readyUrlOf(state.sources[sourceKey(state.projectId, clip.mediaId)]);
    if (url === null || state.clipError?.clipId === clip.clipId) {
      video.pause();
      return;
    }
    const target = sourceSecondsForFrame(clip, frame, state.spf);
    video.playbackRate = clipSpeedRatio(clip) * state.reviewRate;
    video.preservesPitch = true;
    (video as unknown as { mozPreservesPitch?: boolean }).mozPreservesPitch = true;
    (video as unknown as { webkitPreservesPitch?: boolean }).webkitPreservesPitch = true;
    if (loadedRef.current?.url === url) {
      loadedRef.current = { clipId: clip.clipId, url };
      if (video.readyState >= 1) {
        video.currentTime = target;
        pendingSeekRef.current = null;
      } else {
        pendingSeekRef.current = target;
      }
    } else {
      // Troca de fonte: o seek e o play acontecem em handleLoadedMetadata.
      loadedRef.current = { clipId: clip.clipId, url };
      pendingSeekRef.current = target;
      clearLoadTimer();
      const loadingClipId = clip.clipId;
      loadTimerRef.current = window.setTimeout(() => {
        loadTimerRef.current = null;
        const el = videoRef.current;
        // Só vale para a fonte ainda pedida e sem metadados (readyState < 1).
        if (!el || el.readyState >= 1 || loadedRef.current?.url !== url) return;
        setPlayingState(false);
        el.pause();
        setClipError({
          clipId: loadingClipId,
          message: `não foi possível carregar a mídia (sem resposta em ${MEDIA_LOAD_TIMEOUT_MS / 1000} s)`
        });
      }, MEDIA_LOAD_TIMEOUT_MS);
      video.src = url;
    }
    if (autoplay && playingRef.current && video.readyState >= 1) {
      playSafely(video);
    } else if (!autoplay || !playingRef.current) {
      video.pause();
    }
  };

  const seekTo = (frame: number) => {
    const target = Math.min(Math.max(0, Math.round(frame)), Math.max(0, totalFrames));
    setPlayingState(false);
    videoRef.current?.pause();
    gapFloatRef.current = null;
    frameRef.current = target;
    lastEmitRef.current = target;
    onSeekFrame(target);
    syncVideo(target, false);
  };

  // Seek externo (scrub, clique na régua, passos): pausa e reposiciona. Mudanças emitidas pelo laço são ignoradas.
  useEffect(() => {
    frameRef.current = currentFrame;
    if (currentFrame === lastEmitRef.current) return;
    lastEmitRef.current = currentFrame;
    gapFloatRef.current = null;
    if (playingRef.current) setPlayingState(false);
    syncVideo(currentFrame, false);
    // syncVideo lê apenas refs; a dependência é a agulha.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [currentFrame]);

  // Busca da fonte do clipe sob a agulha (uma vez por mídia; cache em estado).
  useEffect(() => {
    if (!activeKey || !activeMediaId || !projectId) return;
    if (sources[activeKey]) return;
    setSources((prev) => (prev[activeKey] ? prev : { ...prev, [activeKey]: { kind: 'loading' } }));
    getPlaybackSource(projectId, activeMediaId)
      .then((source) => setSources((prev) => ({ ...prev, [activeKey]: { kind: 'ready', source } })))
      .catch((err: unknown) =>
        setSources((prev) => ({ ...prev, [activeKey]: { kind: 'error', message: previewErrorMessage(err) } }))
      );
  }, [activeKey, activeMediaId, projectId, sources]);

  // Troca de clipe, de fonte ou de estado de erro: sincroniza o <video> com a posição atual.
  useEffect(() => {
    syncVideo(frameRef.current, playingRef.current);
    // A dependência é a identidade do clipe/fonte; syncVideo lê apenas refs.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [activeClipId, activeEntryTag, clipError?.clipId]);

  // Laço de reprodução: o frame vem do currentTime do vídeo; lacunas avançam por relógio.
  useEffect(() => {
    if (!isPlaying) return;
    let frameId = 0;
    let last = window.performance.now();

    const emit = (frame: number) => {
      frameRef.current = frame;
      lastEmitRef.current = frame;
      latest.current.onSeekFrame(frame);
    };
    const stop = () => {
      setPlayingState(false);
      videoRef.current?.pause();
    };
    const requestNext = () => {
      frameId = window.requestAnimationFrame(tick);
    };
    const tick = (now: number) => {
      const dt = Math.max(0, (now - last) / 1000);
      last = now;
      const state = latest.current;
      const frame = frameRef.current;
      const video = videoRef.current;
      const clip = activeClipAt(state.clips, frame);

      if (!clip) {
        // Lacuna sem clipe V1: avanço por relógio (sem vídeo) até o próximo clipe.
        const next = nextClipStartingAfter(state.clips, frame);
        if (!next || !(state.spf > 0)) {
          stop();
          return;
        }
        const base = gapFloatRef.current !== null && Math.floor(gapFloatRef.current) === frame ? gapFloatRef.current : frame;
        const advanced = Math.min(next.timelineStartFrame, base + (dt * state.reviewRate) / state.spf);
        gapFloatRef.current = advanced;
        const whole = Math.floor(advanced);
        if (whole !== frame) emit(whole);
        requestNext();
        return;
      }

      gapFloatRef.current = null;
      const entry = state.sources[sourceKey(state.projectId, clip.mediaId)];
      if (!entry || entry.kind === 'loading') {
        requestNext();
        return;
      }
      if (readyUrlOf(entry) === null || state.clipError?.clipId === clip.clipId) {
        // Sem fonte reproduzível: não pula em silêncio; pausa e o cartão explica.
        stop();
        return;
      }
      if (!video || loadedRef.current?.clipId !== clip.clipId || pendingSeekRef.current !== null || video.readyState < 1) {
        requestNext();
        return;
      }
      if (video.ended || video.currentTime >= sourceOutSeconds(clip) - SOURCE_END_EPSILON_S) {
        emit(clipEndFrame(clip));
        if (transitionAfter(state.clips, clip).kind === 'end') {
          stop();
          return;
        }
        requestNext();
        return;
      }
      const nextFrame = frameForSourceSeconds(clip, video.currentTime, state.spf);
      if (nextFrame !== frame) emit(nextFrame);
      requestNext();
    };

    requestNext();
    return () => window.cancelAnimationFrame(frameId);
  }, [isPlaying]);

  const handleLoadedMetadata = () => {
    const video = videoRef.current;
    if (!video) return;
    clearLoadTimer();
    if (pendingSeekRef.current !== null) {
      video.currentTime = pendingSeekRef.current;
      pendingSeekRef.current = null;
    }
    if (playingRef.current) playSafely(video);
  };

  const handleVideoError = () => {
    const video = videoRef.current;
    const clipId = loadedRef.current?.clipId;
    clearLoadTimer();
    if (!clipId) return;
    setClipError({ clipId, message: mediaElementErrorText(video?.error?.code ?? null) });
    setPlayingState(false);
    video?.pause();
  };

  const canPlayNow = timeBaseValid && clips.length > 0 && canPlayFrame(clips, sources, projectId, clipError, currentFrame);

  const handleTogglePlay = () => {
    if (playingRef.current) {
      setPlayingState(false);
      videoRef.current?.pause();
      return;
    }
    if (!timeBaseValid || clips.length === 0) return;
    let start = currentFrame;
    if (currentFrame >= totalFrames) {
      seekTo(0);
      start = 0;
    }
    if (!canPlayFrame(clips, sources, projectId, clipError, start)) return;
    setPlayError(null);
    gapFloatRef.current = null;
    setPlayingState(true);
    syncVideo(start, true);
  };

  const handleStep = (deltaSeconds: number) => {
    seekTo(currentFrame + Math.round(deltaSeconds * fps));
  };

  const handleScrubChange = (event: React.ChangeEvent<HTMLInputElement>) => {
    seekTo(parseInt(event.target.value, 10));
  };

  // ---- Estado visível ----
  const clipBlocked = activeClip !== null && clipError?.clipId === activeClip.clipId;
  type Stage = 'no_timebase' | 'no_clips' | 'gap' | 'loading' | 'error' | 'blocked' | 'media_error' | 'playing';
  let stage: Stage;
  if (!timeBaseValid) stage = 'no_timebase';
  else if (clips.length === 0) stage = 'no_clips';
  else if (!activeClip) stage = 'gap';
  else if (!activeEntry || activeEntry.kind === 'loading') stage = 'loading';
  else if (activeEntry.kind === 'error') stage = 'error';
  else if (!isPlayableSource(activeEntry.source)) stage = 'blocked';
  else if (clipBlocked) stage = 'media_error';
  else stage = 'playing';

  const readySource = activeEntry?.kind === 'ready' ? activeEntry.source : null;
  const proxyAvailable = readySource !== null && readySource.proxyStatus !== 'ffmpeg_unavailable';
  const proxyButton = activeClip && proxyAvailable ? (
    <button
      type="button"
      onClick={proxy.start}
      disabled={proxy.busy || readySource?.proxyStatus === 'generating'}
      className="px-3 py-1.5 rounded-lg bg-cyan-600 hover:bg-cyan-500 disabled:opacity-50 disabled:cursor-not-allowed text-[11px] font-bold text-white flex items-center gap-1.5 focus:outline-none focus-visible:ring-2 focus-visible:ring-cyan-300"
    >
      {proxy.busy ? <Loader2 className="w-3.5 h-3.5 animate-spin" aria-hidden="true" /> : <Wrench className="w-3.5 h-3.5" aria-hidden="true" />}
      {proxy.busy ? 'Gerando proxy…' : 'Gerar proxy'}
    </button>
  ) : null;
  const proxyFeedback = proxy.message && (
    <p role="status" className="text-[11px] text-slate-300">
      {proxy.message}
    </p>
  );

  const sourceNowSeconds = activeClip && timeBaseValid ? sourceSecondsForFrame(activeClip, currentFrame, spf) : null;
  const isLoadedMediaShown = stage === 'playing';

  const renderOverlay = () => {
    switch (stage) {
      case 'no_timebase':
        return (
          <div role="alert" className="flex flex-col items-center gap-2 text-center p-4">
            <AlertCircle className="w-6 h-6 text-rose-400" aria-hidden="true" />
            <p className="text-xs text-rose-200">Base de tempo do plano inválida: a prévia não consegue calcular o tempo da fonte.</p>
          </div>
        );
      case 'no_clips':
        return (
          <div role="status" className="flex flex-col items-center gap-1.5 text-center p-4 text-slate-500">
            <Film className="w-6 h-6 text-slate-600" aria-hidden="true" />
            <p className="text-xs">Nenhum clipe de V1 neste plano.</p>
          </div>
        );
      case 'gap':
        return (
          <div role="status" className="flex flex-col items-center gap-1.5 text-center p-4 text-slate-500">
            <Film className="w-6 h-6 text-slate-600" aria-hidden="true" />
            <p className="text-xs">sem vídeo neste trecho</p>
            {isPlaying && (
              <p className="text-[10px] text-amber-300">Lacuna sem clipe V1 — avanço por relógio (sem vídeo)</p>
            )}
          </div>
        );
      case 'loading':
        return (
          <div role="status" className="flex flex-col items-center gap-2 text-center p-4 text-slate-300">
            <Loader2 className="w-6 h-6 text-cyan-400 animate-spin" aria-hidden="true" />
            <p className="text-xs">Carregando fonte do clipe…</p>
          </div>
        );
      case 'error':
        return (
          <div role="alert" className="flex flex-col items-center gap-2 text-center p-4">
            <AlertCircle className="w-6 h-6 text-rose-400" aria-hidden="true" />
            <p className="text-xs text-rose-200 max-w-md">
              Não foi possível obter a fonte deste clipe: {activeEntry?.kind === 'error' ? activeEntry.message : ''}
            </p>
            <button
              type="button"
              onClick={() => activeMediaId && forgetSource(activeMediaId)}
              className="px-3 py-1.5 rounded-lg bg-slate-800 hover:bg-slate-700 text-[11px] font-semibold text-slate-200 flex items-center gap-1.5 focus:outline-none focus-visible:ring-2 focus-visible:ring-cyan-400"
            >
              <RefreshCw className="w-3.5 h-3.5" aria-hidden="true" /> Tentar novamente
            </button>
          </div>
        );
      case 'blocked':
        return (
          <div role="status" data-testid="program-clip-blocked" className="flex flex-col items-center gap-2 text-center p-4">
            <Ban className="w-6 h-6 text-amber-400" aria-hidden="true" />
            <p className="text-xs font-bold text-amber-200">Clipe sem fonte reproduzível</p>
            <p className="text-[11px] text-slate-300 max-w-md">{readySource?.reason ?? 'Fonte indisponível para reprodução.'}</p>
            {proxyButton}
            {proxyFeedback}
          </div>
        );
      case 'media_error':
        return (
          <div role="alert" data-testid="program-clip-media-error" className="flex flex-col items-center gap-2 text-center p-4">
            <AlertCircle className="w-6 h-6 text-rose-400" aria-hidden="true" />
            <p className="text-xs text-rose-200 max-w-md">
              Não foi possível reproduzir este clipe: {clipError?.message}.
            </p>
            {proxyButton}
            {proxyFeedback}
          </div>
        );
      default:
        return null;
    }
  };

  const overlay = renderOverlay();

  return (
    <div className="rounded-xl bg-[#060b14] border border-slate-800 p-3 flex flex-col gap-2.5">
      <div className="flex items-center justify-between gap-2 px-3 py-1.5 rounded-lg bg-amber-950/40 border border-amber-500/30 text-[11px] text-amber-200">
        <div className="flex items-center gap-1.5">
          <AlertCircle className="w-3.5 h-3.5 text-amber-400 shrink-0" aria-hidden="true" />
          <span className="font-medium">Prévia aproximada — não frame-accurate</span>
        </div>
        <div className="flex items-center gap-1.5">
          {ipc.isMock && (
            <span className="text-[10px] uppercase tracking-wider font-mono px-1.5 py-0.5 rounded bg-amber-500/10 text-amber-300 font-bold border border-amber-500/30">
              DEMONSTRAÇÃO
            </span>
          )}
          <span className="text-[10px] uppercase tracking-wider font-mono px-1.5 py-0.5 rounded bg-amber-500/20 text-amber-300 font-bold">
            PRÉVIA DE PROGRAMA · V1
          </span>
        </div>
      </div>

      <div className="relative h-44 rounded-lg bg-black border border-slate-800/80 overflow-hidden">
        <video
          ref={videoRef}
          preload="metadata"
          playsInline
          aria-label="Prévia de programa (trilha V1)"
          onLoadedMetadata={handleLoadedMetadata}
          onError={handleVideoError}
          className={`absolute inset-0 w-full h-full object-contain ${isLoadedMediaShown ? 'opacity-100' : 'opacity-0'}`}
        />
        {overlay && (
          <div className="absolute inset-0 flex items-center justify-center">
            {overlay}
          </div>
        )}

        <div className="absolute bottom-2 right-2 px-2 py-1 rounded bg-black/80 border border-slate-800 font-mono text-xs font-bold text-cyan-300 tracking-wider">
          {formatTimecode(currentFrame, tb)}
        </div>
      </div>

      {activeClip ? (
        <div className="flex flex-wrap items-center justify-center gap-2 text-[10px] text-slate-400 font-mono">
          <span className="font-bold text-white truncate max-w-md" title={activeClip.relativePath}>
            {basename(activeClip.relativePath)}
          </span>
          <span className="px-1.5 py-0.5 rounded bg-cyan-950/70 text-cyan-300 border border-cyan-800/50">
            Trilha: {activeClip.trackId}
          </span>
          <span>In: {formatTimecode(activeClip.timelineStartFrame, tb)}</span>
          <span>Dur: {formatTimecode(activeClip.durationFrames, tb)}</span>
          {sourceNowSeconds !== null && <span data-testid="program-source-time">Fonte: {formatSourceSeconds(sourceNowSeconds)}</span>}
          {activeClip.speedNum !== activeClip.speedDen && (
            <span className="text-amber-400">Velocidade: {formatSpeed(activeClip.speedNum, activeClip.speedDen)}</span>
          )}
          {activeClip.needsHumanReview && (
            <span className="px-2 py-0.5 rounded text-[10px] font-bold bg-amber-950 text-amber-300 border border-amber-600/50 flex items-center gap-1">
              <Scissors className="w-3 h-3" aria-hidden="true" /> Revisão humana indicada no plano
            </span>
          )}
        </div>
      ) : (
        <p className="text-center text-[11px] text-slate-500">Nenhum clipe de V1 sob a agulha.</p>
      )}

      {selectedClip && selectedClip.clipId !== activeClipId && (
        <p className="text-center text-[10px] text-slate-500" data-testid="program-selected-hint">
          Clipe selecionado fora da agulha: {basename(selectedClip.relativePath)} (início{' '}
          {formatTimecode(selectedClip.timelineStartFrame, tb)}). A prévia não o exibe nesta posição.
        </p>
      )}

      {/* F14.6: Waveform superior redundante removida. As ondas sonoras agora são renderizadas diretamente nos blocos de áudio da timeline (A1, A2, etc). */}

      <div className="space-y-2 pt-1">
        <div className="flex items-center gap-2">
          <span className="text-[10px] font-mono text-slate-400 w-16 text-right">{formatDuration(framesToSeconds(currentFrame, tb))}</span>
          <input
            type="range"
            min={0}
            max={Math.max(1, totalFrames)}
            value={Math.min(currentFrame, Math.max(1, totalFrames))}
            onChange={handleScrubChange}
            aria-label="Posição de reprodução da prévia"
            className="flex-1 h-1.5 bg-slate-800 rounded-lg appearance-none cursor-pointer accent-cyan-400 focus:outline-none"
          />
          <span className="text-[10px] font-mono text-slate-500 w-16">{formatDuration(totalSeconds)}</span>
        </div>

        <div className="flex items-center justify-between gap-2">
          <div className="flex items-center gap-1.5">
            <button
              type="button"
              onClick={() => handleStep(-1)}
              title="Retroceder 1 segundo"
              aria-label="Retroceder 1 segundo"
              className="p-1.5 rounded-lg text-slate-400 hover:text-white hover:bg-slate-800 focus:outline-none focus-visible:ring-2 focus-visible:ring-cyan-400"
            >
              <SkipBack className="w-4 h-4" aria-hidden="true" />
            </button>
            <button
              type="button"
              onClick={handleTogglePlay}
              disabled={!isPlaying && !canPlayNow}
              title={isPlaying ? 'Pausar' : canPlayNow ? 'Reproduzir prévia' : 'Clipe sem fonte reproduzível neste trecho'}
              aria-label={isPlaying ? 'Pausar prévia de programa' : 'Reproduzir prévia de programa'}
              className="p-2 rounded-xl bg-cyan-600 hover:bg-cyan-500 disabled:opacity-40 disabled:cursor-not-allowed text-white font-bold shadow-md shadow-cyan-950 flex items-center justify-center transition-all focus:outline-none focus-visible:ring-2 focus-visible:ring-cyan-300"
            >
              {isPlaying ? <Pause className="w-4 h-4" aria-hidden="true" /> : <Play className="w-4 h-4 fill-white" aria-hidden="true" />}
            </button>
            <button
              type="button"
              onClick={() => handleStep(1)}
              title="Avançar 1 segundo"
              aria-label="Avançar 1 segundo"
              className="p-1.5 rounded-lg text-slate-400 hover:text-white hover:bg-slate-800 focus:outline-none focus-visible:ring-2 focus-visible:ring-cyan-400"
            >
              <SkipForward className="w-4 h-4" aria-hidden="true" />
            </button>
            <button
              type="button"
              onClick={() => seekTo(0)}
              title="Voltar ao início"
              aria-label="Voltar ao início"
              className="p-1.5 rounded-lg text-slate-400 hover:text-white hover:bg-slate-800 focus:outline-none focus-visible:ring-2 focus-visible:ring-cyan-400"
            >
              <RotateCcw className="w-3.5 h-3.5" aria-hidden="true" />
            </button>

            {/* Velocidade de revisão compartilhada */}
            <div className="flex items-center gap-0.5 bg-slate-900 border border-slate-700/80 rounded-lg p-0.5 text-[10px] ml-1" title="Velocidade de reprodução para revisão da timeline (não altera a velocidade editorial dos clipes)">
              {REVIEW_PLAYBACK_RATES.map((r) => (
                <button
                  key={r}
                  type="button"
                  onClick={() => setReviewRate(r)}
                  className={`px-1.5 py-0.5 rounded font-mono font-medium transition-colors ${
                    reviewRate === r
                      ? 'bg-cyan-600 text-white shadow-sm'
                      : 'text-slate-400 hover:text-slate-200 hover:bg-slate-800'
                  }`}
                >
                  {r}×
                </button>
              ))}
            </div>
          </div>

          <div className="flex items-center gap-1 font-mono text-[11px] text-slate-400">
            <span className="text-slate-500">Quadro:</span>
            <span className="text-slate-200 font-bold">{currentFrame}</span>
            <span className="text-slate-500">/ {totalFrames}</span>
          </div>
        </div>
        {playError && (
          <p role="alert" className="text-[11px] text-rose-300">
            {playError}
          </p>
        )}
      </div>
    </div>
  );
};
