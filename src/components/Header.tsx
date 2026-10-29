import React from 'react';
import {
  Film,
  Sparkles,
  Layers,
  Music,
  Download,
  Settings,
  Tv,
  CheckCircle2,
  Cpu,
  ChevronDown,
  Plus,
  PlaySquare,
  Undo2,
  Redo2,
  Maximize2
} from 'lucide-react';
import { StudioTab } from '../types';

interface HeaderProps {
  activeTab: StudioTab;
  onTabChange: (tab: StudioTab) => void;
  projectName?: string;
  totalDurationFormatted?: string;
  onQuickImport?: () => void;
  onQuickExport?: () => void;
}

export const Header: React.FC<HeaderProps> = ({
  activeTab,
  onTabChange,
  projectName = 'Viagem Japão 2024 (4K 60fps)',
  totalDurationFormatted = '00:01:01:15',
  onQuickImport,
  onQuickExport
}) => {
  const tabs: { id: StudioTab; label: string; icon: React.ComponentType<{ className?: string }>; keyNum: string }[] = [
    { id: 'analysis', label: '1. Análise & Transcrição', icon: Sparkles, keyNum: '1' },
    { id: 'narrative', label: '2. Roteiro & Storyboard', icon: Layers, keyNum: '2' },
    { id: 'overview', label: '3. Montagem Geral NLE', icon: Tv, keyNum: '3' },
    { id: 'audio', label: '4. Áudio & Música', icon: Music, keyNum: '4' },
    { id: 'export', label: '5. Exportação & Entrega', icon: Download, keyNum: '5' },
    { id: 'settings', label: '6. Configurações & IA', icon: Settings, keyNum: '6' },
  ];

  return (
    <header className="h-14 bg-studio-panel border-b border-studio-border flex items-center justify-between px-3 shrink-0 z-30 select-none">
      {/* Brand & Project Info */}
      <div className="flex items-center space-x-3">
        <div className="flex items-center space-x-2 bg-gradient-to-r from-indigo-600 to-indigo-700 px-2.5 py-1.5 rounded-lg shadow-glow-indigo">
          <Film className="w-5 h-5 text-white" />
          <span className="font-extrabold tracking-tight text-white text-sm flex items-center gap-1.5">
            TravelCut <span className="bg-white/20 text-indigo-100 text-[10px] px-1.5 py-0.5 rounded uppercase font-mono font-bold tracking-wider">Studio</span>
          </span>
        </div>

        {/* Project Selector Badge */}
        <div className="hidden lg:flex items-center space-x-2 bg-studio-card hover:bg-studio-cardHover border border-studio-border px-3 py-1.5 rounded-lg cursor-pointer transition-colors group">
          <div className="w-2 h-2 rounded-full bg-emerald-500 led-indicator text-emerald-400"></div>
          <span className="text-xs font-semibold text-studio-text group-hover:text-white max-w-[200px] truncate">
            {projectName}
          </span>
          <ChevronDown className="w-3.5 h-3.5 text-studio-subtle group-hover:text-studio-muted transition-transform group-hover:translate-y-0.5" />
        </div>

        {/* History Controls */}
        <div className="hidden xl:flex items-center space-x-1 pl-1 border-l border-studio-border">
          <button title="Desfazer (Ctrl+Z)" className="p-1.5 hover:bg-studio-card rounded text-studio-subtle hover:text-studio-text transition-colors">
            <Undo2 className="w-3.5 h-3.5" />
          </button>
          <button title="Refazer (Ctrl+Y)" className="p-1.5 hover:bg-studio-card rounded text-studio-subtle hover:text-studio-text transition-colors">
            <Redo2 className="w-3.5 h-3.5" />
          </button>
        </div>
      </div>

      {/* Main Studio Navigation Tabs */}
      <nav className="flex items-center space-x-1 bg-studio-card/80 p-1 rounded-xl border border-studio-border/70">
        {tabs.map((tab) => {
          const Icon = tab.icon;
          const isActive = activeTab === tab.id;
          return (
            <button
              key={tab.id}
              onClick={() => onTabChange(tab.id)}
              className={`flex items-center space-x-2 px-3 py-1.5 rounded-lg text-xs font-medium transition-all duration-150 relative ${
                isActive
                  ? 'bg-gradient-to-r from-indigo-600 to-indigo-500 text-white shadow-glow-indigo font-semibold'
                  : 'text-studio-muted hover:text-studio-text hover:bg-studio-cardHover'
              }`}
            >
              <Icon className={`w-3.5 h-3.5 ${isActive ? 'text-white' : 'text-studio-subtle'}`} />
              <span className="hidden md:inline">{tab.label}</span>
              <span className="md:hidden font-mono text-[10px] bg-black/30 px-1 rounded">{tab.keyNum}</span>
            </button>
          );
        })}
      </nav>

      {/* Right Controls & AI Engine Status */}
      <div className="flex items-center space-x-3">
        {/* Master Timeline Clock */}
        <div className="hidden md:flex items-center space-x-2 bg-black/40 border border-studio-border px-2.5 py-1 rounded-lg">
          <PlaySquare className="w-3.5 h-3.5 text-indigo-400" />
          <span className="font-mono text-xs text-studio-text font-bold tracking-wider">
            {totalDurationFormatted}
          </span>
        </div>

        {/* AI Engine Status Badge */}
        <div className="hidden sm:flex items-center space-x-1.5 bg-studio-card border border-studio-border px-2.5 py-1 rounded-lg text-[11px] text-studio-muted" title="Motor Local de IA Acelerado via GPU">
          <Cpu className="w-3.5 h-3.5 text-indigo-400 animate-pulse" />
          <span className="text-studio-text font-medium">Whisper v3 + CUDA</span>
          <CheckCircle2 className="w-3 h-3 text-emerald-400" />
        </div>

        {/* Actions */}
        <div className="flex items-center space-x-1.5">
          <button
            onClick={onQuickImport}
            className="flex items-center space-x-1 bg-studio-card hover:bg-studio-cardHover border border-studio-border hover:border-studio-borderLight px-2.5 py-1.5 rounded-lg text-xs font-medium text-studio-text transition-all"
            title="Importar clipes ou pasta de viagem"
          >
            <Plus className="w-3.5 h-3.5 text-indigo-400" />
            <span className="hidden sm:inline">Importar</span>
          </button>

          <button
            onClick={onQuickExport}
            className="flex items-center space-x-1 bg-gradient-to-r from-emerald-600 to-teal-600 hover:from-emerald-500 hover:to-teal-500 text-white px-3 py-1.5 rounded-lg text-xs font-semibold shadow-glow-emerald transition-all"
            title="Renderizar e exportar vídeo final"
          >
            <Download className="w-3.5 h-3.5" />
            <span>Exportar</span>
          </button>

          <button
            onClick={() => {
              if (!document.fullscreenElement) {
                document.documentElement.requestFullscreen().catch(() => {});
              } else {
                document.exitFullscreen().catch(() => {});
              }
            }}
            className="p-1.5 hover:bg-studio-card rounded-lg text-studio-subtle hover:text-studio-text transition-colors"
            title="Alternar Tela Cheia"
          >
            <Maximize2 className="w-4 h-4" />
          </button>
        </div>
      </div>
    </header>
  );
};
