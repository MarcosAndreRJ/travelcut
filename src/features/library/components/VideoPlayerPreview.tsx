import React, { useState, useEffect } from 'react';
import {
  Play,
  Pause,
  RotateCcw,
  Volume2,
  VolumeX,
  Maximize2,
  Sparkles,
  Scissors,
  Check
} from 'lucide-react';
import { LibraryClip } from '../types';

interface VideoPlayerPreviewProps {
  clip: LibraryClip;
  currentTimeSec: number;
  onSeek: (timeSec: number) => void;
  isPlaying: boolean;
  onTogglePlay: () => void;
}

export const VideoPlayerPreview: React.FC<VideoPlayerPreviewProps> = ({
  clip,
  currentTimeSec,
  onSeek,
  isPlaying,
  onTogglePlay
}) => {
  const [isMuted, setIsMuted] = useState(false);
  const [playbackSpeed, setPlaybackSpeed] = useState<number>(1);
  const [volume, setVolume] = useState<number>(85);
  const [isFullscreen, setIsFullscreen] = useState(false);

  // Format seconds to mm:ss
  const formatTime = (secs: number) => {
    const m = Math.floor(secs / 60);
    const s = Math.floor(secs % 60);
    return `${m.toString().padStart(2, '0')}:${s.toString().padStart(2, '0')}`;
  };

  const currentFormatted = formatTime(currentTimeSec);
  const totalFormatted = formatTime(clip.durationSec);
  const progressPercent = Math.min(100, Math.max(0, (currentTimeSec / clip.durationSec) * 100));

  const handleScrubberClick = (e: React.MouseEvent<HTMLDivElement>) => {
    const rect = e.currentTarget.getBoundingClientRect();
    const pos = (e.clientX - rect.left) / rect.width;
    const newSec = Math.max(0, Math.min(clip.durationSec, pos * clip.durationSec));
    onSeek(newSec);
  };

  const handleSpeedChange = () => {
    const speeds = [1, 1.25, 1.5, 2];
    const nextIdx = (speeds.indexOf(playbackSpeed) + 1) % speeds.length;
    setPlaybackSpeed(speeds[nextIdx]);
  };

  return (
    <div className="bg-slate-900/90 border border-slate-800/90 rounded-xl overflow-hidden shadow-sm flex flex-col">
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
            {clip.resolution}
          </span>
          <span className="text-[10px] px-2 py-0.5 rounded-full bg-blue-500/10 text-blue-300 border border-blue-500/20 font-semibold">
            Score {clip.aiScore}%
          </span>
        </div>
      </div>

      {/* Video Viewport Area */}
      <div className="relative aspect-video w-full bg-slate-950 flex items-center justify-center overflow-hidden group">
        <img
          src={clip.thumbnail}
          alt={clip.title}
          className="w-full h-full object-cover select-none"
        />

        {/* Ambient Overlay */}
        <div className="absolute inset-0 bg-gradient-to-t from-slate-950/90 via-transparent to-slate-950/20 pointer-events-none" />

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
        <div className="absolute top-2.5 left-2.5 bg-slate-950/80 backdrop-blur-md px-2 py-1 rounded-md border border-slate-800/80 text-white font-mono text-xs flex items-center space-x-1.5">
          <span className="w-2 h-2 rounded-full bg-red-500 animate-pulse" />
          <span>
            {currentFormatted} / {totalFormatted}
          </span>
        </div>

        {/* Location tag bottom left */}
        <div className="absolute bottom-2.5 left-2.5 bg-slate-950/80 backdrop-blur-md px-2 py-1 rounded-md border border-slate-800/80 text-slate-300 text-[11px] truncate max-w-[200px]">
          {clip.location}
        </div>
      </div>

      {/* Playhead Scrubber Bar */}
      <div
        onClick={handleScrubberClick}
        className="relative w-full h-2 bg-slate-950 cursor-pointer group/scrub"
      >
        <div
          className="h-full bg-blue-500 relative transition-all"
          style={{ width: `${progressPercent}%` }}
        >
          <div className="absolute right-0 top-1/2 -translate-y-1/2 w-3.5 h-3.5 rounded-full bg-white shadow-md border-2 border-blue-600 transform scale-0 group-hover/scrub:scale-100 transition-transform" />
        </div>

        {/* Cut candidate marker if any */}
        {clip.transcript.filter((t) => t.isCutCandidate).map((cut, idx) => {
          const cutPos = (cut.seconds / clip.durationSec) * 100;
          return (
            <div
              key={idx}
              title={`Corte sugerido: ${cut.text}`}
              style={{ left: `${cutPos}%` }}
              className="absolute top-0 bottom-0 w-1 bg-red-500/80 z-10"
            />
          );
        })}
      </div>

      {/* Transport Controls Bar */}
      <div className="px-3.5 py-2.5 bg-slate-950/80 flex items-center justify-between text-xs text-slate-300">
        <div className="flex items-center space-x-3">
          {/* Play/Pause */}
          <button
            onClick={onTogglePlay}
            className="text-white hover:text-blue-400 transition-colors p-1"
            title={isPlaying ? 'Pausar' : 'Reproduzir'}
          >
            {isPlaying ? <Pause className="w-4 h-4" /> : <Play className="w-4 h-4 fill-current" />}
          </button>

          {/* Jump back -5s */}
          <button
            onClick={() => onSeek(Math.max(0, currentTimeSec - 5))}
            className="text-slate-400 hover:text-white transition-colors p-1"
            title="Voltar 5 segundos"
          >
            <RotateCcw className="w-3.5 h-3.5" />
          </button>

          {/* Volume Control */}
          <div className="flex items-center space-x-1.5 group/vol">
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
          {/* Time text */}
          <span className="text-slate-400">
            {currentFormatted} / <span className="text-slate-200">{totalFormatted}</span>
          </span>

          {/* Speed Selector */}
          <button
            onClick={handleSpeedChange}
            className="px-1.5 py-0.5 rounded bg-slate-800/80 hover:bg-slate-700 text-slate-300 text-[10px] font-bold"
            title="Velocidade de reprodução"
          >
            {playbackSpeed}x
          </button>

          {/* Fullscreen */}
          <button
            onClick={() => setIsFullscreen(!isFullscreen)}
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
