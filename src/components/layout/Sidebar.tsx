import React from 'react';
import {
  Home,
  FolderOpen,
  Sparkles,
  Scissors,
  CheckSquare,
  Share2,
  Settings,
  Film,
  ChevronLeft,
  ChevronRight,
  Cpu,
  MonitorPlay
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
    badge: 'IA',
    badgeVariant: 'primary',
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
    badge: 'Cortes',
    badgeVariant: 'warning',
    description: 'Revisão e refinamento'
  },
  {
    id: 'export',
    label: 'Exportação para DaVinci',
    icon: Share2,
    badge: 'Resolve',
    badgeVariant: 'success',
    description: 'Exportação XML/EDL para DaVinci'
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
  return (
    <aside
      aria-label="Navegação Principal"
      className={`h-full bg-studio-sidebar border-r border-studio-border flex flex-col justify-between shrink-0 transition-all duration-200 select-none z-30 ${
        isCollapsed ? 'w-16' : 'w-64'
      }`}
    >
      {/* Top: Logo & Brand */}
      <div className="flex flex-col">
        <div className="h-14 border-b border-studio-border px-3.5 flex items-center justify-between">
          <div className="flex items-center space-x-2.5 overflow-hidden">
            <div className="w-8 h-8 rounded-xl bg-gradient-to-tr from-blue-600 via-indigo-600 to-cyan-400 flex items-center justify-center shadow-glow-blue shrink-0">
              <Film className="w-4 h-4 text-white" />
            </div>

            {!isCollapsed && (
              <div className="flex flex-col min-w-0">
                <div className="flex items-center gap-1.5">
                  <span className="font-extrabold text-sm tracking-tight text-white truncate">
                    TravelCut<span className="text-blue-400">Studio</span>
                  </span>
                </div>
                <span className="text-[10px] text-studio-muted font-mono tracking-wider uppercase font-semibold">
                  Pré-edição Desktop
                </span>
              </div>
            )}
          </div>

          {onToggleCollapse && (
            <button
              onClick={onToggleCollapse}
              title={isCollapsed ? 'Expandir barra lateral' : 'Recolher barra lateral'}
              className="p-1 rounded-lg text-studio-subtle hover:text-studio-text hover:bg-studio-card transition-colors shrink-0"
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
        <nav className="p-2 space-y-1 overflow-y-auto max-h-[calc(100vh-220px)]">
          {NAVIGATION_ITEMS.map((item) => {
            const Icon = item.icon;
            const isActive = activeTab === item.id;

            return (
              <button
                key={item.id}
                onClick={() => onTabChange(item.id)}
                title={isCollapsed ? `${item.label} - ${item.description || ''}` : undefined}
                className={`w-full flex items-center gap-3 px-3 py-2 rounded-xl text-xs font-semibold transition-all duration-150 group relative cursor-pointer ${
                  isActive
                    ? 'bg-blue-600/15 text-blue-300 border border-blue-500/30 shadow-inner-light'
                    : 'text-studio-muted hover:text-studio-text hover:bg-studio-card/80 border border-transparent'
                }`}
                aria-current={isActive ? 'page' : undefined}
              >
                {/* Active Indicator Bar */}
                {isActive && (
                  <span className="absolute left-0 top-2 bottom-2 w-1 bg-studio-primary rounded-r-full" />
                )}

                <Icon
                  className={`w-4 h-4 shrink-0 transition-colors ${
                    isActive ? 'text-blue-400' : 'text-studio-subtle group-hover:text-studio-text'
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
            );
          })}
        </nav>
      </div>

      {/* Bottom: System Status & Hardware Info */}
      <div className="p-3 border-t border-studio-border/80 bg-studio-panel/40 flex flex-col gap-2">
        {!isCollapsed ? (
          <>
            <div className="flex items-center justify-between text-[11px] px-1 text-studio-muted">
              <span className="flex items-center gap-1.5 font-medium">
                <Cpu className="w-3.5 h-3.5 text-blue-400" />
                Motor IA Local
              </span>
              <span className="text-[10px] font-mono text-emerald-400 flex items-center gap-1">
                <span className="w-1.5 h-1.5 rounded-full bg-emerald-400 led-indicator" />
                GPU Ativa
              </span>
            </div>

            <div className="flex items-center justify-between text-[11px] px-1 text-studio-muted">
              <span className="flex items-center gap-1.5 font-medium">
                <MonitorPlay className="w-3.5 h-3.5 text-studio-subtle" />
                DaVinci Resolve
              </span>
              <span className="text-[10px] font-mono text-studio-subtle">
                Pronto p/ Export
              </span>
            </div>

            <div className="text-[10px] text-studio-subtle/80 text-center font-mono pt-1 border-t border-studio-border/40">
              TravelCut v1.0 • Tauri 2 Ready
            </div>
          </>
        ) : (
          <div className="flex flex-col items-center gap-2 py-1">
            <span
              title="Motor IA: Ativo (GPU)"
              className="w-2.5 h-2.5 rounded-full bg-emerald-400 led-indicator"
            />
            <Cpu className="w-4 h-4 text-studio-subtle" title="Motor IA Local Ativo" />
          </div>
        )}
      </div>
    </aside>
  );
};
