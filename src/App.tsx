import React, { useState, useEffect, useMemo } from 'react';
import { Sparkles } from 'lucide-react';
import { NavigationTab } from './types/navigation';
import {
  MediaClip,
  CutSegment,
  SuggestedCut,
  StoryboardScene,
  TimelineClip,
  ClipCategory,
  QualityGrade
} from './types';

// Layout & Foundation Components
import { AppShell } from './components/layout';
import { useProject } from './contexts/ProjectContext';
import { NewProjectModal } from './components/modals/NewProjectModal';

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
  const [isNewProjectModalOpen, setIsNewProjectModalOpen] = useState(false);

  const { project, openFolderDialogAndLoad, loadProjectFromFolder } = useProject();
  const projectName = project.name || 'Nenhum Projeto Aberto';
  const projectPath = project.folderPath || '';

  // Synchronize media clips with ProjectContext
  const mediaClips: MediaClip[] = useMemo(() => {
    if (!project.clips || project.clips.length === 0) return [];
    return project.clips.map((clip) => ({
      id: clip.id,
      title: clip.title || clip.filename,
      filename: clip.filename,
      location: clip.location || '',
      date: clip.date || '',
      time: '12:00',
      durationSec: clip.durationSec || 0,
      durationFormatted: clip.duration || '00:00',
      resolution: (clip.resolution as MediaClip['resolution']) || '1080p 60fps',
      fps: 60,
      camera: clip.camera || 'Câmera Principal',
      category: (clip.category as ClipCategory) || 'Paisagem',
      thumbnail: clip.thumbnail || '',
      videoUrl: clip.filePath,
      quality: 'Boa' as QualityGrade,
      stability: 'Gimbal Suave',
      facesCount: 0,
      aiScore: clip.aiScore || 80,
      aiAnalysisSummary: 'Aguardando processamento detalhado de IA.',
      suggestedCuts: [],
      transcription: [],
      waveformPoints: [20, 45, 60, 40, 70, 85, 60, 40, 50, 70, 65, 45],
      filePath: clip.filePath,
    }));
  }, [project.clips]);

  const [selectedClipIdState, setSelectedClipIdState] = useState<string>('');
  const selectedClipId = selectedClipIdState || mediaClips[0]?.id || '';
  const setSelectedClipId = setSelectedClipIdState;

  const [storyboard, setStoryboard] = useState<StoryboardScene[]>([]);
  const [timelineClips, setTimelineClips] = useState<TimelineClip[]>([]);

  // Playback & Timeline Transport State
  const [currentTimeSec, setCurrentTimeSec] = useState<number>(0);
  const [isPlaying, setIsPlaying] = useState<boolean>(false);
  const [showTimeline, setShowTimeline] = useState<boolean>(true);
  const [notification, setNotification] = useState<string | null>(null);

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

  const handleImportClips = async () => {
    notify('Abrindo seletor nativo para importar pasta de mídias...');
    const selected = await openFolderDialogAndLoad();
    if (selected) {
      notify(`Pasta importada com sucesso: ${selected}`);
    }
  };

  const handleQuickExport = () => {
    setActiveTab('export');
    notify('Abrindo módulo de exportação XML para o DaVinci Resolve.');
  };

  const handleCreateNewProject = async (name: string, folder: string) => {
    notify(`Inicializando projeto "${name}" e escaneando pasta...`);
    await loadProjectFromFolder(folder, name);
    setActiveTab('project');
    notify(`Projeto "${name}" inicializado com sucesso.`);
  };

  return (
    <>
      <AppShell
        activeTab={activeTab}
        onTabChange={setActiveTab}
        projectName={projectName}
        projectPath={projectPath}
        onQuickImport={handleImportClips}
        onQuickExport={handleQuickExport}
        onNewProject={() => setIsNewProjectModalOpen(true)}
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
            onNewProject={() => setIsNewProjectModalOpen(true)}
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

    <NewProjectModal
      isOpen={isNewProjectModalOpen}
      onClose={() => setIsNewProjectModalOpen(false)}
      onCreateProject={handleCreateNewProject}
    />
  </>
  );
}

export default App;
