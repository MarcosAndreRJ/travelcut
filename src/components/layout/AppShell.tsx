import React, { useState, useEffect } from 'react';
import { Sidebar } from './Sidebar';
import { NavigationTab } from '../../types/navigation';
import {
  ChevronDown,
  FolderPlus,
  Share2,
  Tv,
  CheckCircle2
} from 'lucide-react';
import { Button } from '../ui/Button';

export interface AppShellProps {
  activeTab: NavigationTab;
  onTabChange: (tab: NavigationTab) => void;
  projectName?: string;
  onQuickImport?: () => void;
  onQuickExport?: () => void;
  timelineVisible?: boolean;
  onToggleTimeline?: () => void;
  timelineSlot?: React.ReactNode;
  children: React.ReactNode;
}

export const AppShell: React.FC<AppShellProps> = ({
  activeTab,
  onTabChange,
  projectName = 'Viagem Japão 2026 - Tóquio & Kyoto 4K',
  onQuickImport,
  onQuickExport,
  timelineVisible = true,
  onToggleTimeline,
  timelineSlot,
  children
}) => {
  // Sidebar responsive collapse state: default expanded on >=1440, compact on <1440
  const [isSidebarCollapsed, setIsSidebarCollapsed] = useState<boolean>(() => {
    if (typeof window !== 'undefined') {
      return window.innerWidth < 1280;
    }
    return false;
  });

  // Handle responsive window resize
  useEffect(() => {
    const handleResize = () => {
      if (window.innerWidth < 1024) {
        setIsSidebarCollapsed(true);
      }
    };
    window.addEventListener('resize', handleResize);
    return () => window.removeEventListener('resize', handleResize);
  }, []);

  return (
    <div className="w-screen h-screen flex bg-studio-bg text-studio-text overflow-hidden font-sans select-none">
      {/* 1. Global Persistent Sidebar */}
      <Sidebar
        activeTab={activeTab}
        onTabChange={onTabChange}
        isCollapsed={isSidebarCollapsed}
        onToggleCollapse={() => setIsSidebarCollapsed(!isSidebarCollapsed)}
        projectName={projectName}
      />

      {/* 2. Main Workspace Layout */}
      <div className="flex-1 flex flex-col min-w-0 h-full overflow-hidden relative">
        {/* Top App Contextual Header */}
        <header className="h-14 border-b border-studio-border bg-studio-panel/90 backdrop-blur-md px-4 flex items-center justify-between shrink-0 z-20">
          {/* Left: Active Project Selector */}
          <div className="flex items-center space-x-3 min-w-0">
            <div className="flex items-center space-x-2 bg-studio-card/80 hover:bg-studio-card border border-studio-border px-3 py-1.5 rounded-xl text-xs transition-colors cursor-pointer group">
              <span className="w-2 h-2 rounded-full bg-emerald-400 led-indicator shrink-0" />
              <span className="text-studio-muted font-medium hidden sm:inline">Projeto:</span>
              <span className="font-bold text-white truncate max-w-[240px] md:max-w-xs">
                {projectName}
              </span>
              <ChevronDown className="w-3.5 h-3.5 text-studio-subtle group-hover:text-studio-muted shrink-0" />
            </div>

            <div className="hidden lg:flex items-center gap-1.5 text-xs text-studio-muted border-l border-studio-border pl-3">
              <span className="font-mono text-[11px] bg-studio-card px-2 py-0.5 rounded border border-studio-border/60">
                4K 60fps • 10-bit 4:2:2
              </span>
            </div>
          </div>

          {/* Right: Quick Global Workflow Actions */}
          <div className="flex items-center space-x-2">
            {onToggleTimeline && (
              <Button
                variant={timelineVisible ? 'primary' : 'outline'}
                size="sm"
                onClick={onToggleTimeline}
                leftIcon={<Tv className="w-3.5 h-3.5" />}
                className="hidden md:inline-flex text-xs"
                title="Exibir ou ocultar Linha do Tempo inferior"
              >
                <span>Timeline</span>
              </Button>
            )}

            {onQuickImport && (
              <Button
                variant="secondary"
                size="sm"
                onClick={onQuickImport}
                leftIcon={<FolderPlus className="w-3.5 h-3.5 text-blue-400" />}
                className="text-xs"
                title="Importar novos clipes de viagem"
              >
                <span className="hidden sm:inline">Importar Mídia</span>
                <span className="sm:hidden">Importar</span>
              </Button>
            )}

            {onQuickExport && (
              <Button
                variant="turquoise"
                size="sm"
                onClick={onQuickExport}
                leftIcon={<Share2 className="w-3.5 h-3.5" />}
                className="text-xs font-bold"
                title="Exportar montagem para o DaVinci Resolve"
              >
                <span>DaVinci Resolve</span>
              </Button>
            )}
          </div>
        </header>

        {/* Dynamic Page Viewport (Scrollable container) */}
        <main className="flex-1 min-h-0 overflow-y-auto overflow-x-hidden relative flex flex-col bg-studio-bg">
          {children}
        </main>

        {/* Optional Multi-track Timeline or Bottom Slot */}
        {timelineVisible && timelineSlot && (
          <div className="shrink-0 border-t border-studio-border bg-studio-panel z-20">
            {timelineSlot}
          </div>
        )}
      </div>
    </div>
  );
};
