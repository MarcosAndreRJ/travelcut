import React, { useState, useRef } from 'react';
import {
  Layers,
  ZoomIn,
  ZoomOut,
  Scissors,
  Sliders,
  Sparkles,
  Music,
  Video,
  Film,
  Camera
} from 'lucide-react';
import { useEditingWorkspace } from '../context/EditingContext';
import { secondsToTimecode } from '../utils/davinciExporter';

export const IntegratedTimeline: React.FC = () => {
  const {
    timelineClips,
    selectedTimelineClipId,
    onSelectTimelineClip,
    currentTimeSec,
    seekToTime,
    totalSequenceDurationSec,
    viewModeAB
  } = useEditingWorkspace();

  const [zoomLevel, setZoomLevel] = useState<number>(1);
  const timelineTracksRef = useRef<HTMLDivElement>(null);

  // Pixel width scale: base is 12px per second * zoomLevel
  const pxPerSec = 14 * zoomLevel;
  const totalTimelineWidth = Math.max(800, totalSequenceDurationSec * pxPerSec);

  const handleTimelineClick = (e: React.MouseEvent<HTMLDivElement>) => {
    if (!timelineTracksRef.current) return;
    const rect = timelineTracksRef.current.getBoundingClientRect();
    const scrollLeft = timelineTracksRef.current.scrollLeft;
    const clickX = e.clientX - rect.left + scrollLeft;
    const newTime = Math.max(0, clickX / pxPerSec);
    seekToTime(newTime);
  };

  // Group clips into tracks
  const v2Clips = timelineClips.filter((c) => c.trackIndex === 1);
  const v1Clips = timelineClips.filter((c) => c.trackIndex === 0);

  // Timecode Ruler markers
  const rulerTicksCount = Math.ceil(totalSequenceDurationSec / 5) + 2;
  const rulerTicks = Array.from({ length: rulerTicksCount }, (_, i) => i * 5);

  return (
    <div className="h-64 border-t border-studio-border bg-studio-panel flex flex-col shrink-0 select-none">
      {/* Timeline Controls Header */}
      <div className="h-9 px-4 border-b border-studio-border bg-studio-card/90 flex items-center justify-between text-xs">
        <div className="flex items-center space-x-3">
          <div className="flex items-center space-x-1.5 font-semibold text-studio-text">
            <Layers className="w-4 h-4 text-indigo-400" />
            <span>Timeline Multitrack NLE</span>
          </div>
          <span className="text-[11px] font-mono text-studio-subtle bg-studio-panel border border-studio-border px-2 py-0.5 rounded">
            Duração Total: {secondsToTimecode(totalSequenceDurationSec, 60)}
          </span>
          {viewModeAB === 'after_cuts' && (
            <span className="text-[10px] bg-emerald-950/60 border border-emerald-500/40 text-emerald-300 px-2 py-0.5 rounded flex items-center space-x-1">
              <Sparkles className="w-3 h-3 text-emerald-400" />
              <span>Cortes IA Aplicados</span>
            </span>
          )}
        </div>

        {/* Zoom Controls */}
        <div className="flex items-center space-x-2">
          <span className="text-[10px] text-studio-subtle font-mono">Zoom:</span>
          <button
            onClick={() => setZoomLevel((z) => Math.max(0.6, z - 0.2))}
            className="p-1 hover:bg-studio-panel rounded text-studio-muted hover:text-white"
            title="Diminuir Zoom"
          >
            <ZoomOut className="w-3.5 h-3.5" />
          </button>
          <span className="text-[11px] font-mono text-studio-text w-8 text-center">
            {Math.round(zoomLevel * 100)}%
          </span>
          <button
            onClick={() => setZoomLevel((z) => Math.min(2.5, z + 0.2))}
            className="p-1 hover:bg-studio-panel rounded text-studio-muted hover:text-white"
            title="Aumentar Zoom"
          >
            <ZoomIn className="w-3.5 h-3.5" />
          </button>
        </div>
      </div>

      {/* Main Track Workspace */}
      <div className="flex-1 flex overflow-hidden relative">
        {/* Track Headers Column */}
        <div className="w-24 border-r border-studio-border bg-studio-card flex flex-col shrink-0 text-[10px] font-mono font-semibold text-studio-muted z-20">
          <div className="h-6 border-b border-studio-border bg-studio-panel px-2 flex items-center">
            RÉGUA
          </div>
          <div className="h-12 border-b border-studio-border px-2 flex items-center justify-between bg-studio-card/80">
            <span className="text-purple-300 flex items-center space-x-1">
              <Video className="w-3 h-3" />
              <span>V2 B-Roll</span>
            </span>
          </div>
          <div className="h-12 border-b border-studio-border px-2 flex items-center justify-between bg-studio-card/80">
            <span className="text-indigo-300 flex items-center space-x-1">
              <Film className="w-3 h-3" />
              <span>V1 Base</span>
            </span>
          </div>
          <div className="h-10 border-b border-studio-border px-2 flex items-center justify-between bg-studio-card/80">
            <span className="text-emerald-300 flex items-center space-x-1">
              <Music className="w-3 h-3" />
              <span>A1 Voz</span>
            </span>
          </div>
          <div className="h-10 px-2 flex items-center justify-between bg-studio-card/80">
            <span className="text-cyan-300 flex items-center space-x-1">
              <Music className="w-3 h-3" />
              <span>A2 Amb</span>
            </span>
          </div>
        </div>

        {/* Scrollable Track Canvas with Playhead */}
        <div
          ref={timelineTracksRef}
          onClick={handleTimelineClick}
          className="flex-1 overflow-x-auto relative bg-studio-bg cursor-pointer custom-scrollbar"
        >
          <div
            className="relative h-full"
            style={{ width: `${totalTimelineWidth}px`, minWidth: '100%' }}
          >
            {/* Timecode Ruler */}
            <div className="h-6 border-b border-studio-border bg-studio-panel relative flex items-center">
              {rulerTicks.map((sec) => (
                <div
                  key={sec}
                  className="absolute top-0 bottom-0 border-l border-studio-border/60 pl-1 text-[9px] font-mono text-studio-subtle flex items-center"
                  style={{ left: `${sec * pxPerSec}px` }}
                >
                  {secondsToTimecode(sec, 60).slice(3)}
                </div>
              ))}
            </div>

            {/* Playhead Needle Vertical Line */}
            <div
              className="absolute top-0 bottom-0 w-0.5 bg-red-500 z-30 pointer-events-none"
              style={{ left: `${currentTimeSec * pxPerSec}px` }}
            >
              {/* Playhead Head Marker */}
              <div className="w-3 h-3 bg-red-500 rounded-sm -ml-1.5 rotate-45 shadow-lg shadow-red-500/50" />
            </div>

            {/* Track V2: Overlay & B-Roll */}
            <div className="h-12 border-b border-studio-border/40 relative bg-zinc-950/40">
              {v2Clips.map((clip) => {
                const isSelected = selectedTimelineClipId === clip.id;
                const leftPx = clip.timelineStartSec * pxPerSec;
                const widthPx = Math.max(30, clip.durationSec * pxPerSec);

                return (
                  <div
                    key={clip.id}
                    onClick={(e) => {
                      e.stopPropagation();
                      onSelectTimelineClip(clip.id);
                    }}
                    className={`absolute top-1 bottom-1 rounded-md px-2 flex flex-col justify-center border transition-all cursor-pointer ${
                      isSelected
                        ? 'bg-purple-600 border-white shadow-lg shadow-purple-500/30 text-white z-10 scale-[1.02]'
                        : 'bg-purple-900/70 border-purple-500/50 text-purple-200 hover:bg-purple-800/80'
                    }`}
                    style={{ left: `${leftPx}px`, width: `${widthPx}px` }}
                    title={`${clip.title} (${clip.durationSec}s)`}
                  >
                    <span className="text-[11px] font-semibold truncate leading-tight">
                      {clip.title}
                    </span>
                    <span className="text-[9px] font-mono opacity-80 truncate">
                      LUT: {clip.colorGradeLut}
                    </span>
                  </div>
                );
              })}
            </div>

            {/* Track V1: Primary A-Roll / Base Narrative */}
            <div className="h-12 border-b border-studio-border/40 relative bg-zinc-950/20">
              {v1Clips.map((clip) => {
                const isSelected = selectedTimelineClipId === clip.id;
                const leftPx = clip.timelineStartSec * pxPerSec;
                const widthPx = Math.max(30, clip.durationSec * pxPerSec);

                return (
                  <div
                    key={clip.id}
                    onClick={(e) => {
                      e.stopPropagation();
                      onSelectTimelineClip(clip.id);
                    }}
                    className={`absolute top-1 bottom-1 rounded-md px-2 flex flex-col justify-center border transition-all cursor-pointer ${
                      isSelected
                        ? 'bg-indigo-600 border-white shadow-lg shadow-indigo-500/30 text-white z-10 scale-[1.02]'
                        : 'bg-indigo-950/80 border-indigo-500/50 text-indigo-200 hover:bg-indigo-900/80'
                    }`}
                    style={{ left: `${leftPx}px`, width: `${widthPx}px` }}
                    title={`${clip.title} (${clip.durationSec}s)`}
                  >
                    <div className="flex items-center justify-between">
                      <span className="text-[11px] font-semibold truncate leading-tight">
                        {clip.title}
                      </span>
                      {clip.isCutSlice && (
                        <span className="w-2 h-2 rounded-full bg-amber-400" title="Corte IA Aplicado neste trecho" />
                      )}
                    </div>
                    <span className="text-[9px] font-mono opacity-80 truncate">
                      {clip.device} • {clip.durationSec}s
                    </span>
                  </div>
                );
              })}
            </div>

            {/* Track A1: Dialogue / Voice Track */}
            <div className="h-10 border-b border-studio-border/40 relative bg-zinc-950/40">
              {v1Clips.map((clip) => {
                const leftPx = clip.timelineStartSec * pxPerSec;
                const widthPx = Math.max(30, clip.durationSec * pxPerSec);
                return (
                  <div
                    key={`a1-${clip.id}`}
                    className="absolute top-1 bottom-1 rounded bg-emerald-950/70 border border-emerald-500/40 px-1.5 flex items-center justify-between text-emerald-300 text-[9px] font-mono overflow-hidden pointer-events-none"
                    style={{ left: `${leftPx}px`, width: `${widthPx}px` }}
                  >
                    <span className="truncate">Áudio V1 ({clip.title})</span>
                    <div className="flex items-center space-x-0.5 opacity-60">
                      <div className="w-0.5 h-3 bg-emerald-400" />
                      <div className="w-0.5 h-4 bg-emerald-400" />
                      <div className="w-0.5 h-2 bg-emerald-400" />
                    </div>
                  </div>
                );
              })}
            </div>

            {/* Track A2: Ambience / Music */}
            <div className="h-10 relative bg-zinc-950/20">
              <div
                className="absolute top-1 bottom-1 left-0 rounded bg-cyan-950/60 border border-cyan-500/30 px-2 flex items-center justify-between text-cyan-300 text-[9px] font-mono overflow-hidden pointer-events-none"
                style={{ width: `${totalTimelineWidth - 40}px` }}
              >
                <span>Trilha Sonora: "Amazon Reverberations - Cinematic Epic Ambient"</span>
                <span className="opacity-75">-14 LUFS Rec709 Mix</span>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
};
