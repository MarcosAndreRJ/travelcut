import React, { useState, useEffect } from 'react';
import { Sidebar } from './Sidebar';
import { NavigationTab } from '../../types/navigation';
import {
  Folder,
  Minus,
  Square,
  X,
  Plus
} from 'lucide-react';

export interface AppShellProps {
  activeTab: NavigationTab;
  onTabChange: (tab: NavigationTab) => void;
  projectName?: string;
  projectPath?: string;
  onNewProject?: () => void;
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
  projectName = 'Viagem – Boa Vista',
  projectPath = 'D:\\Videos\\Viagem_BoaVista',
  onNewProject,
  timelineVisible = false,
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
    <div className="w-screen h-screen flex bg-[#070b12] text-slate-100 overflow-hidden font-sans select-none">
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
        {/* Top Desktop Window Bar matching 01_Painel_Inicial.png */}
        <header className="h-12 border-b border-slate-800/80 bg-[#0a0e17] px-4 flex items-center justify-between shrink-0 z-20">
          {/* Left: Active Project Folder Path & New Project Action */}
          <div className="flex items-center space-x-3 text-xs text-slate-300 min-w-0">
            {onNewProject && (
              <button
                type="button"
                onClick={onNewProject}
                className="flex items-center space-x-1.5 px-2.5 py-1 bg-blue-600/20 hover:bg-blue-600/30 text-blue-400 hover:text-blue-300 border border-blue-500/30 rounded-lg text-xs font-medium transition-colors shrink-0"
                title="Criar ou carregar novo projeto"
              >
                <Plus size={13} />
                <span>Novo Projeto</span>
              </button>
            )}
            <div className="flex items-center space-x-2 min-w-0">
              <Folder size={15} className="text-cyan-400 shrink-0" />
              <span className="font-mono text-[12px] text-slate-300 truncate hover:text-white transition-colors cursor-pointer" title={projectPath}>
                {projectPath || 'Nenhuma pasta selecionada'}
              </span>
            </div>
          </div>

          {/* Right: User Avatar + Window Controls */}
          <div className="flex items-center space-x-4">
            {/* User Avatar Badge: VC */}
            <div
              className="w-7 h-7 rounded-full bg-gradient-to-tr from-cyan-600 to-teal-500 border border-cyan-400/30 flex items-center justify-center text-[11px] font-bold text-white shadow-sm cursor-pointer hover:ring-2 hover:ring-cyan-400/40 transition-all"
              title="Perfil: Vinicius Castro (VC)"
            >
              VC
            </div>

            {/* Window Controls: Minimize, Maximize, Close */}
            <div className="flex items-center space-x-1 text-slate-400">
              <button
                type="button"
                className="w-7 h-7 flex items-center justify-center rounded hover:bg-slate-800 hover:text-white transition-colors"
                title="Minimizar"
                aria-label="Minimizar janela"
              >
                <Minus size={13} />
              </button>
              <button
                type="button"
                className="w-7 h-7 flex items-center justify-center rounded hover:bg-slate-800 hover:text-white transition-colors"
                title="Maximizar"
                aria-label="Maximizar janela"
              >
                <Square size={11} />
              </button>
              <button
                type="button"
                className="w-7 h-7 flex items-center justify-center rounded hover:bg-rose-600 hover:text-white transition-colors"
                title="Fechar"
                aria-label="Fechar janela"
              >
                <X size={13} />
              </button>
            </div>
          </div>
        </header>

        {/* Dynamic Page Viewport (Scrollable container) */}
        <main className="flex-1 min-h-0 overflow-y-auto overflow-x-hidden relative flex flex-col bg-[#070b12]">
          {children}
        </main>

        {/* Optional Multi-track Timeline or Bottom Slot (for editing views) */}
        {timelineVisible && timelineSlot && (
          <div className="shrink-0 border-t border-slate-800 bg-[#0d131f] z-20">
            {timelineSlot}
          </div>
        )}
      </div>
    </div>
  );
};
