import React, { useState } from 'react';
import {
  Play,
  Pause,
  SkipBack,
  SkipForward,
  Scissors,
  Magnet,
  Maximize2,
  ZoomIn,
  ZoomOut,
  Volume2,
  Sparkles,
  Layers,
  Clock,
  Eye,
  Lock,
  Music,
  Mic,
  Video,
  FileText
} from 'lucide-react';
import { TimelineClip, MediaClip } from '../types';

interface TimelineNLEProps {
  timelineClips: TimelineClip[];
  allMediaClips: MediaClip[];
  currentTimeSec: number;
  onSeek: (time: number) => void;
  isPlaying: boolean;
  onTogglePlay: () => void;
  onSelectClip?: (clipId: string) => void;
}

export const TimelineNLE: React.FC<TimelineNLEProps> = ({
  timelineClips,
  allMediaClips,
  currentTimeSec,
  onSeek,
  isPlaying,
  onTogglePlay,
  onSelectClip,
}) => {
  const [zoomLevel, setZoomLevel] = useState<number>(1);
  const [snapEnabled, setSnapEnabled] = useState<boolean>(true);
  const [selectedTimelineClipId, setSelectedTimelineClipId] = useState<string | null>(
    timelineClips[0]?.id || null
  );

  // Total duration of current sequence
  const totalDuration = timelineClips.reduce(
    (max, c) => Math.max(max, c.timelineStartSec + c.durationSec),
    61.6
  );

  const formatTimecode = (secs: number) => {
    const h = Math.floor(secs / 3600);
    const m = Math.floor((secs % 3600) / 60);
    const s = Math.floor(secs % 60);
    const f = Math.floor((secs % 1) * 60);
    return `${h.toString().padStart(2, '0')}:${m.toString().padStart(2, '0')}:${s.toString().padStart(2, '0')}:${f.toString().padStart(2, '0')}`;
  };

  const handleTimelineClick = (e: React.MouseEvent<HTMLDivElement>) => {
    const rect = e.currentTarget.getBoundingClientRect();
    const clickX = e.clientX - rect.left;
    const clickPercent = Math.max(0, Math.min(1, clickX / rect.width));
    onSeek(clickPercent * totalDuration);
  };

  const playheadPercent = (currentTimeSec / totalDuration) * 100;

  return (
    <div className="h-64 border-t border-studio-border bg-studio-panel flex flex-col shrink-0 select-none">
      {/* Timeline Controls & Action Bar */}
      <div className="h-10 border-b border-studio-border px-4 flex items-center justify-between bg-studio-card/80">
        <div className="flex items-center space-x-3">
          {/* Timecode Readout */}
          <div className="bg-black/80 border border-studio-border px-3 py-1 rounded-md font-mono text-xs flex items-center space-x-2">
            <span className="text-indigo-400 font-bold">{formatTimecode(currentTimeSec)}</span>
            <span className="text-studio-subtle">/</span>
            <span className="text-studio-muted">{formatTimecode(totalDuration)}</span>
          </div>

          <span className="text-xs font-mono text-studio-subtle bg-studio-panel px-2 py-0.5 rounded border border-studio-border">
            60.00 fps
          </span>

          {/* Transport buttons */}
          <div className="flex items-center space-x-1">
            <button
              onClick={() => onSeek(0)}
              className="p-1 hover:bg-studio-panel rounded text-studio-muted hover:text-white"
              title="Ir para o início"
            >
              <SkipBack className="w-3.5 h-3.5" />
            </button>
            <button
              onClick={onTogglePlay}
              className="p-1.5 bg-indigo-600 hover:bg-indigo-500 rounded-lg text-white shadow-glow-indigo"
              title="Play / Pause"
            >
              {isPlaying ? <Pause className="w-3.5 h-3.5" /> : <Play className="w-3.5 h-3.5 ml-0.5" />}
            </button>
            <button
              onClick={() => onSeek(totalDuration)}
              className="p-1 hover:bg-studio-panel rounded text-studio-muted hover:text-white"
              title="Ir para o fim"
            >
              <SkipForward className="w-3.5 h-3.5" />
            </button>
          </div>
        </div>

        {/* Editing Tools */}
        <div className="flex items-center space-x-3">
          <button
            onClick={() => setSnapEnabled(!snapEnabled)}
            className={`p-1.5 rounded-lg border text-xs flex items-center gap-1 transition-colors ${
              snapEnabled
                ? 'bg-indigo-600/30 border-indigo-500 text-indigo-300'
                : 'bg-studio-panel border-studio-border text-studio-muted hover:text-white'
            }`}
            title="Ajuste Magnético (Snap)"
          >
            <Magnet className="w-3.5 h-3.5" />
            <span className="text-[11px] font-mono">Snap</span>
          </button>

          <button
            className="p-1.5 rounded-lg bg-studio-panel hover:bg-studio-cardHover border border-studio-border text-studio-muted hover:text-white transition-colors"
            title="Dividir Clipe no Playhead (C)"
          >
            <Scissors className="w-3.5 h-3.5" />
          </button>

          {/* Zoom Slider */}
          <div className="flex items-center space-x-2 bg-studio-panel border border-studio-border px-2 py-0.5 rounded-lg">
            <ZoomOut className="w-3 h-3 text-studio-subtle" />
            <input
              type="range"
              min="0.8"
              max="2.5"
              step="0.1"
              value={zoomLevel}
              onChange={(e) => setZoomLevel(parseFloat(e.target.value))}
              className="w-16 accent-indigo-500 h-1 bg-studio-card rounded cursor-pointer"
            />
            <ZoomIn className="w-3 h-3 text-studio-subtle" />
          </div>
        </div>
      </div>

      {/* Tracks Container: Headers on Left, Canvas Lanes on Right */}
      <div className="flex-1 flex overflow-hidden">
        {/* Left Track Headers */}
        <div className="w-36 border-r border-studio-border bg-studio-card/90 flex flex-col justify-around py-1 shrink-0 text-[10px] font-mono">
          <div className="h-9 px-2 flex items-center justify-between border-b border-studio-border/40 text-studio-muted">
            <div className="flex items-center space-x-1.5 font-bold">
              <FileText className="w-3 h-3 text-amber-400" />
              <span>T1 Legendas</span>
            </div>
            <Eye className="w-3 h-3 text-studio-subtle" />
          </div>

          <div className="h-10 px-2 flex items-center justify-between border-b border-studio-border/40 text-studio-text">
            <div className="flex items-center space-x-1.5 font-bold">
              <Video className="w-3 h-3 text-indigo-400" />
              <span>V1 Vídeo A</span>
            </div>
            <Eye className="w-3 h-3 text-studio-subtle" />
          </div>

          <div className="h-8 px-2 flex items-center justify-between border-b border-studio-border/40 text-studio-muted">
            <div className="flex items-center space-x-1.5 font-bold">
              <Mic className="w-3 h-3 text-emerald-400" />
              <span>A1 Narração</span>
            </div>
            <Volume2 className="w-3 h-3 text-studio-subtle" />
          </div>

          <div className="h-8 px-2 flex items-center justify-between text-studio-muted">
            <div className="flex items-center space-x-1.5 font-bold">
              <Music className="w-3 h-3 text-pink-400" />
              <span>A2 Trilha 118</span>
            </div>
            <Volume2 className="w-3 h-3 text-studio-subtle" />
          </div>
        </div>

        {/* Right Multi-track Canvas with Playhead */}
        <div
          onClick={handleTimelineClick}
          className="flex-1 relative overflow-x-auto overflow-y-hidden bg-studio-bg flex flex-col justify-around py-1 cursor-pointer"
        >
          {/* Vertical Draggable Playhead */}
          <div
            style={{ left: `${playheadPercent}%` }}
            className="absolute top-0 bottom-0 w-0.5 bg-rose-500 z-30 pointer-events-none"
          >
            <div className="w-3 h-3 -ml-[5px] bg-rose-500 rotate-45 rounded-sm shadow-md" />
          </div>

          {/* Track T1: Subtitles / Karaoke */}
          <div className="h-9 relative px-2 flex items-center">
            {timelineClips.map((clip) => {
              const left = (clip.timelineStartSec / totalDuration) * 100;
              const width = (clip.durationSec / totalDuration) * 100;
              return (
                <div
                  key={`sub-${clip.id}`}
                  style={{ left: `${left}%`, width: `${width}%` }}
                  className="absolute h-6 bg-amber-500/15 border border-amber-500/40 rounded px-1.5 flex items-center text-[10px] text-amber-200 truncate font-sans"
                >
                  <span className="truncate">"{clip.title}"</span>
                </div>
              );
            })}
          </div>

          {/* Track V1: Video Clips */}
          <div className="h-10 relative px-2 flex items-center">
            {timelineClips.map((clip) => {
              const left = (clip.timelineStartSec / totalDuration) * 100;
              const width = (clip.durationSec / totalDuration) * 100;
              const isSelected = clip.id === selectedTimelineClipId;
              const sourceClip = allMediaClips.find((m) => m.id === clip.mediaClipId);

              return (
                <div
                  key={`v1-${clip.id}`}
                  onClick={(e) => {
                    e.stopPropagation();
                    setSelectedTimelineClipId(clip.id);
                    if (onSelectClip) onSelectClip(clip.mediaClipId);
                  }}
                  style={{ left: `${left}%`, width: `${width}%` }}
                  className={`absolute h-8 rounded-md border flex items-center justify-between px-2 cursor-pointer transition-all ${
                    isSelected
                      ? 'bg-indigo-600/90 border-indigo-300 shadow-glow-indigo text-white z-10'
                      : 'bg-indigo-950/60 border-indigo-500/40 text-indigo-200 hover:bg-indigo-900/70'
                  }`}
                >
                  <div className="flex items-center space-x-1.5 truncate">
                    {sourceClip && (
                      <img
                        src={sourceClip.thumbnail}
                        alt=""
                        className="w-4 h-4 rounded object-cover shrink-0"
                      />
                    )}
                    <span className="text-[11px] font-bold truncate">{clip.title}</span>
                  </div>

                  <span className="text-[9px] font-mono opacity-80 shrink-0">
                    {clip.durationSec.toFixed(1)}s
                  </span>
                </div>
              );
            })}
          </div>

          {/* Track A1: Speech Waveforms */}
          <div className="h-8 relative px-2 flex items-center">
            {timelineClips.map((clip) => {
              const left = (clip.timelineStartSec / totalDuration) * 100;
              const width = (clip.durationSec / totalDuration) * 100;
              return (
                <div
                  key={`a1-${clip.id}`}
                  style={{ left: `${left}%`, width: `${width}%` }}
                  className="absolute h-6 bg-emerald-950/50 border border-emerald-500/30 rounded flex items-center px-2 overflow-hidden"
                >
                  <div className="w-full flex items-center space-x-0.5 opacity-60">
                    {Array.from({ length: 18 }).map((_, i) => (
                      <div
                        key={i}
                        style={{ height: `${Math.sin(i * 0.8) * 10 + 12}px` }}
                        className="w-1 bg-emerald-400 rounded-full"
                      />
                    ))}
                  </div>
                </div>
              );
            })}
          </div>

          {/* Track A2: Music BGM with Beat Markers */}
          <div className="h-8 relative px-2 flex items-center">
            <div className="w-full h-6 bg-pink-950/40 border border-pink-500/30 rounded flex items-center px-2 justify-between">
              <span className="text-[10px] font-mono text-pink-300 flex items-center gap-1.5">
                <Music className="w-3 h-3" />
                Tokyo Sunset Horizon (118 BPM) - Trilha Contínua
              </span>
              {/* Beat sync markers */}
              <div className="flex items-center space-x-4 opacity-50">
                {Array.from({ length: 12 }).map((_, i) => (
                  <div key={i} className="w-1.5 h-1.5 rounded-full bg-pink-400 animate-pulse" />
                ))}
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
};
