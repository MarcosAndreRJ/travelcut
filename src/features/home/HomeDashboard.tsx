import React, { useState, useEffect } from 'react';
import { ProjectHeroCard } from './components/ProjectHeroCard';
import { FolderCardsGrid } from './components/FolderCardsGrid';
import { PipelineStepper } from './components/PipelineStepper';
import { RecentAnalysesGrid } from './components/RecentAnalysesGrid';
import { QuickActionsCard } from './components/QuickActionsCard';
import { ToolStatusCard } from './components/ToolStatusCard';
import { RecentActivityCard } from './components/RecentActivityCard';
import { initialHomeData } from './data/homeMockData';
import { useProject } from '../../contexts/ProjectContext';
import {
  HomeDashboardData,
  ProjectFolder,
  MediaAnalysisItem,
  QuickAction,
  ToolIntegration,
  RecentActivityItem,
  PipelineStep,
} from './types';

export interface HomeDashboardProps {
  initialData?: HomeDashboardData;
  onNavigateToTab?: (tab: string) => void;
  onOpenFolder?: (path: string) => void;
  onNewProject?: () => void;
  onStartAnalysis?: () => void;
  onMediaSelect?: (media: MediaAnalysisItem) => void;
  className?: string;
}

export const HomeDashboard: React.FC<HomeDashboardProps> = ({
  initialData = initialHomeData,
  onNavigateToTab,
  onOpenFolder,
  onNewProject,
  onStartAnalysis,
  onMediaSelect,
  className = '',
}) => {
  const { project, openFolderDialogAndLoad, renameProject, isScanning } = useProject();
  const [data, setData] = useState<HomeDashboardData>(initialData);
  const [notification, setNotification] = useState<string | null>(null);

  // Sync with global ProjectContext state
  useEffect(() => {
    setData((prev) => ({
      ...prev,
      project: {
        ...prev.project,
        name: project.name,
        folderPath: project.folderPath,
        videoCount: project.mediaCount,
        totalDuration: `${Math.floor(project.totalDurationSeconds / 60)}m ${Math.floor(project.totalDurationSeconds % 60)}s`,
      },
    }));
  }, [project.name, project.folderPath, project.mediaCount, project.totalDurationSeconds]);

  const showNotification = (msg: string) => {
    setNotification(msg);
    setTimeout(() => {
      setNotification((curr) => (curr === msg ? null : curr));
    }, 3500);
  };

  const handleEditTitle = (newTitle: string) => {
    renameProject(newTitle);
    setData((prev) => ({
      ...prev,
      project: {
        ...prev.project,
        name: newTitle,
      },
    }));
    showNotification(`Nome do projeto atualizado para "${newTitle}"`);
  };

  const handleOpenProjectFolder = async () => {
    if (onOpenFolder) {
      onOpenFolder(data.project.folderPath);
    } else {
      showNotification('Abrindo seletor nativo de pasta...');
      const selected = await openFolderDialogAndLoad();
      if (selected) {
        showNotification(`Pasta carregada com sucesso: ${selected}`);
      }
    }
  };

  const handleFolderClick = (folder: ProjectFolder) => {
    if (folder.path && onOpenFolder) {
      onOpenFolder(folder.path);
    } else {
      showNotification(`Pasta selecionada: ${folder.name} (${folder.subtitle})`);
    }
  };

  const handlePipelineStepClick = (step: PipelineStep) => {
    showNotification(`Etapa ${step.number}: ${step.title} (${step.statusLabel})`);
  };

  const handleQuickAction = (action: QuickAction) => {
    switch (action.id) {
      case 'action-full-analysis':
        if (onStartAnalysis) {
          onStartAnalysis();
        } else {
          showNotification('Iniciando análise completa dos 48 vídeos...');
        }
        break;
      case 'action-continue-task':
        showNotification('Continuando tarefa atual: Análise de mídia (65%)');
        break;
      case 'action-open-davinci':
        showNotification('Conectando ao DaVinci Resolve Studio e abrindo projeto...');
        break;
      case 'action-view-report':
        showNotification('Gerando relatório detalhado de edição e metadados...');
        break;
      default:
        action.onClick?.();
        showNotification(`Ação executada: ${action.label}`);
    }
  };

  const handleMediaClick = (item: MediaAnalysisItem) => {
    if (onMediaSelect) {
      onMediaSelect(item);
    } else {
      showNotification(`Mídia selecionada: ${item.filename} (${item.categoryLabel})`);
    }
  };

  const handleToolClick = (tool: ToolIntegration) => {
    showNotification(`${tool.name}: Status ${tool.statusLabel}`);
  };

  const handleActivityClick = (activity: RecentActivityItem) => {
    showNotification(`${activity.title} • ${activity.description}`);
  };

  return (
    <div className={`w-full max-w-[1600px] mx-auto p-4 md:p-6 lg:p-8 space-y-6 text-slate-100 ${className}`}>
      {/* Toast Notification */}
      {notification && (
        <div className="fixed bottom-6 right-6 z-50 px-4 py-2.5 rounded-xl bg-slate-900/95 border border-cyan-500/50 text-cyan-200 text-xs md:text-sm font-medium shadow-2xl shadow-cyan-950/60 backdrop-blur-md animate-fade-in flex items-center gap-2">
          <span className="w-2 h-2 rounded-full bg-cyan-400 animate-ping" />
          <span>{notification}</span>
        </div>
      )}

      {/* 1. Project Hero Card */}
      <section aria-label="Projeto Atual">
        <ProjectHeroCard
          project={data.project}
          onOpenFolder={handleOpenProjectFolder}
          onNewProject={onNewProject}
          onEditTitle={handleEditTitle}
          onMoreOptions={() => showNotification('Opções avançadas do projeto')}
        />
      </section>

      {/* 2. Project Folder Cards Grid (4 cards) */}
      <section aria-label="Pastas do Projeto">
        <FolderCardsGrid
          folders={data.folders}
          onFolderClick={handleFolderClick}
        />
      </section>

      {/* 3. Main Dashboard Layout (Two Columns: Left 2/3, Right 1/3) */}
      <div className="grid grid-cols-1 lg:grid-cols-12 gap-6 items-start">
        {/* Left Column (8 cols): Pipeline & Recent Analyses */}
        <div className="lg:col-span-8 flex flex-col gap-6">
          {/* Pipeline de Edição */}
          <section aria-label="Pipeline de Edição">
            <PipelineStepper
              pipeline={data.pipeline}
              onStepClick={handlePipelineStepClick}
            />
          </section>

          {/* Últimas Análises */}
          <section aria-label="Últimas Análises">
            <RecentAnalysesGrid
              analyses={data.recentAnalyses}
              onViewAll={() => {
                if (onNavigateToTab) {
                  onNavigateToTab('analysis');
                } else {
                  showNotification('Navegando para lista completa de análises...');
                }
              }}
              onItemClick={handleMediaClick}
            />
          </section>
        </div>

        {/* Right Column (4 cols): Quick Actions, Tool Status, Recent Activity */}
        <div className="lg:col-span-4 flex flex-col gap-6">
          {/* Ações Rápidas */}
          <section aria-label="Ações Rápidas">
            <QuickActionsCard
              actions={data.quickActions}
              onActionClick={handleQuickAction}
            />
          </section>

          {/* Status das Ferramentas */}
          <section aria-label="Status das Ferramentas">
            <ToolStatusCard
              tools={data.tools}
              onToolClick={handleToolClick}
            />
          </section>

          {/* Atividade Recente */}
          <section aria-label="Atividade Recente">
            <RecentActivityCard
              activities={data.activities}
              onViewAll={() => showNotification('Exibindo histórico completo de atividades...')}
              onActivityClick={handleActivityClick}
            />
          </section>
        </div>
      </div>
    </div>
  );
};

export default HomeDashboard;
