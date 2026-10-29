import React, { useRef } from 'react';
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
  Video
} from 'lucide-react';
import { useEditingWorkspace } from '../context/EditingContext';
import { secondsToTimecode } from '../utils/davinciExporter';

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

  const [isMuted, setIsMuted] = React.useState(false);
  const progressBarRef = useRef<HTMLDivElement>(null);

  const duration = selectedAsset.durationSec || 120;
  const progressPercent = Math.min(100, (currentTimeSec / duration) * 100);

  // Check if current playhead is inside any cut zone
  const activeCutInPlayhead = selectedAsset.cutSuggestions.find(
    (c) => currentTimeSec >= c.startSec && currentTimeSec <= c.endSec
  );

  const handleProgressClick = (e: React.MouseEvent<HTMLDivElement>) => {
    if (!progressBarRef.current) return;
    const rect = progressBarRef.current.getBoundingClientRect();
    const clickX = e.clientX - rect.left;
    const ratio = Math.max(0, Math.min(1, clickX / rect.width));
    seekToTime(ratio * duration);
  };

  return (
    <div className="flex-1 flex flex-col bg-studio-bg overflow-hidden border-b border-studio-border relative">
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
        {/* Mock Video Canvas with Photographic Depth */}
        <div className="w-full h-full relative flex items-center justify-center">
          <img
            src={selectedAsset.thumbnail}
            alt={selectedAsset.displayName}
            className={`w-full h-full object-cover transition-all duration-300 ${
              activeCutInPlayhead && activeCutInPlayhead.decisionState === 'accepted' && viewModeAB !== 'before_cuts'
                ? 'opacity-30 blur-sm grayscale'
                : 'opacity-90'
            }`}
          />

          {/* Color Grade / LUT Indicator Overlay */}
          <div className="absolute top-3 left-3 bg-black/70 backdrop-blur-md px-2.5 py-1 rounded-md border border-white/10 text-[11px] font-mono text-zinc-300 flex items-center space-x-2">
            <span className="w-2 h-2 rounded-full bg-emerald-400 animate-pulse"></span>
            <span>{selectedAsset.colorProfile}</span>
            <span className="text-zinc-500">|</span>
            <span className="text-indigo-300">{selectedAsset.device}</span>
          </div>

          {/* Timecode Overlay Display */}
          <div className="absolute top-3 right-3 bg-black/85 backdrop-blur-md px-3 py-1.5 rounded-lg border border-indigo-500/30 text-indigo-300 font-mono text-sm tracking-wider shadow-lg">
            {secondsToTimecode(currentTimeSec, selectedAsset.frameRate)}
          </div>

          {/* Active Cut Zone Banner */}
          {activeCutInPlayhead && (
            <div className={`absolute bottom-4 left-1/2 -translate-x-1/2 px-4 py-2 rounded-xl backdrop-blur-md border shadow-2xl flex items-center space-x-3 transition-all animate-fade-in ${
              activeCutInPlayhead.decisionState === 'accepted'
                ? 'bg-red-950/90 border-red-500/50 text-red-200'
                : 'bg-amber-950/90 border-amber-500/50 text-amber-200'
            }`}>
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
        {/* Scrubber Bar with Cut Highlights */}
        <div
          ref={progressBarRef}
          onClick={handleProgressClick}
          className="h-4 bg-studio-card rounded-md relative cursor-pointer group flex items-center border border-studio-border overflow-hidden"
        >
          {/* Audio Waveform visualization */}
          <div className="absolute inset-0 flex items-center justify-between px-1 pointer-events-none opacity-30">
            {selectedAsset.waveformPoints.map((height, i) => (
              <div
                key={i}
                className="w-1 bg-indigo-400 rounded-full"
                style={{ height: `${height}%` }}
              />
            ))}
          </div>

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
        </div>

        {/* Transport & Control Buttons */}
        <div className="flex items-center justify-between text-xs">
          <div className="flex items-center space-x-2">
            <button
              onClick={() => seekToTime(0)}
              className="p-1.5 hover:bg-studio-card rounded-md text-studio-muted hover:text-white"
              title="Voltar ao início"
            >
              <RotateCcw className="w-3.5 h-3.5" />
            </button>
            <button
              onClick={togglePlay}
              className="px-3 py-1.5 bg-indigo-600 hover:bg-indigo-500 text-white rounded-lg font-medium flex items-center space-x-1 shadow-glow-indigo"
            >
              {isPlaying ? <Pause className="w-3.5 h-3.5" /> : <Play className="w-3.5 h-3.5" />}
              <span>{isPlaying ? 'Pausar' : 'Reproduzir'}</span>
            </button>

            {/* Time readout */}
            <div className="font-mono text-studio-text font-semibold ml-2">
              <span>{secondsToTimecode(currentTimeSec, selectedAsset.frameRate)}</span>
              <span className="text-studio-subtle mx-1.5">/</span>
              <span className="text-studio-muted">{secondsToTimecode(duration, selectedAsset.frameRate)}</span>
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
            >
              {isMuted ? <VolumeX className="w-4 h-4 text-red-400" /> : <Volume2 className="w-4 h-4" />}
            </button>
          </div>
        </div>
      </div>
    </div>
  );
};
