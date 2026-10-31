import React, { useState, useEffect, useRef, useCallback } from 'react';
import {
  Play,
  Pause,
  RotateCcw,
  Volume2,
  VolumeX,
  Maximize2,
  ChevronLeft,
  ChevronRight,
  FastForward,
  Rewind,
  Sparkles,
  Scissors
} from 'lucide-react';
import { LibraryClip } from '../types';
import { getMediaAssetUrl } from '../../../services/desktopBridge';
import { secondsToTimecode } from '../../editing/utils/davinciExporter';
import { AudioWaveformVisualizer } from './AudioWaveformVisualizer';

interface VideoPlayerPreviewProps {
  clip: LibraryClip;
  currentTimeSec: number;
  onSeek: (timeSec: number) => void;
  isPlaying: boolean;
  onTogglePlay: () => void;
}

// Converte string de timecode HH:MM:SS:FF para segundos
function timecodeToSeconds(tc: string, fps: number = 30): number | null {
  const parts = tc.trim().split(':');
  if (parts.length !== 4) return null;
  const [h, m, s, f] = parts.map((p) => parseInt(p, 10));
  if (isNaN(h) || isNaN(m) || isNaN(s) || isNaN(f)) return null;
  return h * 3600 + m * 60 + s + f / fps;
}

export const VideoPlayerPreview: React.FC<VideoPlayerPreviewProps> = ({
  clip,
  currentTimeSec,
  onSeek,
  isPlaying,
  onTogglePlay
}) => {
  const videoRef = useRef<HTMLVideoElement | null>(null);
  const containerRef = useRef<HTMLDivElement | null>(null);
  const scrubberRef = useRef<HTMLDivElement | null>(null);

  const [isMuted, setIsMuted] = useState(false);
  const [playbackSpeed, setPlaybackSpeed] = useState<number>(1);
  const [volume, setVolume] = useState<number>(85);
  const [isFullscreen, setIsFullscreen] = useState(false);
  const [videoError, setVideoError] = useState(false);
  const [duration, setDuration] = useState<number>(clip.durationSec || 30);

  // Timecode SMPTE edicao
  const fps = clip.fps || 30;
  const [isEditingTimecode, setIsEditingTimecode] = useState(false);
  const [timecodeInput, setTimecodeInput] = useState('');

  // Tooltip no hover da barra de progresso
  const [hoverTime, setHoverTime] = useState<number | null>(null);
  const [hoverPosPercent, setHoverPosPercent] = useState<number>(0);

  // URL do arquivo de midia
  const mediaUrl = React.useMemo(() => {
    if (clip.filePath) {
      return getMediaAssetUrl(clip.filePath);
    }
    return '';
  }, [clip.filePath]);

  // Sincroniza Play/Pause com o elemento nativo de video
  useEffect(() => {
    const video = videoRef.current;
    if (!video) return;

    if (isPlaying) {
      video.play().catch((err) => {
        console.warn('[VideoPlayerPreview] Falha ao iniciar reproducao nativa:', err);
      });
    } else {
      video.pause();
    }
  }, [isPlaying]);

  // Sincroniza volume e mudo
  useEffect(() => {
    const video = videoRef.current;
    if (!video) return;
    video.volume = isMuted ? 0 : volume / 100;
    video.muted = isMuted;
  }, [volume, isMuted]);

  // Sincroniza velocidade de reproducao
  useEffect(() => {
    const video = videoRef.current;
    if (!video) return;
    video.playbackRate = playbackSpeed;
  }, [playbackSpeed]);

  // Reseta estado de erro ao trocar de clipe
  useEffect(() => {
    setVideoError(false);
    setDuration(clip.durationSec || 30);
  }, [clip.id, clip.filePath, clip.durationSec]);

  // Eventos nativos do elemento <video>
  const handleLoadedMetadata = () => {
    const video = videoRef.current;
    if (video && video.duration && !isNaN(video.duration) && isFinite(video.duration)) {
      setDuration(video.duration);
    }
  };

  const handleTimeUpdate = () => {
    const video = videoRef.current;
    if (video) {
      onSeek(video.currentTime);
    }
  };

  // Seek nativo
  const handlePerformSeek = useCallback((targetSec: number) => {
    const clamped = Math.max(0, Math.min(duration, targetSec));
    const video = videoRef.current;
    if (video) {
      video.currentTime = clamped;
    }
    onSeek(clamped);
  }, [duration, onSeek]);

  // Controles NLE de Frame-by-Frame
  const handleStepFrames = useCallback((frames: number) => {
    const frameDuration = 1 / fps;
    const target = currentTimeSec + frames * frameDuration;
    handlePerformSeek(target);
  }, [currentTimeSec, fps, handlePerformSeek]);

  // Atalhos de teclado NLE (Espaco, Setas, J/K/L)
  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      // Ignora se estiver digitando em um input
      if (['INPUT', 'TEXTAREA'].includes((e.target as HTMLElement)?.tagName)) {
        return;
      }

      if (e.code === 'Space') {
        e.preventDefault();
        onTogglePlay();
      } else if (e.code === 'ArrowLeft') {
        e.preventDefault();
        handleStepFrames(-1);
      } else if (e.code === 'ArrowRight') {
        e.preventDefault();
        handleStepFrames(1);
      } else if (e.key === 'j' || e.key === 'J') {
        e.preventDefault();
        // NLE J: retroceder ou diminuir velocidade
        setPlaybackSpeed((prev) => Math.max(0.5, prev > 1 ? 1 : 0.5));
        handlePerformSeek(Math.max(0, currentTimeSec - 2));
      } else if (e.key === 'k' || e.key === 'K') {
        e.preventDefault();
        // NLE K: pausar
        if (isPlaying) onTogglePlay();
      } else if (e.key === 'l' || e.key === 'L') {
        e.preventDefault();
        // NLE L: reproduzir rapido (+2x)
        if (!isPlaying) onTogglePlay();
        setPlaybackSpeed((prev) => (prev === 1 ? 2 : prev === 2 ? 4 : 1));
      }
    };

    window.addEventListener('keydown', handleKeyDown);
    return () => {
      window.removeEventListener('keydown', handleKeyDown);
    };
  }, [isPlaying, onTogglePlay, handleStepFrames, handlePerformSeek, currentTimeSec]);

  // Scrubber Click
  const handleScrubberClick = (e: React.MouseEvent<HTMLDivElement>) => {
    const rect = e.currentTarget.getBoundingClientRect();
    const pos = Math.max(0, Math.min(1, (e.clientX - rect.left) / rect.width));
    handlePerformSeek(pos * duration);
  };

  // Hover na barra para timecode tooltip
  const handleScrubberMouseMove = (e: React.MouseEvent<HTMLDivElement>) => {
    const rect = e.currentTarget.getBoundingClientRect();
    const pos = Math.max(0, Math.min(1, (e.clientX - rect.left) / rect.width));
    setHoverPosPercent(pos * 100);
    setHoverTime(pos * duration);
  };

  const handleScrubberMouseLeave = () => {
    setHoverTime(null);
  };

  // Formatacao SMPTE
  const currentSMPTE = secondsToTimecode(currentTimeSec, fps);
  const totalSMPTE = secondsToTimecode(duration, fps);
  const progressPercent = duration > 0 ? Math.min(100, Math.max(0, (currentTimeSec / duration) * 100)) : 0;

  // Submissao do timecode editado manualmente
  const handleTimecodeSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    const parsed = timecodeToSeconds(timecodeInput, fps);
    if (parsed !== null) {
      handlePerformSeek(parsed);
    }
    setIsEditingTimecode(false);
  };

  // Alterna tela cheia
  const handleToggleFullscreen = () => {
    if (!containerRef.current) return;
    if (!document.fullscreenElement) {
      containerRef.current.requestFullscreen?.().then(() => setIsFullscreen(true)).catch(() => {});
    } else {
      document.exitFullscreen?.().then(() => setIsFullscreen(false)).catch(() => {});
    }
  };

  return (
    <div
      ref={containerRef}
      className="bg-slate-900/90 border border-slate-800/90 rounded-xl overflow-hidden shadow-sm flex flex-col focus:outline-none"
      tabIndex={0}
    >
      {/* Player Header */}
      <div className="px-3.5 py-2.5 bg-slate-950/60 border-b border-slate-800/80 flex items-center justify-between">
        <div className="flex items-center space-x-2 min-w-0">
          <span className="text-xs font-bold text-white tracking-tight truncate">
            Prévia do clipe selecionado:
          </span>
          <span className="text-xs font-mono font-semibold text-blue-400 truncate">
            {clip.filename}
          </span>
        </div>

        <div className="flex items-center space-x-1.5 shrink-0">
          <span className="text-[10px] px-2 py-0.5 rounded-full bg-slate-800 text-slate-300 font-medium">
            {clip.resolution} • {fps}fps
          </span>
          <span className="text-[10px] px-2 py-0.5 rounded-full bg-blue-500/10 text-blue-300 border border-blue-500/20 font-semibold">
            Score {clip.aiScore}%
          </span>
        </div>
      </div>

      {/* Video Viewport Area */}
      <div className="relative aspect-video w-full bg-slate-950 flex items-center justify-center overflow-hidden group">
        {mediaUrl && !videoError ? (
          <video
            ref={videoRef}
            src={mediaUrl}
            preload="metadata"
            playsInline
            onLoadedMetadata={handleLoadedMetadata}
            onTimeUpdate={handleTimeUpdate}
            onEnded={() => onTogglePlay()}
            onError={() => {
              console.warn('[VideoPlayerPreview] Erro ao carregar video nativo, usando thumbnail.');
              setVideoError(true);
            }}
            className="w-full h-full object-contain select-none bg-black"
          />
        ) : (
          <img
            src={clip.thumbnail}
            alt={clip.title}
            className="w-full h-full object-cover select-none"
          />
        )}

        {/* Ambient Overlay */}
        <div className="absolute inset-0 bg-gradient-to-t from-slate-950/80 via-transparent to-slate-950/30 pointer-events-none" />

        {/* Big Center Play/Pause button on click */}
        <button
          onClick={onTogglePlay}
          className="absolute inset-0 flex items-center justify-center cursor-pointer group-hover:bg-slate-950/20 transition-colors"
        >
          <div className="w-12 h-12 rounded-full bg-blue-600/90 hover:bg-blue-500 text-white flex items-center justify-center shadow-xl transform group-hover:scale-105 transition-all">
            {isPlaying ? (
              <Pause className="w-5 h-5 fill-current" />
            ) : (
              <Play className="w-5 h-5 ml-0.5 fill-current" />
            )}
          </div>
        </button>

        {/* Current Timecode Indicator Overlay Top Left */}
        <div className="absolute top-2.5 left-2.5 bg-slate-950/90 backdrop-blur-md px-2.5 py-1 rounded-md border border-slate-800/80 text-white font-mono text-xs flex items-center space-x-2">
          <span className={`w-2 h-2 rounded-full ${isPlaying ? 'bg-red-500 animate-pulse' : 'bg-slate-500'}`} />
          {isEditingTimecode ? (
            <form onSubmit={handleTimecodeSubmit} className="inline-block">
              <input
                type="text"
                value={timecodeInput}
                onChange={(e) => setTimecodeInput(e.target.value)}
                onBlur={() => setIsEditingTimecode(false)}
                autoFocus
                placeholder="HH:MM:SS:FF"
                className="w-24 bg-slate-800 text-blue-400 font-mono text-xs px-1 py-0.5 rounded border border-blue-500 focus:outline-none"
              />
            </form>
          ) : (
            <span
              onClick={() => {
                setTimecodeInput(currentSMPTE);
                setIsEditingTimecode(true);
              }}
              title="Clique para digitar timecode"
              className="cursor-pointer hover:text-blue-400 font-bold tracking-wider"
            >
              {currentSMPTE}
            </span>
          )}
          <span className="text-slate-500">/</span>
          <span className="text-slate-400">{totalSMPTE}</span>
        </div>

        {/* Location tag bottom left */}
        <div className="absolute bottom-2.5 left-2.5 bg-slate-950/80 backdrop-blur-md px-2 py-1 rounded-md border border-slate-800/80 text-slate-300 text-[11px] truncate max-w-[200px]">
          {clip.location}
        </div>
      </div>

      {/* Forma de Onda de Audio (Waveform) Real */}
      <div className="px-3 pt-2 pb-1 bg-slate-950/90 border-t border-slate-800/60">
        <AudioWaveformVisualizer
          filePath={clip.filePath}
          currentTimeSec={currentTimeSec}
          durationSec={duration}
          onSeek={handlePerformSeek}
          height={32}
        />
      </div>

      {/* Playhead Scrubber Bar com Tooltip de Hover */}
      <div
        ref={scrubberRef}
        onClick={handleScrubberClick}
        onMouseMove={handleScrubberMouseMove}
        onMouseLeave={handleScrubberMouseLeave}
        className="relative w-full h-3 bg-slate-950 cursor-pointer group/scrub"
      >
        <div
          className="h-full bg-blue-500 relative transition-all"
          style={{ width: `${progressPercent}%` }}
        >
          <div className="absolute right-0 top-1/2 -translate-y-1/2 w-3.5 h-3.5 rounded-full bg-white shadow-md border-2 border-blue-600 transform scale-0 group-hover/scrub:scale-100 transition-transform" />
        </div>

        {/* Cut candidate markers */}
        {clip.transcript.filter((t) => t.isCutCandidate).map((cut, idx) => {
          const cutPos = (cut.seconds / duration) * 100;
          return (
            <div
              key={idx}
              title={`Corte sugerido: ${cut.text}`}
              style={{ left: `${cutPos}%` }}
              className="absolute top-0 bottom-0 w-1 bg-red-500/80 z-10"
            />
          );
        })}

        {/* Hover Tooltip com Timecode */}
        {hoverTime !== null && (
          <div
            style={{ left: `${hoverPosPercent}%` }}
            className="absolute bottom-4 -translate-x-1/2 pointer-events-none bg-slate-900 border border-slate-700 text-white font-mono text-[10px] px-1.5 py-0.5 rounded shadow-lg z-20 whitespace-nowrap"
          >
            {secondsToTimecode(hoverTime, fps)}
          </div>
        )}
      </div>

      {/* Transport Controls Bar */}
      <div className="px-3.5 py-2.5 bg-slate-950/80 flex items-center justify-between text-xs text-slate-300">
        <div className="flex items-center space-x-2">
          {/* Play/Pause */}
          <button
            onClick={onTogglePlay}
            className="text-white hover:text-blue-400 transition-colors p-1"
            title={isPlaying ? 'Pausar (Espaço)' : 'Reproduzir (Espaço)'}
          >
            {isPlaying ? <Pause className="w-4 h-4" /> : <Play className="w-4 h-4 fill-current" />}
          </button>

          {/* Jump back -5s */}
          <button
            onClick={() => handlePerformSeek(currentTimeSec - 5)}
            className="text-slate-400 hover:text-white transition-colors p-1"
            title="Voltar 5 segundos"
          >
            <RotateCcw className="w-3.5 h-3.5" />
          </button>

          {/* Frame Anterior (Left Arrow) */}
          <button
            onClick={() => handleStepFrames(-1)}
            className="text-slate-400 hover:text-white transition-colors p-1"
            title="Voltar 1 Frame (Seta Esquerda)"
          >
            <ChevronLeft className="w-3.5 h-3.5" />
          </button>

          {/* Proximo Frame (Right Arrow) */}
          <button
            onClick={() => handleStepFrames(1)}
            className="text-slate-400 hover:text-white transition-colors p-1"
            title="Avançar 1 Frame (Seta Direita)"
          >
            <ChevronRight className="w-3.5 h-3.5" />
          </button>

          {/* Volume Control */}
          <div className="flex items-center space-x-1.5 group/vol ml-2">
            <button
              onClick={() => setIsMuted(!isMuted)}
              className="text-slate-400 hover:text-white p-1"
              title={isMuted ? 'Desmutar' : 'Mutar'}
            >
              {isMuted || volume === 0 ? (
                <VolumeX className="w-3.5 h-3.5 text-red-400" />
              ) : (
                <Volume2 className="w-3.5 h-3.5" />
              )}
            </button>
            <input
              type="range"
              min="0"
              max="100"
              value={isMuted ? 0 : volume}
              onChange={(e) => {
                setVolume(Number(e.target.value));
                if (isMuted) setIsMuted(false);
              }}
              className="w-14 h-1 bg-slate-800 rounded-lg appearance-none cursor-pointer accent-blue-500"
            />
          </div>
        </div>

        <div className="flex items-center space-x-3 font-mono text-[11px]">
          {/* Atalhos NLE Guia */}
          <span className="hidden sm:inline-block text-[10px] text-slate-500 font-sans">
            J-K-L / Espaço / ← →
          </span>

          {/* Speed Selector */}
          <button
            onClick={() => {
              const speeds = [1, 1.25, 1.5, 2];
              const nextIdx = (speeds.indexOf(playbackSpeed) + 1) % speeds.length;
              setPlaybackSpeed(speeds[nextIdx]);
            }}
            className="px-1.5 py-0.5 rounded bg-slate-800/80 hover:bg-slate-700 text-slate-300 text-[10px] font-bold"
            title="Velocidade de reprodução (J/L)"
          >
            {playbackSpeed}x
          </button>

          {/* Fullscreen */}
          <button
            onClick={handleToggleFullscreen}
            className="text-slate-400 hover:text-white p-1"
            title="Tela cheia"
          >
            <Maximize2 className="w-3.5 h-3.5" />
          </button>
        </div>
      </div>
    </div>
  );
};
