import React, { useRef, useState, useEffect, useCallback, useMemo } from 'react';
import {
  Play,
  Pause,
  RotateCcw,
  Volume2,
  VolumeX,
  Maximize2,
  Sparkles,
  SplitSquareVertical,
  Scissors,
  CheckCircle2,
  Eye,
  SlidersHorizontal,
  Video,
  ChevronLeft,
  ChevronRight
} from 'lucide-react';
import { useEditingWorkspace } from '../context/EditingContext';
import { secondsToTimecode } from '../utils/davinciExporter';
import { getMediaAssetUrl } from '../../../services/desktopBridge';
import { AudioWaveformVisualizer } from '../../library/components/AudioWaveformVisualizer';

// Converte string de timecode HH:MM:SS:FF para segundos
function timecodeToSeconds(tc: string, fps: number = 30): number | null {
  const parts = tc.trim().split(':');
  if (parts.length !== 4) return null;
  const [h, m, s, f] = parts.map((p) => parseInt(p, 10));
  if (isNaN(h) || isNaN(m) || isNaN(s) || isNaN(f)) return null;
  return h * 3600 + m * 60 + s + f / fps;
}

export const IntegratedVideoPlayer: React.FC = () => {
  const {
    selectedAsset,
    currentTimeSec,
    seekToTime,
    isPlaying,
    togglePlay,
    playbackSpeed,
    setPlaybackSpeed,
    viewModeAB,
    setViewModeAB,
    allCutSuggestions,
    acceptCut,
    rejectCut,
    activeCutId
  } = useEditingWorkspace();

  const [isMuted, setIsMuted] = useState(false);
  const [volume, setVolume] = useState<number>(90);
  const [videoError, setVideoError] = useState(false);
  const [isEditingTimecode, setIsEditingTimecode] = useState(false);
  const [timecodeInput, setTimecodeInput] = useState('');
  const [hoverTime, setHoverTime] = useState<number | null>(null);
  const [hoverPosPercent, setHoverPosPercent] = useState<number>(0);

  const videoRef = useRef<HTMLVideoElement | null>(null);
  const progressBarRef = useRef<HTMLDivElement | null>(null);
  const containerRef = useRef<HTMLDivElement | null>(null);

  const fps = selectedAsset.frameRate || 60;
  const duration = selectedAsset.durationSec || 120;
  const progressPercent = duration > 0 ? Math.min(100, Math.max(0, (currentTimeSec / duration) * 100)) : 0;

  // URL do asset de video para reproducao nativa
  const mediaUrl = useMemo(() => {
    if (selectedAsset.sourcePath) {
      return getMediaAssetUrl(selectedAsset.sourcePath);
    }
    return '';
  }, [selectedAsset.sourcePath]);

  // Reseta estado de erro ao trocar de clipe selecionado
  useEffect(() => {
    setVideoError(false);
  }, [selectedAsset.id, selectedAsset.sourcePath]);

  // Sincroniza Play/Pause com elemento nativo <video>
  useEffect(() => {
    const video = videoRef.current;
    if (!video) return;

    if (isPlaying) {
      video.play().catch((err) => {
        console.warn('[IntegratedVideoPlayer] Falha ao reproduzir video nativo:', err);
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

  // Sincroniza currentTime quando alterado externamente (ex: clique na timeline ou transcricao)
  useEffect(() => {
    const video = videoRef.current;
    if (!video) return;
    if (Math.abs(video.currentTime - currentTimeSec) > 0.3) {
      video.currentTime = currentTimeSec;
    }
  }, [currentTimeSec]);

  // Trata timeupdate nativo do elemento de video
  const handleTimeUpdate = () => {
    const video = videoRef.current;
    if (video) {
      seekToTime(video.currentTime);
    }
  };

  // Funcao para avancar/recuar frames (NLE)
  const handleStepFrames = useCallback((frames: number) => {
    const frameSec = 1 / fps;
    const target = Math.max(0, Math.min(duration, currentTimeSec + frames * frameSec));
    const video = videoRef.current;
    if (video) {
      video.currentTime = target;
    }
    seekToTime(target);
  }, [currentTimeSec, duration, fps, seekToTime]);

  // Atalhos de teclado NLE (Espaco, Setas, J/K/L)
  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      if (['INPUT', 'TEXTAREA'].includes((e.target as HTMLElement)?.tagName)) {
        return;
      }

      if (e.code === 'Space') {
        e.preventDefault();
        togglePlay();
      } else if (e.code === 'ArrowLeft') {
        e.preventDefault();
        handleStepFrames(-1);
      } else if (e.code === 'ArrowRight') {
        e.preventDefault();
        handleStepFrames(1);
      } else if (e.key === 'j' || e.key === 'J') {
        e.preventDefault();
        const target = Math.max(0, currentTimeSec - 2);
        seekToTime(target);
        if (videoRef.current) videoRef.current.currentTime = target;
      } else if (e.key === 'k' || e.key === 'K') {
        e.preventDefault();
        if (isPlaying) togglePlay();
      } else if (e.key === 'l' || e.key === 'L') {
        e.preventDefault();
        if (!isPlaying) togglePlay();
        setPlaybackSpeed(playbackSpeed === 1 ? 2 : playbackSpeed === 2 ? 4 : 1);
      }
    };

    window.addEventListener('keydown', handleKeyDown);
    return () => {
      window.removeEventListener('keydown', handleKeyDown);
    };
  }, [isPlaying, togglePlay, handleStepFrames, currentTimeSec, duration, playbackSpeed, setPlaybackSpeed, seekToTime]);

  // Check if current playhead is inside any cut zone
  const activeCutInPlayhead = selectedAsset.cutSuggestions.find(
    (c) => currentTimeSec >= c.startSec && currentTimeSec <= c.endSec
  );

  const handleProgressClick = (e: React.MouseEvent<HTMLDivElement>) => {
    if (!progressBarRef.current) return;
    const rect = progressBarRef.current.getBoundingClientRect();
    const clickX = e.clientX - rect.left;
    const ratio = Math.max(0, Math.min(1, clickX / rect.width));
    const target = ratio * duration;
    if (videoRef.current) {
      videoRef.current.currentTime = target;
    }
    seekToTime(target);
  };

  const handleProgressMouseMove = (e: React.MouseEvent<HTMLDivElement>) => {
    if (!progressBarRef.current) return;
    const rect = progressBarRef.current.getBoundingClientRect();
    const clickX = e.clientX - rect.left;
    const ratio = Math.max(0, Math.min(1, clickX / rect.width));
    setHoverPosPercent(ratio * 100);
    setHoverTime(ratio * duration);
  };

  const handleProgressMouseLeave = () => {
    setHoverTime(null);
  };

  const currentSMPTE = secondsToTimecode(currentTimeSec, fps);
  const totalSMPTE = secondsToTimecode(duration, fps);

  const handleTimecodeSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    const parsed = timecodeToSeconds(timecodeInput, fps);
    if (parsed !== null) {
      const clamped = Math.max(0, Math.min(duration, parsed));
      if (videoRef.current) {
        videoRef.current.currentTime = clamped;
      }
      seekToTime(clamped);
    }
    setIsEditingTimecode(false);
  };

  return (
    <div
      ref={containerRef}
      className="flex-1 flex flex-col bg-studio-bg overflow-hidden border-b border-studio-border relative select-none focus:outline-none"
      tabIndex={0}
    >
      {/* Top Header / Mode Switcher */}
      <div className="h-10 border-b border-studio-border bg-studio-panel px-4 flex items-center justify-between">
        <div className="flex items-center space-x-3">
          <div className="flex items-center space-x-2">
            <Video className="w-4 h-4 text-indigo-400" />
            <span className="text-xs font-semibold text-studio-text truncate max-w-[200px] md:max-w-[320px]">
              {selectedAsset.displayName}
            </span>
          </div>
          <span className="text-[10px] font-mono bg-studio-card border border-studio-border px-1.5 py-0.5 rounded text-studio-subtle">
            {selectedAsset.resolution} • {selectedAsset.frameRate}fps • {selectedAsset.codec}
          </span>
        </div>

        {/* A/B Comparison Buttons */}
        <div className="flex items-center space-x-1 bg-studio-card p-1 rounded-lg border border-studio-border">
          <button
            onClick={() => setViewModeAB('after_cuts')}
            className={`px-2.5 py-1 rounded text-xs font-medium transition-colors flex items-center space-x-1.5 ${
              viewModeAB === 'after_cuts'
                ? 'bg-indigo-600 text-white shadow-glow-indigo'
                : 'text-studio-muted hover:text-white'
            }`}
            title="Exibir vídeo com os cortes aplicados pela IA"
          >
            <Sparkles className="w-3 h-3 text-amber-300" />
            <span>Com Cortes (IA)</span>
          </button>
          <button
            onClick={() => setViewModeAB('before_cuts')}
            className={`px-2.5 py-1 rounded text-xs font-medium transition-colors flex items-center space-x-1.5 ${
              viewModeAB === 'before_cuts'
                ? 'bg-studio-panel text-white border border-studio-border'
                : 'text-studio-muted hover:text-white'
            }`}
            title="Exibir vídeo bruto original sem cortes"
          >
            <Eye className="w-3 h-3 text-studio-subtle" />
            <span>Original (Bruto)</span>
          </button>
          <button
            onClick={() => setViewModeAB('split_ab')}
            className={`px-2.5 py-1 rounded text-xs font-medium transition-colors flex items-center space-x-1.5 ${
              viewModeAB === 'split_ab'
                ? 'bg-indigo-700/80 text-white border border-indigo-400/50'
                : 'text-studio-muted hover:text-white'
            }`}
            title="Comparação lado a lado A/B"
          >
            <SplitSquareVertical className="w-3 h-3" />
            <span>Split A/B</span>
          </button>
        </div>
      </div>

      {/* Main Video Viewport */}
      <div className="flex-1 bg-black relative flex items-center justify-center select-none overflow-hidden group">
        <div className="w-full h-full relative flex items-center justify-center">
          {mediaUrl && !videoError ? (
            <video
              ref={videoRef}
              src={mediaUrl}
              preload="metadata"
              playsInline
              onTimeUpdate={handleTimeUpdate}
              onError={() => {
                console.warn('[IntegratedVideoPlayer] Fallback para thumbnail devido a erro de reproducao nativa.');
                setVideoError(true);
              }}
              className={`w-full h-full object-contain transition-all duration-300 ${
                activeCutInPlayhead && activeCutInPlayhead.decisionState === 'accepted' && viewModeAB !== 'before_cuts'
                  ? 'opacity-30 blur-sm grayscale'
                  : 'opacity-100'
              }`}
            />
          ) : (
            <img
              src={selectedAsset.thumbnail}
              alt={selectedAsset.displayName}
              className={`w-full h-full object-cover transition-all duration-300 ${
                activeCutInPlayhead && activeCutInPlayhead.decisionState === 'accepted' && viewModeAB !== 'before_cuts'
                  ? 'opacity-30 blur-sm grayscale'
                  : 'opacity-90'
              }`}
            />
          )}

          {/* Color Grade / LUT Indicator Overlay */}
          <div className="absolute top-3 left-3 bg-black/70 backdrop-blur-md px-2.5 py-1 rounded-md border border-white/10 text-[11px] font-mono text-zinc-300 flex items-center space-x-2">
            <span className="w-2 h-2 rounded-full bg-emerald-400 animate-pulse" />
            <span>{selectedAsset.colorProfile}</span>
            <span className="text-zinc-500">|</span>
            <span className="text-indigo-300">{selectedAsset.device}</span>
          </div>

          {/* Timecode Overlay Display com Edicao Direta */}
          <div className="absolute top-3 right-3 bg-black/85 backdrop-blur-md px-3 py-1.5 rounded-lg border border-indigo-500/30 text-indigo-300 font-mono text-sm tracking-wider shadow-lg">
            {isEditingTimecode ? (
              <form onSubmit={handleTimecodeSubmit} className="inline-block">
                <input
                  type="text"
                  value={timecodeInput}
                  onChange={(e) => setTimecodeInput(e.target.value)}
                  onBlur={() => setIsEditingTimecode(false)}
                  autoFocus
                  placeholder="HH:MM:SS:FF"
                  className="w-28 bg-zinc-900 text-indigo-300 font-mono text-xs px-1.5 py-0.5 rounded border border-indigo-500 focus:outline-none"
                />
              </form>
            ) : (
              <span
                onClick={() => {
                  setTimecodeInput(currentSMPTE);
                  setIsEditingTimecode(true);
                }}
                className="cursor-pointer hover:text-white"
                title="Clique para editar Timecode SMPTE"
              >
                {currentSMPTE}
              </span>
            )}
          </div>

          {/* Active Cut Zone Banner */}
          {activeCutInPlayhead && (
            <div
              className={`absolute bottom-4 left-1/2 -translate-x-1/2 px-4 py-2 rounded-xl backdrop-blur-md border shadow-2xl flex items-center space-x-3 transition-all animate-fade-in ${
                activeCutInPlayhead.decisionState === 'accepted'
                  ? 'bg-red-950/90 border-red-500/50 text-red-200'
                  : 'bg-amber-950/90 border-amber-500/50 text-amber-200'
              }`}
            >
              <Scissors className="w-4 h-4 text-amber-400 animate-bounce" />
              <div className="text-xs">
                <span className="font-bold uppercase tracking-wider">{activeCutInPlayhead.reasonLabel}:</span>{' '}
                <span className="italic">"{activeCutInPlayhead.previewText}"</span>{' '}
                <span className="font-mono text-[11px] text-zinc-400">({activeCutInPlayhead.durationSec}s)</span>
              </div>
              <div className="flex items-center space-x-1.5 ml-2">
                {activeCutInPlayhead.decisionState === 'accepted' ? (
                  <button
                    onClick={() => rejectCut(activeCutInPlayhead.id)}
                    className="px-2 py-0.5 bg-zinc-800 hover:bg-zinc-700 text-zinc-200 rounded text-[11px] border border-zinc-600"
                  >
                    Restaurar Trecho
                  </button>
                ) : (
                  <>
                    <button
                      onClick={() => acceptCut(activeCutInPlayhead.id)}
                      className="px-2 py-0.5 bg-emerald-600 hover:bg-emerald-500 text-white rounded text-[11px] font-semibold"
                    >
                      Cortar Trecho
                    </button>
                    <button
                      onClick={() => rejectCut(activeCutInPlayhead.id)}
                      className="px-2 py-0.5 bg-zinc-800 hover:bg-zinc-700 text-zinc-300 rounded text-[11px]"
                    >
                      Manter
                    </button>
                  </>
                )}
              </div>
            </div>
          )}

          {/* Play/Pause Center Trigger Icon */}
          <button
            onClick={togglePlay}
            className="absolute inset-0 m-auto w-16 h-16 rounded-full bg-black/60 hover:bg-indigo-600/80 backdrop-blur-md border border-white/20 text-white flex items-center justify-center transition-all opacity-0 group-hover:opacity-100 hover:scale-110"
          >
            {isPlaying ? <Pause className="w-7 h-7" /> : <Play className="w-7 h-7 ml-1" />}
          </button>
        </div>
      </div>

      {/* Scrubber & Player Control Bar */}
      <div className="bg-studio-panel border-t border-studio-border p-3 space-y-2">
        {/* Real Audio Waveform Visualizer */}
        <AudioWaveformVisualizer
          filePath={selectedAsset.sourcePath}
          currentTimeSec={currentTimeSec}
          durationSec={duration}
          onSeek={(time) => {
            if (videoRef.current) videoRef.current.currentTime = time;
            seekToTime(time);
          }}
          height={28}
        />

        {/* Scrubber Bar with Cut Highlights */}
        <div
          ref={progressBarRef}
          onClick={handleProgressClick}
          onMouseMove={handleProgressMouseMove}
          onMouseLeave={handleProgressMouseLeave}
          className="h-4 bg-studio-card rounded-md relative cursor-pointer group flex items-center border border-studio-border overflow-hidden"
        >
          {/* Cut Zones Markers */}
          {selectedAsset.cutSuggestions.map((cut) => {
            const startRatio = (cut.startSec / duration) * 100;
            const widthRatio = (cut.durationSec / duration) * 100;
            return (
              <div
                key={cut.id}
                className={`absolute top-0 bottom-0 pointer-events-none transition-colors ${
                  cut.decisionState === 'accepted'
                    ? 'bg-red-500/50 border-x border-red-500'
                    : 'bg-amber-400/40 border-x border-amber-400'
                }`}
                style={{ left: `${startRatio}%`, width: `${widthRatio}%` }}
                title={`${cut.reasonLabel}: ${cut.previewText}`}
              />
            );
          })}

          {/* Elapsed Progress Fill */}
          <div
            className="h-full bg-indigo-600/70 border-r-2 border-indigo-400"
            style={{ width: `${progressPercent}%` }}
          />

          {/* Playhead Pin */}
          <div
            className="absolute top-0 bottom-0 w-2 -ml-1 bg-white rounded-sm shadow-md pointer-events-none z-10"
            style={{ left: `${progressPercent}%` }}
          />

          {/* Tooltip de Hover com Timecode */}
          {hoverTime !== null && (
            <div
              style={{ left: `${hoverPosPercent}%` }}
              className="absolute bottom-5 -translate-x-1/2 pointer-events-none bg-zinc-900 border border-zinc-700 text-white font-mono text-[10px] px-1.5 py-0.5 rounded shadow-lg z-30 whitespace-nowrap"
            >
              {secondsToTimecode(hoverTime, fps)}
            </div>
          )}
        </div>

        {/* Transport & Control Buttons */}
        <div className="flex items-center justify-between text-xs">
          <div className="flex items-center space-x-2">
            <button
              onClick={() => {
                if (videoRef.current) videoRef.current.currentTime = 0;
                seekToTime(0);
              }}
              className="p-1.5 hover:bg-studio-card rounded-md text-studio-muted hover:text-white"
              title="Voltar ao início"
            >
              <RotateCcw className="w-3.5 h-3.5" />
            </button>
            <button
              onClick={togglePlay}
              className="px-3 py-1.5 bg-indigo-600 hover:bg-indigo-500 text-white rounded-lg font-medium flex items-center space-x-1 shadow-glow-indigo"
              title="Espaço"
            >
              {isPlaying ? <Pause className="w-3.5 h-3.5" /> : <Play className="w-3.5 h-3.5" />}
              <span>{isPlaying ? 'Pausar' : 'Reproduzir'}</span>
            </button>

            {/* Frame Step Buttons */}
            <button
              onClick={() => handleStepFrames(-1)}
              className="p-1.5 hover:bg-studio-card rounded-md text-studio-muted hover:text-white"
              title="Voltar 1 Frame (Seta Esquerda)"
            >
              <ChevronLeft className="w-3.5 h-3.5" />
            </button>
            <button
              onClick={() => handleStepFrames(1)}
              className="p-1.5 hover:bg-studio-card rounded-md text-studio-muted hover:text-white"
              title="Avançar 1 Frame (Seta Direita)"
            >
              <ChevronRight className="w-3.5 h-3.5" />
            </button>

            {/* Time readout */}
            <div className="font-mono text-studio-text font-semibold ml-2">
              <span>{currentSMPTE}</span>
              <span className="text-studio-subtle mx-1.5">/</span>
              <span className="text-studio-muted">{totalSMPTE}</span>
            </div>
          </div>

          <div className="flex items-center space-x-3">
            {/* Speed Selector */}
            <div className="flex items-center space-x-1 bg-studio-card border border-studio-border px-2 py-0.5 rounded-md">
              <span className="text-[10px] text-studio-subtle">Velocidade:</span>
              {[1, 1.25, 1.5, 2].map((speed) => (
                <button
                  key={speed}
                  onClick={() => setPlaybackSpeed(speed)}
                  className={`px-1.5 py-0.5 rounded text-[11px] font-mono ${
                    playbackSpeed === speed
                      ? 'bg-indigo-600 text-white font-bold'
                      : 'text-studio-muted hover:text-white'
                  }`}
                >
                  {speed}x
                </button>
              ))}
            </div>

            {/* Volume */}
            <button
              onClick={() => setIsMuted(!isMuted)}
              className="p-1.5 hover:bg-studio-card rounded-md text-studio-muted hover:text-white"
              title={isMuted ? 'Desmutar' : 'Mutar'}
            >
              {isMuted ? <VolumeX className="w-4 h-4 text-red-400" /> : <Volume2 className="w-4 h-4" />}
            </button>
          </div>
        </div>
      </div>
    </div>
  );
};
