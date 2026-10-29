import React from 'react';
import {
  Home,
  FolderOpen,
  Sparkles,
  Scissors,
  CheckSquare,
  Film,
  Settings,
  ChevronLeft,
  ChevronRight,
  Mic,
  Bot,
  HardDrive
} from 'lucide-react';
import { NavigationTab, NavigationItem } from '../../types/navigation';
import { Badge } from '../ui/Badge';

export interface SidebarProps {
  activeTab: NavigationTab;
  onTabChange: (tab: NavigationTab) => void;
  isCollapsed?: boolean;
  onToggleCollapse?: () => void;
  projectName?: string;
}

export const NAVIGATION_ITEMS: NavigationItem[] = [
  {
    id: 'home',
    label: 'Início',
    icon: Home,
    description: 'Projetos e visão geral'
  },
  {
    id: 'project',
    label: 'Projeto',
    icon: FolderOpen,
    description: 'Mídias e metadados da viagem'
  },
  {
    id: 'analysis',
    label: 'Análise',
    icon: Sparkles,
    description: 'Detecção de cenas e transcrição'
  },
  {
    id: 'edit',
    label: 'Edição',
    icon: Scissors,
    description: 'Montagem prévia e cortes'
  },
  {
    id: 'review',
    label: 'Revisão',
    icon: CheckSquare,
    description: 'Revisão e refinamento'
  },
  {
    id: 'export',
    label: 'Renderização',
    icon: Film,
    description: 'Exportação e render DaVinci'
  },
  {
    id: 'settings',
    label: 'Configurações',
    icon: Settings,
    description: 'Motores IA e hardware'
  }
];

export const Sidebar: React.FC<SidebarProps> = ({
  activeTab,
  onTabChange,
  isCollapsed = false,
  onToggleCollapse
}) => {
  const renderDaVinciIcon = () => (
    <svg className="w-3.5 h-3.5 shrink-0" viewBox="0 0 24 24" fill="none">
      <circle cx="8" cy="8" r="4.5" fill="#f43f5e" />
      <circle cx="16" cy="8" r="4.5" fill="#38bdf8" />
      <circle cx="12" cy="15" r="4.5" fill="#f59e0b" />
    </svg>
  );

  return (
    <aside
      aria-label="Navegação Principal"
      className={`h-full bg-[#0a0e17] border-r border-slate-800/80 flex flex-col justify-between shrink-0 transition-all duration-200 select-none z-30 ${
        isCollapsed ? 'w-16' : 'w-64'
      }`}
    >
      {/* Top: Logo & Brand */}
      <div className="flex flex-col">
        <div className="h-14 border-b border-slate-800/80 px-4 flex items-center justify-between">
          <div className="flex items-center space-x-3 overflow-hidden">
            <div className="w-8 h-8 rounded-xl bg-cyan-500/10 border border-cyan-500/25 flex items-center justify-center shadow-lg shadow-cyan-500/10 shrink-0">
              <Film className="w-4 h-4 text-cyan-400" />
            </div>

            {!isCollapsed && (
              <div className="flex flex-col min-w-0">
                <div className="flex items-center gap-1">
                  <span className="font-extrabold text-sm tracking-tight text-white truncate">
                    TravelCut <span className="text-white">Studio</span>
                  </span>
                </div>
                <span className="text-[10px] text-slate-400 font-mono tracking-wider font-semibold">
                  v1.0.0
                </span>
              </div>
            )}
          </div>

          {onToggleCollapse && (
            <button
              onClick={onToggleCollapse}
              title={isCollapsed ? 'Expandir barra lateral' : 'Recolher barra lateral'}
              className="p-1 rounded-lg text-slate-400 hover:text-white hover:bg-slate-800 transition-colors shrink-0"
              aria-label={isCollapsed ? 'Expandir barra lateral' : 'Recolher barra lateral'}
            >
              {isCollapsed ? (
                <ChevronRight className="w-4 h-4" />
              ) : (
                <ChevronLeft className="w-4 h-4" />
              )}
            </button>
          )}
        </div>

        {/* Navigation Links */}
        <nav className="p-3 space-y-1.5 overflow-y-auto">
          {NAVIGATION_ITEMS.map((item, idx) => {
            const Icon = item.icon;
            const isActive = activeTab === item.id;
            const isSettings = item.id === 'settings';

            return (
              <React.Fragment key={item.id}>
                {isSettings && (
                  <div className="my-2 border-t border-slate-800/70" />
                )}
                <button
                  onClick={() => onTabChange(item.id)}
                  title={isCollapsed ? `${item.label} - ${item.description || ''}` : undefined}
                  className={`w-full flex items-center gap-3 px-3 py-2.5 rounded-xl text-xs font-semibold transition-all duration-150 group relative cursor-pointer ${
                    isActive
                      ? 'bg-cyan-500/15 text-cyan-300 border border-cyan-500/30 shadow-sm'
                      : 'text-slate-400 hover:text-slate-200 hover:bg-slate-800/60 border border-transparent'
                  }`}
                  aria-current={isActive ? 'page' : undefined}
                >
                  {/* Active Indicator Bar */}
                  {isActive && (
                    <span className="absolute left-0 top-2 bottom-2 w-1 bg-cyan-400 rounded-r-full" />
                  )}

                  <Icon
                    className={`w-4 h-4 shrink-0 transition-colors ${
                      isActive ? 'text-cyan-400' : 'text-slate-400 group-hover:text-slate-200'
                    }`}
                  />

                  {!isCollapsed && (
                    <div className="flex-1 flex items-center justify-between text-left overflow-hidden">
                      <span className="truncate">{item.label}</span>
                      {item.badge && (
                        <Badge
                          variant={item.badgeVariant || 'neutral'}
                          size="xs"
                          className="ml-1.5"
                        >
                          {item.badge}
                        </Badge>
                      )}
                    </div>
                  )}
                </button>
              </React.Fragment>
            );
          })}
        </nav>
      </div>

      {/* Bottom: Integrations & Storage Info */}
      <div className="p-3.5 border-t border-slate-800/80 bg-[#070b12] flex flex-col gap-3">
        {!isCollapsed ? (
          <>
            {/* Integrations Header */}
            <div className="space-y-2">
              <span className="text-[10px] font-bold text-slate-400 uppercase tracking-wider block px-1">
                Integrações
              </span>

              {/* 4 Integrations matching screenshot */}
              <div className="space-y-1.5">
                <div className="flex items-center justify-between text-[11px] px-1 text-slate-300">
                  <span className="flex items-center gap-2 font-medium">
                    {renderDaVinciIcon()}
                    <span>DaVinci Resolve</span>
                  </span>
                  <span className="text-[10px] font-medium text-emerald-400 flex items-center gap-1.5">
                    <span className="w-1.5 h-1.5 rounded-full bg-emerald-400 shadow-[0_0_6px_rgba(52,211,153,0.8)]" />
                    Conectado
                  </span>
                </div>

                <div className="flex items-center justify-between text-[11px] px-1 text-slate-300">
                  <span className="flex items-center gap-2 font-medium">
                    <Mic className="w-3.5 h-3.5 text-cyan-400" />
                    <span>Whisper large-v3</span>
                  </span>
                  <span className="text-[10px] font-medium text-emerald-400 flex items-center gap-1.5">
                    <span className="w-1.5 h-1.5 rounded-full bg-emerald-400" />
                    Disponível
                  </span>
                </div>

                <div className="flex items-center justify-between text-[11px] px-1 text-slate-300">
                  <span className="flex items-center gap-2 font-medium">
                    <Sparkles className="w-3.5 h-3.5 text-amber-400" />
                    <span>HyperFrames</span>
                  </span>
                  <span className="text-[10px] font-medium text-emerald-400 flex items-center gap-1.5">
                    <span className="w-1.5 h-1.5 rounded-full bg-emerald-400" />
                    Disponível
                  </span>
                </div>

                <div className="flex items-center justify-between text-[11px] px-1 text-slate-300">
                  <span className="flex items-center gap-2 font-medium">
                    <Bot className="w-3.5 h-3.5 text-rose-400" />
                    <span>Claude Code</span>
                  </span>
                  <span className="text-[10px] font-medium text-emerald-400 flex items-center gap-1.5">
                    <span className="w-1.5 h-1.5 rounded-full bg-emerald-400 shadow-[0_0_6px_rgba(52,211,153,0.8)]" />
                    Conectado
                  </span>
                </div>
              </div>
            </div>

            {/* Storage Meter */}
            <div className="pt-2.5 border-t border-slate-800/60 space-y-1.5">
              <div className="flex items-center justify-between text-[11px] px-1 text-slate-300">
                <span className="flex items-center gap-1.5 font-medium">
                  <HardDrive className="w-3.5 h-3.5 text-slate-400" />
                  <span>Armazenamento (Projeto)</span>
                </span>
                <span className="text-[10px] text-slate-400 font-mono">
                  152 GB de 500 GB
                </span>
              </div>
              <div className="w-full h-1.5 bg-slate-800 rounded-full overflow-hidden">
                <div
                  className="h-full bg-gradient-to-r from-cyan-400 to-blue-500 rounded-full"
                  style={{ width: '30.4%' }}
                />
              </div>
            </div>
          </>
        ) : (
          <div className="flex flex-col items-center gap-2.5 py-1" title="Integrações Conectadas">
            <span className="w-2.5 h-2.5 rounded-full bg-emerald-400 shadow-[0_0_6px_rgba(52,211,153,0.8)]" />
            {renderDaVinciIcon()}
          </div>
        )}
      </div>
    </aside>
  );
};
