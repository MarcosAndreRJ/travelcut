import React, { useState, useEffect } from 'react';
import {
  Film,
  Sparkles,
  Sliders,
  Layers,
  Music,
  Download,
  Settings,
  FolderPlus,
  Play,
  RotateCcw,
  CheckCircle2,
  Tv,
  ChevronDown
} from 'lucide-react';
import {
  StudioTab,
  MediaClip,
  CutSegment,
  SuggestedCut,
  StoryboardScene,
  TimelineClip
} from './types';
import {
  MOCK_MEDIA_CLIPS,
  INITIAL_STORYBOARD,
  INITIAL_TIMELINE_CLIPS
} from './data/mockData';
import { AnalysisView } from './views/AnalysisView';
import { NarrativeView } from './views/NarrativeView';
import { AudioView } from './views/AudioView';
import { ExportView } from './views/ExportView';
import { SettingsView } from './views/SettingsView';
import { TimelineNLE } from './components/TimelineNLE';

export function App() {
  const [activeTab, setActiveTab] = useState<StudioTab>('analysis');
  const [mediaClips, setMediaClips] = useState<MediaClip[]>(MOCK_MEDIA_CLIPS);
  const [selectedClipId, setSelectedClipId] = useState<string>(mediaClips[0].id);
  const [storyboard, setStoryboard] = useState<StoryboardScene[]>(INITIAL_STORYBOARD);
  const [timelineClips, setTimelineClips] = useState<TimelineClip[]>(INITIAL_TIMELINE_CLIPS);

  // Global Transport State
  const [currentTimeSec, setCurrentTimeSec] = useState<number>(14.8);
  const [isPlaying, setIsPlaying] = useState<boolean>(false);
  const [showTimeline, setShowTimeline] = useState<boolean>(true);
  const [notification, setNotification] = useState<string | null>(null);

  // Playback timer simulation
  useEffect(() => {
    let interval: any = null;
    if (isPlaying) {
      interval = setInterval(() => {
        setCurrentTimeSec((prev) => {
          if (prev >= 61.6) return 0;
          return prev + 0.1;
        });
      }, 100);
    }
    return () => {
      if (interval) clearInterval(interval);
    };
  }, [isPlaying]);

  const notify = (msg: string) => {
    setNotification(msg);
    setTimeout(() => setNotification(null), 3000);
  };

  const selectedClip = mediaClips.find((c) => c.id === selectedClipId) || mediaClips[0];

  const handleAcceptCut = (clipId: string, cut: SuggestedCut) => {
    const newTimelineClip: TimelineClip = {
      id: `tl-${Date.now()}`,
      mediaClipId: clipId,
      title: cut.tags.join(' • ') || cut.reason.slice(0, 24),
      trackIndex: 0,
      timelineStartSec: currentTimeSec,
      sourceInSec: cut.startSec,
      sourceOutSec: cut.endSec,
      durationSec: cut.endSec - cut.startSec,
      colorGradeLut: 'Tokyo Warm Sunset',
      transition: 'Cross Dissolve 0.5s'
    };
    setTimelineClips((prev) => [...prev, newTimelineClip]);
    notify(`Corte IA adicionado à Linha do Tempo: ${cut.reason.slice(0, 40)}...`);
  };

  const handleApplyCut = (segment: CutSegment) => {
    // Add segment to timeline
    const newTimelineClip: TimelineClip = {
      id: `tl-${Date.now()}`,
      mediaClipId: selectedClipId,
      title: segment.description,
      trackIndex: 0,
      timelineStartSec: currentTimeSec,
      sourceInSec: segment.startSec,
      sourceOutSec: segment.endSec,
      durationSec: segment.durationSec,
      colorGradeLut: 'Tokyo Warm Sunset',
      transition: 'Cross Dissolve 0.5s'
    };
    setTimelineClips([...timelineClips, newTimelineClip]);
    notify(`Corte adicionado à Linha do Tempo: ${segment.description}`);
  };

  const handleImportClips = () => {
    notify('Buscando novos arquivos de câmera Sony FX3 / A7SIII / DJI Pocket 3...');
  };

  return (
    <div className="w-full h-screen flex flex-col bg-studio-bg text-studio-text overflow-hidden font-sans select-none">
      {/* Top Application Header */}
      <header className="h-14 border-b border-studio-border bg-studio-panel/95 backdrop-blur-md px-4 flex items-center justify-between shrink-0 z-40">
        {/* Brand & Project Info */}
        <div className="flex items-center space-x-4">
          <div className="flex items-center space-x-2.5">
            <div className="w-8 h-8 rounded-xl bg-gradient-to-tr from-indigo-600 via-indigo-500 to-amber-400 flex items-center justify-center shadow-glow-indigo">
              <Film className="w-4 h-4 text-white" />
            </div>
            <div>
              <div className="flex items-center space-x-1.5">
                <span className="font-extrabold text-sm tracking-tight text-white">
                  TravelCut<span className="text-indigo-400">Studio</span>
                </span>
                <span className="text-[10px] font-mono px-1.5 py-0.2 rounded bg-indigo-500/20 text-indigo-300 border border-indigo-500/30 font-bold">
                  v2.4 PRO IA
                </span>
              </div>
            </div>
          </div>

          <div className="h-4 w-[1px] bg-studio-border" />

          {/* Project Switcher */}
          <div className="flex items-center space-x-2 bg-studio-card/80 border border-studio-border px-3 py-1 rounded-xl text-xs">
            <span className="text-studio-subtle font-medium">Projeto:</span>
            <span className="font-bold text-studio-text">Japão 2026 - Tóquio & Kyoto 4K</span>
            <ChevronDown className="w-3.5 h-3.5 text-studio-subtle" />
          </div>
        </div>

        {/* Center: Navigation Tabs */}
        <nav className="flex items-center space-x-1 bg-studio-card p-1 rounded-xl border border-studio-border">
          {[
            { id: 'analysis', label: '1. Mídia & Transcrição', icon: Film },
            { id: 'narrative', label: '2. Roteiro & Storyboard', icon: Layers },
            { id: 'audio', label: '3. Áudio & Beats', icon: Music },
            { id: 'export', label: '4. Exportação & NLE', icon: Download },
            { id: 'settings', label: '5. Configurações IA', icon: Settings },
          ].map((tab) => {
            const Icon = tab.icon;
            const isActive = activeTab === tab.id;
            return (
              <button
                key={tab.id}
                onClick={() => setActiveTab(tab.id as StudioTab)}
                className={`flex items-center space-x-2 px-3 py-1.5 rounded-lg text-xs font-semibold transition-all ${
                  isActive
                    ? 'bg-indigo-600 text-white shadow-glow-indigo'
                    : 'text-studio-muted hover:text-studio-text hover:bg-studio-panel'
                }`}
              >
                <Icon className="w-3.5 h-3.5" />
                <span>{tab.label}</span>
              </button>
            );
          })}
        </nav>

        {/* Right Action Buttons */}
        <div className="flex items-center space-x-3">
          <button
            onClick={handleImportClips}
            className="flex items-center space-x-1.5 bg-studio-card hover:bg-studio-cardHover border border-studio-border text-xs text-studio-text px-3 py-1.5 rounded-lg font-semibold transition-colors"
          >
            <FolderPlus className="w-3.5 h-3.5 text-indigo-400" />
            <span>Importar Clipes</span>
          </button>

          <button
            onClick={() => setActiveTab('export')}
            className="flex items-center space-x-1.5 bg-gradient-to-r from-emerald-600 to-teal-600 hover:from-emerald-500 hover:to-teal-500 text-white text-xs px-3.5 py-1.5 rounded-lg font-bold shadow-glow-emerald transition-all"
          >
            <Download className="w-3.5 h-3.5" />
            <span>Exportar Vídeo</span>
          </button>
        </div>
      </header>

      {/* Global Notification Toast */}
      {notification && (
        <div className="absolute top-16 right-4 z-50 bg-indigo-600/95 text-white px-4 py-2.5 rounded-xl shadow-panel border border-indigo-400/40 flex items-center space-x-2 text-xs font-semibold backdrop-blur-md animate-fade-in">
          <Sparkles className="w-4 h-4 text-amber-300" />
          <span>{notification}</span>
        </div>
      )}

      {/* Main Studio Viewport */}
      <main className="flex-1 flex overflow-hidden relative">
        {activeTab === 'analysis' && (
          <AnalysisView
            clips={mediaClips}
            selectedClip={selectedClip}
            onSelectClip={(c) => setSelectedClipId(c.id)}
            onAcceptCut={handleAcceptCut}
            onNavigateToTimeline={() => setShowTimeline(true)}
            onNavigateToStoryboard={() => setActiveTab('narrative')}
          />
        )}

        {activeTab === 'narrative' && (
          <NarrativeView
            storyboard={storyboard}
            clips={mediaClips}
            onUpdateStoryboard={setStoryboard}
            onNavigateToNLE={() => setActiveTab('analysis')}
            onNavigateToAudio={() => setActiveTab('audio')}
          />
        )}

        {activeTab === 'audio' && (
          <AudioView
            onNavigateToNLE={() => setActiveTab('analysis')}
            onNavigateToExport={() => setActiveTab('export')}
          />
        )}

        {activeTab === 'export' && <ExportView />}

        {activeTab === 'settings' && <SettingsView />}
      </main>

      {/* Persistent Multi-track NLE Timeline Footer */}
      {showTimeline && (
        <TimelineNLE
          timelineClips={timelineClips}
          allMediaClips={mediaClips}
          currentTimeSec={currentTimeSec}
          onSeek={setCurrentTimeSec}
          isPlaying={isPlaying}
          onTogglePlay={() => setIsPlaying(!isPlaying)}
          onSelectClip={(clipId) => setSelectedClipId(clipId)}
        />
      )}
    </div>
  );
}

export default App;
