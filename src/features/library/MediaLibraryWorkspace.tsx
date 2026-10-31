import React, { useState, useEffect } from 'react';
import { ProjectHeader } from './components/ProjectHeader';
import { ProjectStatsCards } from './components/ProjectStatsCards';
import { MediaGrid } from './components/MediaGrid';
import { ProcessingQueue } from './components/ProcessingQueue';
import { VideoPlayerPreview } from './components/VideoPlayerPreview';
import { TranscriptInspector } from './components/TranscriptInspector';
import { ProjectSummaryCard } from './components/ProjectSummaryCard';
import {
  RORAIMA_LIBRARY_CLIPS,
  INITIAL_PROJECT_HEADER,
  INITIAL_PROJECT_SUMMARY,
  INITIAL_PROCESSING_QUEUE
} from './data/libraryMockData';
import { LibraryClip, TranscriptLine, NavTab } from './types';
import { useProject } from '../../contexts/ProjectContext';

interface MediaLibraryWorkspaceProps {
  onNavigateTab?: (tab: NavTab | string) => void;
}

export const MediaLibraryWorkspace: React.FC<MediaLibraryWorkspaceProps> = ({
  onNavigateTab
}) => {
  const { project, renameProject, setClips, saveCurrentProjectState } = useProject();
  const [headerData, setHeaderData] = useState(INITIAL_PROJECT_HEADER);
  const [summaryData, setSummaryData] = useState(INITIAL_PROJECT_SUMMARY);
  const [tasks, setTasks] = useState(INITIAL_PROCESSING_QUEUE);
  const [selectedClipId, setSelectedClipId] = useState<string>('clip-roraima-1');
  const [currentTimeSec, setCurrentTimeSec] = useState<number>(42);
  const [isPlaying, setIsPlaying] = useState<boolean>(false);
  const [toastMessage, setToastMessage] = useState<string | null>(null);

  const clips = project.clips && project.clips.length > 0 ? project.clips : RORAIMA_LIBRARY_CLIPS;

  // Sync header with project context
  useEffect(() => {
    if (project.name) {
      setHeaderData((prev) => ({
        ...prev,
        title: project.name,
      }));
    }
  }, [project.name]);

  const selectedClip =
    clips.find((c) => c.id === selectedClipId) || clips[0] || RORAIMA_LIBRARY_CLIPS[0];

  // Playback timer effect
  // O controle de reproducao e timecode e gerenciado nativamente pelo VideoPlayerPreview (eventos timeupdate do elemento <video>)
  const showToast = (msg: string) => {
    setToastMessage(msg);
    setTimeout(() => {
      setToastMessage(null);
    }, 3500);
  };

  const handleSelectClip = (clip: LibraryClip) => {
    setSelectedClipId(clip.id);
    setCurrentTimeSec(0);
    setIsPlaying(false);
  };

  const handleUpdateClipCategory = (clipId: string, newCategory: LibraryClip['category']) => {
    const updatedClips = clips.map((c) =>
      c.id === clipId ? { ...c, category: newCategory, isUserOverridden: true } : c
    );
    setClips(updatedClips);
    saveCurrentProjectState();
    showToast(`Tomada reclassificada como "${newCategory}"`);
  };

  const handleUpdateTitle = (newTitle: string) => {
    renameProject(newTitle);
    setHeaderData((prev) => ({ ...prev, title: newTitle }));
    showToast(`Título atualizado para "${newTitle}"`);
    saveCurrentProjectState();
  };

  const handleStepClick = (stepNumber: number) => {
    if (stepNumber === 1 || stepNumber === 2) {
      showToast(`Etapa ${stepNumber} já foi concluída.`);
    } else if (stepNumber === 3) {
      onNavigateTab?.('narrative');
    } else if (stepNumber === 4) {
      onNavigateTab?.('review');
    } else if (stepNumber === 5) {
      onNavigateTab?.('export');
    }
  };

  const handleAcceptCut = (cutLine: TranscriptLine) => {
    setSummaryData((prev) => ({
      ...prev,
      autoCutsCount: prev.autoCutsCount + 1,
      reviewCutsCount: Math.max(0, prev.reviewCutsCount - 1),
    }));
    showToast(`Corte aceito em ${cutLine.timecode}: hesitação removida da timeline.`);
  };

  const handleRejectCut = (cutLine: TranscriptLine) => {
    setSummaryData((prev) => ({
      ...prev,
      reviewCutsCount: Math.max(0, prev.reviewCutsCount - 1),
    }));
    showToast(`Corte descartado: trecho mantido na íntegra.`);
  };

  const handleRerunTask = (taskId: string) => {
    setTasks((prev) =>
      prev.map((t) => (t.id === taskId ? { ...t, progress: 100, status: 'completed' } : t))
    );
    showToast('Etapa reexecutada com sucesso!');
  };

  const handleContinueEditing = () => {
    if (onNavigateTab) {
      onNavigateTab('narrative');
    }
  };

  return (
    <div className="flex-1 flex flex-col p-4 sm:p-6 space-y-5 max-w-[1720px] mx-auto w-full relative">
      {/* Toast alert */}
      {toastMessage && (
        <div className="fixed bottom-6 right-6 z-50 bg-blue-600 text-white text-xs font-semibold px-4 py-2.5 rounded-xl shadow-2xl border border-blue-400 flex items-center space-x-2 animate-bounce">
          <span>✓</span>
          <span>{toastMessage}</span>
        </div>
      )}

      {/* Top Project Header */}
      <ProjectHeader
        data={headerData}
        onUpdateTitle={handleUpdateTitle}
        onStepClick={handleStepClick}
      />

      {/* Top 4 Metrics Summary Cards */}
      <ProjectStatsCards />

      {/* Main Two-Column Layout */}
      <div className="grid grid-cols-1 lg:grid-cols-12 gap-5 items-start">
        {/* Left / Center Column: Media Grid + Processing Queue */}
        <div className="lg:col-span-7 xl:col-span-8 flex flex-col space-y-5">
          <MediaGrid
            clips={clips}
            selectedClipId={selectedClipId}
            onSelectClip={handleSelectClip}
            onOpenClipMenu={(clip) => showToast(`Opções do arquivo ${clip.filename}`)}
            onUpdateClipCategory={handleUpdateClipCategory}
          />

          <ProcessingQueue tasks={tasks} onRerunTask={handleRerunTask} />
        </div>

        {/* Right Column: Preview Player + Tabbed Inspector + Project Summary */}
        <div className="lg:col-span-5 xl:col-span-4 flex flex-col space-y-4">
          <VideoPlayerPreview
            clip={selectedClip}
            currentTimeSec={currentTimeSec}
            onSeek={(newSec) => setCurrentTimeSec(newSec)}
            isPlaying={isPlaying}
            onTogglePlay={() => setIsPlaying(!isPlaying)}
          />

          <TranscriptInspector
            clip={selectedClip}
            currentTimeSec={currentTimeSec}
            onSeek={(newSec) => setCurrentTimeSec(newSec)}
            onAcceptCut={handleAcceptCut}
            onRejectCut={handleRejectCut}
          />

          <ProjectSummaryCard
            summary={summaryData}
            onContinueEditing={handleContinueEditing}
          />
        </div>
      </div>
    </div>
  );
};
