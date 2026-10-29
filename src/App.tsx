import React, { useState, useEffect } from 'react';
import { Sparkles } from 'lucide-react';
import { NavigationTab } from './types/navigation';
import {
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

// Layout & Foundation Components
import { AppShell } from './components/layout';

// Features & Views
import { HomeDashboard } from './features/home/HomeDashboard';
import { MediaLibraryWorkspace } from './features/library';
import './features/media';

// Placeholders (Phase 1 Foundation & Phase 2 Contract)
import {
  ReviewPlaceholderView
} from './app/placeholders';

// Views
import { AnalysisView } from './views/AnalysisView';
import { NarrativeView } from './views/NarrativeView';
import { ExportView } from './views/ExportView';
import { SettingsView } from './views/SettingsView';
import { TimelineNLE } from './components/TimelineNLE';

export function App() {
  // Navigation State (Simple, typed, without unnecessary heavy router libraries)
  const [activeTab, setActiveTab] = useState<NavigationTab>('home');

  // Media & Project State
  const [mediaClips, setMediaClips] = useState<MediaClip[]>(MOCK_MEDIA_CLIPS);
  const [selectedClipId, setSelectedClipId] = useState<string>(mediaClips[0]?.id || '');
  const [storyboard, setStoryboard] = useState<StoryboardScene[]>(INITIAL_STORYBOARD);
  const [timelineClips, setTimelineClips] = useState<TimelineClip[]>(INITIAL_TIMELINE_CLIPS);

  // Playback & Timeline Transport State
  const [currentTimeSec, setCurrentTimeSec] = useState<number>(14.8);
  const [isPlaying, setIsPlaying] = useState<boolean>(false);
  const [showTimeline, setShowTimeline] = useState<boolean>(true);
  const [notification, setNotification] = useState<string | null>(null);

  const projectName = 'Viagem – Boa Vista';
  const projectPath = 'D:\\Videos\\Viagem_BoaVista';

  // Playback timer simulation
  useEffect(() => {
    let interval: ReturnType<typeof setInterval> | null = null;
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
    setTimeout(() => setNotification(null), 3500);
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
    notify(`Corte IA aprovado e inserido na Linha do Tempo: ${cut.reason.slice(0, 42)}...`);
  };

  const handleImportClips = () => {
    notify('Escaneando cartão SD e SSD NVMe em busca de mídias Sony FX3 e DJI...');
  };

  const handleQuickExport = () => {
    setActiveTab('export');
    notify('Abrindo módulo de exportação XML para o DaVinci Resolve.');
  };

  return (
    <AppShell
      activeTab={activeTab}
      onTabChange={setActiveTab}
      projectName={projectName}
      projectPath={projectPath}
      onQuickImport={handleImportClips}
      onQuickExport={handleQuickExport}
      timelineVisible={activeTab !== 'home' && showTimeline}
      onToggleTimeline={() => setShowTimeline(!showTimeline)}
      timelineSlot={
        <TimelineNLE
          timelineClips={timelineClips}
          allMediaClips={mediaClips}
          currentTimeSec={currentTimeSec}
          onSeek={setCurrentTimeSec}
          isPlaying={isPlaying}
          onTogglePlay={() => setIsPlaying(!isPlaying)}
          onSelectClip={(clipId) => {
            setSelectedClipId(clipId);
            setActiveTab('analysis');
          }}
        />
      }
    >
      {/* Global Notification Toast */}
      {notification && (
        <div className="absolute top-4 right-4 z-50 bg-blue-600/95 text-white px-4 py-2.5 rounded-xl shadow-panel border border-blue-400/40 flex items-center space-x-2 text-xs font-semibold backdrop-blur-md">
          <Sparkles className="w-4 h-4 text-amber-300 shrink-0" />
          <span>{notification}</span>
        </div>
      )}

      {/* Dynamic Viewport Content */}
      {activeTab === 'home' && (
        <HomeDashboard
          onNavigateToTab={(tab) => setActiveTab(tab as NavigationTab)}
        />
      )}

      {activeTab === 'project' && (
        <MediaLibraryWorkspace
          onNavigateTab={(tab) => {
            if (tab === 'narrative') {
              setActiveTab('edit');
            } else if (
              tab === 'home' ||
              tab === 'project' ||
              tab === 'analysis' ||
              tab === 'edit' ||
              tab === 'review' ||
              tab === 'export' ||
              tab === 'settings'
            ) {
              setActiveTab(tab as NavigationTab);
            }
          }}
        />
      )}

      {activeTab === 'analysis' && (
        <AnalysisView
          clips={mediaClips}
          selectedClip={selectedClip}
          onSelectClip={(c) => setSelectedClipId(c.id)}
          onAcceptCut={handleAcceptCut}
          onNavigateToTimeline={() => setShowTimeline(true)}
          onNavigateToStoryboard={() => setActiveTab('edit')}
        />
      )}

      {activeTab === 'edit' && (
        <NarrativeView
          storyboard={storyboard}
          clips={mediaClips}
          onUpdateStoryboard={setStoryboard}
          onNavigateToNLE={() => setActiveTab('analysis')}
          onNavigateToAudio={() => setActiveTab('review')}
        />
      )}

      {activeTab === 'review' && (
        <ReviewPlaceholderView
          onNavigateToTab={(tab) => setActiveTab(tab as NavigationTab)}
        />
      )}

      {activeTab === 'export' && <ExportView />}

      {activeTab === 'settings' && <SettingsView />}
    </AppShell>
  );
}

export default App;
