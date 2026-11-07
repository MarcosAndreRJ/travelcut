import React, { useCallback, useEffect, useRef, useState } from 'react';
import { AlertCircle, Ban, Loader2, Maximize2, Minimize2, Pause, Play, RefreshCw, Volume2, VolumeX, Wrench } from 'lucide-react';
import { isPlayableSource, usePlaybackSource } from './usePlaybackSource';
import { useProxyGeneration } from './useProxyGeneration';
import {
  clampSeek,
  CLIP_TIME_NOTIFY_SECONDS,
  formatPlayerTime,
  proxyIndicator,
  resolveClipNotice,
  type ClipSeekRequest
} from './mediaClipModel';

interface MediaClipPlayerProps {
  projectId: string;
  mediaId: string;
  /** Imagem exibida antes de reproduzir. */
  posterUrl?: string | null;
  /** Áudio não tem imagem: videoWidth 0 só é problema em vídeo. */
  isVideo?: boolean;
  /** Duração conhecida antes dos metadados do elemento. */
  durationHint?: number | null;
  /** Busca externa (faixa temporal, palavra, silêncio). O player nunca gera este pedido. */
  seekRequest?: ClipSeekRequest | null;
  /** Tempo atual da mídia: a tela acompanha o vídeo, sem devolver busca ao player. */
  onTimeUpdate?: (sourceSeconds: number) => void;
  /** Conteúdo sobreposto no canto da área de vídeo (ex.: selos de codec). */
  badges?: React.ReactNode;
  videoAreaClassName?: string;
}

const KEYBOARD_STEP_SECONDS = 5;

// Volume da sessão: o player é recriado a cada clipe e o ajuste do usuário não deve se perder.
const sessionAudio = { volume: 1, muted: false };

/**
 * Player de clipe completo. Mesma infraestrutura do MediaIntervalPlayer (media.getPlaybackSource, host de
 * mídia somente leitura, proxy via FFmpeg), sem janela de corte. Nunca inicia sozinho.
 */
export const MediaClipPlayer: React.FC<MediaClipPlayerProps> = ({
  projectId,
  mediaId,
  posterUrl = null,
  isVideo = true,
  durationHint = null,
  seekRequest = null,
  onTimeUpdate,
  badges,
  videoAreaClassName = 'h-36 xl:h-52'
}) => {
  const { state, reload } = usePlaybackSource(projectId, mediaId);
  const readySource = state.kind === 'ready' ? state.source : null;
  const url = readySource && isPlayableSource(readySource) ? readySource.url : null;

  const videoRef = useRef<HTMLVideoElement>(null);
  const containerRef = useRef<HTMLElement>(null);
  const pendingSeekRef = useRef<number | null>(null);
  const lastNotifiedRef = useRef<number>(Number.NaN);
  const onTimeUpdateRef = useRef(onTimeUpdate);
  onTimeUpdateRef.current = onTimeUpdate;

  const [duration, setDuration] = useState<number | null>(null);
  const [current, setCurrent] = useState(0);
  const [playing, setPlaying] = useState(false);
  const [mediaError, setMediaError] = useState<{ code: number | null } | null>(null);
  const [noVideoTrack, setNoVideoTrack] = useState(false);
  const [playError, setPlayError] = useState<string | null>(null);
  const [volume, setVolume] = useState(sessionAudio.volume);
  const [muted, setMuted] = useState(sessionAudio.muted);
  const [fullscreen, setFullscreen] = useState(false);

  // Proxy pronto: a fonte muda, mas a posição do usuário é mantida.
  const reloadKeepingPosition = useCallback(() => {
    const v = videoRef.current;
    if (v && v.currentTime > 0) pendingSeekRef.current = v.currentTime;
    reload();
  }, [reload]);
  const proxy = useProxyGeneration(projectId, mediaId, reloadKeepingPosition);

  const notify = useCallback((seconds: number, force = false) => {
    const last = lastNotifiedRef.current;
    if (!force && Number.isFinite(last) && Math.abs(seconds - last) < CLIP_TIME_NOTIFY_SECONDS) return;
    lastNotifiedRef.current = seconds;
    onTimeUpdateRef.current?.(seconds);
  }, []);

  // Nova fonte (ex.: original → proxy): limpa o estado do elemento, sem perder uma busca pendente.
  useEffect(() => {
    setDuration(null);
    setCurrent(0);
    setPlaying(false);
    setMediaError(null);
    setNoVideoTrack(false);
    setPlayError(null);
    lastNotifiedRef.current = Number.NaN;
  }, [url]);

  // Desmontagem (troca de clipe ou de projeto): libera o arquivo no host de mídia.
  useEffect(() => {
    const v = videoRef.current;
    return () => {
      if (!v) return;
      v.pause();
      v.removeAttribute('src');
      v.load();
    };
  }, []);

  // Busca externa: só o token dispara. Atualizações de tempo não voltam por aqui, então não há laço.
  const seekToken = seekRequest?.token ?? null;
  useEffect(() => {
    if (!seekRequest) return;
    const v = videoRef.current;
    if (!v || v.readyState < 1) {
      pendingSeekRef.current = seekRequest.seconds;
      return;
    }
    const target = clampSeek(seekRequest.seconds, Number.isFinite(v.duration) ? v.duration : null);
    v.currentTime = target;
    setCurrent(target);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [seekToken]);

  // Fonte ainda em geração por job anterior: reconsulta enquanto não houver o que tocar.
  const waitingForProxy = readySource?.proxyStatus === 'generating' && !proxy.busy && (url === null || mediaError !== null || noVideoTrack);
  useEffect(() => {
    if (!waitingForProxy) return;
    const timer = setTimeout(reload, 3000);
    return () => clearTimeout(timer);
  }, [waitingForProxy, reload]);

  useEffect(() => {
    const onChange = () => setFullscreen(document.fullscreenElement === containerRef.current);
    document.addEventListener('fullscreenchange', onChange);
    return () => document.removeEventListener('fullscreenchange', onChange);
  }, []);

  const handleLoadedMetadata = () => {
    const v = videoRef.current;
    if (!v) return;
    v.volume = sessionAudio.volume;
    v.muted = sessionAudio.muted;
    setDuration(Number.isFinite(v.duration) && v.duration > 0 ? v.duration : null);
    setNoVideoTrack(isVideo && v.videoWidth === 0);
    if (pendingSeekRef.current !== null) {
      const target = clampSeek(pendingSeekRef.current, Number.isFinite(v.duration) ? v.duration : null);
      pendingSeekRef.current = null;
      v.currentTime = target;
      setCurrent(target);
    }
  };

  const handleTimeUpdate = () => {
    const v = videoRef.current;
    if (!v) return;
    setCurrent(v.currentTime);
    notify(v.currentTime);
  };

  const notice = resolveClipNotice({
    state,
    hasMediaError: mediaError !== null,
    mediaErrorCode: mediaError?.code,
    noVideoTrack
  });
  const canControl = url !== null && mediaError === null;
  const shownDuration = duration ?? (durationHint && durationHint > 0 ? durationHint : null);

  const togglePlay = () => {
    const v = videoRef.current;
    if (!v || !canControl) return;
    if (!v.paused) {
      v.pause();
      return;
    }
    setPlayError(null);
    v.play().catch((err: unknown) => {
      if (err instanceof DOMException && err.name === 'AbortError') return;
      setPlayError(err instanceof Error ? err.message : 'Não foi possível iniciar a reprodução.');
    });
  };

  const seekLocal = (seconds: number) => {
    const v = videoRef.current;
    if (!v || v.readyState < 1) return;
    const target = clampSeek(seconds, duration);
    v.currentTime = target;
    setCurrent(target);
    notify(target, true);
  };

  const changeVolume = (value: number) => {
    const v = videoRef.current;
    sessionAudio.volume = value;
    sessionAudio.muted = value === 0;
    setVolume(value);
    setMuted(value === 0);
    if (v) {
      v.volume = value;
      v.muted = value === 0;
    }
  };

  const toggleMute = () => {
    const v = videoRef.current;
    const next = !muted;
    sessionAudio.muted = next;
    setMuted(next);
    if (v) v.muted = next;
  };

  const toggleFullscreen = () => {
    if (document.fullscreenElement) {
      void document.exitFullscreen();
    } else {
      containerRef.current?.requestFullscreen().catch(() => setPlayError('Tela cheia indisponível neste ambiente.'));
    }
  };

  const handleKeyDown = (event: React.KeyboardEvent<HTMLElement>) => {
    if (event.target !== event.currentTarget) return;
    if (event.key === ' ' || event.key === 'k' || event.key === 'K') {
      event.preventDefault();
      togglePlay();
    } else if (event.key === 'ArrowLeft') {
      event.preventDefault();
      seekLocal(current - KEYBOARD_STEP_SECONDS);
    } else if (event.key === 'ArrowRight') {
      event.preventDefault();
      seekLocal(current + KEYBOARD_STEP_SECONDS);
    }
  };

  const generatingProxy = proxy.busy || readySource?.proxyStatus === 'generating';
  const proxyStatusText = proxyIndicator(readySource, proxy.busy);

  const proxyButton = (
    <button
      type="button"
      onClick={proxy.start}
      disabled={generatingProxy}
      className="px-3 py-1.5 rounded-lg bg-cyan-600 hover:bg-cyan-500 disabled:opacity-50 disabled:cursor-not-allowed text-[11px] font-bold text-white flex items-center gap-1.5"
    >
      {generatingProxy ? <Loader2 className="w-3.5 h-3.5 animate-spin" aria-hidden="true" /> : <Wrench className="w-3.5 h-3.5" aria-hidden="true" />}
      {generatingProxy ? 'Gerando proxy…' : 'Gerar proxy compatível'}
    </button>
  );

  const overlay = (() => {
    switch (notice.kind) {
      case 'none':
        return null;
      case 'loading':
        return (
          <div role="status" className="absolute inset-0 flex flex-col items-center justify-center gap-2 text-center p-4 text-slate-300">
            <Loader2 className="w-6 h-6 text-cyan-400 animate-spin" aria-hidden="true" />
            <p className="text-xs">Carregando fonte de reprodução…</p>
          </div>
        );
      case 'source_error':
        return (
          <div role="alert" className="absolute inset-0 flex flex-col items-center justify-center gap-2 text-center p-4">
            <AlertCircle className="w-6 h-6 text-rose-400" aria-hidden="true" />
            <p className="text-xs text-rose-200 max-w-md">Não foi possível obter a fonte de reprodução: {notice.message}</p>
            <button type="button" onClick={reload} className="px-3 py-1.5 rounded-lg bg-slate-800 hover:bg-slate-700 text-[11px] font-semibold text-slate-200 flex items-center gap-1.5">
              <RefreshCw className="w-3.5 h-3.5" aria-hidden="true" /> Tentar novamente
            </button>
          </div>
        );
      default: {
        const Icon = notice.kind === 'unavailable' ? Ban : AlertCircle;
        return (
          <div role="alert" className="absolute inset-0 bg-black/85 flex flex-col items-center justify-center gap-2 text-center p-4">
            <Icon className="w-6 h-6 text-amber-400" aria-hidden="true" />
            <p className="text-xs text-slate-200 max-w-md">{notice.message}</p>
            {notice.offerProxy && proxyButton}
            {notice.offerProxy && <p className="text-[10px] text-slate-400">O proxy é uma cópia em 3-Trabalho; o arquivo original não é alterado.</p>}
            {proxy.message && <p role="status" className="text-[11px] text-slate-300">{proxy.message}</p>}
          </div>
        );
      }
    }
  })();

  return (
    <section
      ref={containerRef}
      aria-label="Player do clipe"
      data-media-player="clip"
      tabIndex={0}
      onKeyDown={handleKeyDown}
      className={`flex flex-col gap-1.5 focus:outline-none focus-visible:ring-2 focus-visible:ring-cyan-400 rounded-2xl ${fullscreen ? 'bg-black p-3 h-full' : ''}`}
    >
      <div className={`relative rounded-2xl overflow-hidden bg-black border border-slate-800 ${fullscreen ? 'flex-1 min-h-0' : videoAreaClassName}`}>
        <video
          ref={videoRef}
          src={url ?? undefined}
          poster={posterUrl ?? undefined}
          preload="metadata"
          playsInline
          aria-label="Vídeo do clipe"
          onClick={togglePlay}
          onLoadedMetadata={handleLoadedMetadata}
          onTimeUpdate={handleTimeUpdate}
          onPlay={() => setPlaying(true)}
          onPause={() => setPlaying(false)}
          onEnded={() => setPlaying(false)}
          onError={() => {
            if (!videoRef.current?.error) return;
            setMediaError({ code: videoRef.current.error.code });
            setPlaying(false);
          }}
          className="absolute inset-0 w-full h-full object-contain"
        />
        {overlay}
        {badges && <div className="absolute top-3 right-3 z-10 flex gap-1.5">{badges}</div>}
      </div>

      <div className="flex items-center gap-2">
        <button
          type="button"
          onClick={togglePlay}
          disabled={!canControl}
          aria-label={playing ? 'Pausar' : 'Reproduzir'}
          className="p-1.5 rounded-lg bg-cyan-600 hover:bg-cyan-500 disabled:opacity-40 disabled:cursor-not-allowed text-white"
        >
          {playing ? <Pause className="w-3.5 h-3.5" aria-hidden="true" /> : <Play className="w-3.5 h-3.5 fill-white" aria-hidden="true" />}
        </button>

        <span data-testid="clip-player-time" className="shrink-0 text-[11px] font-mono text-slate-300">
          {formatPlayerTime(current)} / {formatPlayerTime(shownDuration)}
        </span>

        <input
          type="range"
          aria-label="Posição no clipe"
          min={0}
          max={shownDuration ?? 0}
          step={0.01}
          value={Math.min(current, shownDuration ?? current)}
          onChange={(event) => seekLocal(Number(event.target.value))}
          disabled={!canControl || !shownDuration}
          className="flex-1 min-w-0 h-1.5 bg-slate-800 rounded-lg appearance-none cursor-pointer accent-cyan-400 disabled:opacity-40"
        />

        <button
          type="button"
          onClick={toggleMute}
          disabled={!canControl}
          aria-label={muted ? 'Ativar som' : 'Silenciar'}
          className="p-1.5 rounded-lg text-slate-300 hover:text-white hover:bg-slate-800 disabled:opacity-40"
        >
          {muted ? <VolumeX className="w-3.5 h-3.5" aria-hidden="true" /> : <Volume2 className="w-3.5 h-3.5" aria-hidden="true" />}
        </button>
        <input
          type="range"
          aria-label="Volume"
          min={0}
          max={1}
          step={0.05}
          value={muted ? 0 : volume}
          onChange={(event) => changeVolume(Number(event.target.value))}
          disabled={!canControl}
          className="w-16 shrink-0 h-1.5 bg-slate-800 rounded-lg appearance-none cursor-pointer accent-cyan-400 disabled:opacity-40"
        />
        <button
          type="button"
          onClick={toggleFullscreen}
          disabled={!canControl}
          aria-label={fullscreen ? 'Sair da tela cheia' : 'Tela cheia'}
          className="p-1.5 rounded-lg text-slate-300 hover:text-white hover:bg-slate-800 disabled:opacity-40"
        >
          {fullscreen ? <Minimize2 className="w-3.5 h-3.5" aria-hidden="true" /> : <Maximize2 className="w-3.5 h-3.5" aria-hidden="true" />}
        </button>
      </div>

      {(proxyStatusText || playError || (readySource?.likelyPlayable === false && readySource.status === 'original' && notice.kind === 'none')) && (
        <div className="flex flex-wrap items-center gap-2 text-[10px]">
          {proxyStatusText && <span role="status" className="text-cyan-300">{proxyStatusText}</span>}
          {readySource?.likelyPlayable === false && readySource.status === 'original' && notice.kind === 'none' && (
            <>
              <span className="text-amber-300">Codec provavelmente sem suporte no WebView2.</span>
              {proxyButton}
            </>
          )}
          {playError && <span role="alert" className="text-rose-300">{playError}</span>}
        </div>
      )}
    </section>
  );
};
