import React from 'react';
import {
  Home,
  FolderOpen,
  Scissors,
  Compass,
  Film,
  ShieldCheck,
  Settings,
  Play,
  Send,
  CheckCircle2,
  AlertCircle
} from 'lucide-react';
import { EditorCapabilities } from '../types/ipc';
import { ipc } from '../services/ipcBridge';
import { editorStatusBadge, isEditorConnected } from './editor/editorModel';
import { TONE_TEXT } from './editor/EditorPrimitives';
import { APP_VERSION } from '../version';
import { useIntegrationStatus } from './environment/useIntegrationStatus';
import { formatCheckedAt, toolAvailability, type ToolAvailability } from './environment/integrationStatus';

// Nunca exibir "ativo" sem confirmação do host. Mesma fonte e mesma regra da tela de Verificação do Ambiente.
const ToolStatusRow: React.FC<{ label: string; status: ToolAvailability; detail?: string; reason?: string | null }> = ({
  label,
  status,
  detail,
  reason
}) => (
  <div className="flex items-center justify-between text-slate-300" title={reason ?? undefined}>
    <span className="truncate">{label}</span>
    {status.kind === 'failed' ? (
      <span className="flex items-center gap-1 text-[10px] text-rose-400 font-medium">
        <AlertCircle className="w-3 h-3" /> Falha na consulta
      </span>
    ) : status.kind === 'unverified' ? (
      <span className="text-[10px] text-slate-500 font-medium">Não verificado</span>
    ) : status.kind === 'available' ? (
      <span className={`flex items-center gap-1 text-[10px] font-medium ${status.stale ? 'text-amber-300' : 'text-emerald-400'}`}>
        <CheckCircle2 className="w-3 h-3" /> {detail ?? 'Disponível'}{status.stale ? ' (desatualizado)' : ''}
      </span>
    ) : (
      <span className={`flex items-center gap-1 text-[10px] font-medium ${status.stale ? 'text-amber-300' : 'text-rose-400'}`}>
        <AlertCircle className="w-3 h-3" /> Não encontrado{status.stale ? ' (desatualizado)' : ''}
      </span>
    )}
  </div>
);

export type NavigationTab =
  | 'overview'
  | 'library'
  | 'transcription'
  | 'timeline'
  | 'review'
  | 'settings'
  | 'narrative'
  | 'audio'
  | 'animations'
  | 'export'
  | 'environment'
  | 'onboarding';

interface SidebarProps {
  currentTab: NavigationTab;
  onSelectTab: (tab: NavigationTab) => void;
  editorCaps: EditorCapabilities | null;
}

export const Sidebar: React.FC<SidebarProps> = ({
  currentTab,
  onSelectTab,
  editorCaps
}) => {
  const menuItems: {
    id: NavigationTab;
    label: string;
    screenNumber?: string;
    icon: React.ComponentType<{ className?: string }>;
  }[] = [
    { id: 'overview', label: 'Painel Inicial', screenNumber: '01', icon: Home },
    { id: 'library', label: 'Biblioteca & Transcrição', screenNumber: '02', icon: FolderOpen },
    { id: 'review', label: 'Revisão de Cortes & Efeitos', screenNumber: '03', icon: Scissors },
    { id: 'timeline', label: 'Planejamento da Timeline', screenNumber: '04', icon: Film },
    { id: 'narrative', label: 'Visão Geral da Montagem', screenNumber: '05', icon: Compass },
    { id: 'export', label: 'Enviar ao DaVinci', screenNumber: '06', icon: Send },
    { id: 'settings', label: 'Configurações & Motores', screenNumber: '07', icon: Settings },
    { id: 'environment', label: 'Verificação do Ambiente', icon: ShieldCheck },
  ];

  // Estado derivado do host: conectado só com DaVinci real; simulador é rotulado como tal.
  const isResolveConnected = isEditorConnected(editorCaps);
  const resolveBadge = editorStatusBadge(editorCaps);
  const integration = useIntegrationStatus();
  const { analysisEnv, analysisError, analysisStaleReason } = integration;
  const checkedAt = formatCheckedAt(integration.analysisCheckedAtMs);

  return (
    <aside className="w-64 bg-[#0a1120] border-r border-slate-800/80 flex flex-col justify-between select-none shrink-0 h-full font-sans">
      {/* Brand & Identity */}
      <div>
        <div
          onClick={() => onSelectTab('overview')}
          className="p-5 flex items-center gap-3 cursor-pointer hover:opacity-90 transition-opacity border-b border-slate-800/60"
        >
          <div className="w-10 h-10 rounded-xl bg-gradient-to-tr from-cyan-500 via-sky-600 to-blue-600 flex items-center justify-center shadow-lg shadow-cyan-500/20 border border-cyan-400/30 shrink-0">
            <Play className="w-5 h-5 text-white fill-white ml-0.5" />
          </div>
          <div>
            <div className="text-base font-bold tracking-tight text-white flex items-center gap-1 leading-none">
              TravelCut <span className="text-cyan-400">Studio</span>
            </div>
            <div className="text-[11px] text-slate-400 font-medium mt-1 leading-none">
              Edição inteligente de viagens
            </div>
          </div>
        </div>

        {/* Section title */}
        <div className="px-5 pt-4 pb-2 flex items-center justify-between">
          <span className="text-[10px] font-bold uppercase tracking-wider text-slate-400">
            Telas principais
          </span>
          {ipc.isMock && (
            <span className="px-1.5 py-0.5 rounded text-[9px] font-bold bg-amber-500/10 text-amber-300 border border-amber-500/30">
              DEMO
            </span>
          )}
        </div>

        {/* Navigation items */}
        <nav className="px-3 space-y-1">
          {menuItems.map((item) => {
            const Icon = item.icon;
            const isActive = currentTab === item.id;
            return (
              <button
                key={item.id}
                onClick={() => onSelectTab(item.id)}
                className={`w-full flex items-center gap-2.5 px-3 py-2 rounded-xl text-xs font-semibold transition-all duration-150 text-left ${
                  isActive
                    ? 'bg-gradient-to-r from-cyan-500/20 to-blue-600/15 text-cyan-300 border border-cyan-500/30 shadow-sm shadow-cyan-950'
                    : 'text-slate-300 hover:text-white hover:bg-slate-800/50 border border-transparent'
                }`}
              >
                <Icon
                  className={`w-4 h-4 shrink-0 ${
                    isActive ? 'text-cyan-400' : 'text-slate-400'
                  }`}
                />
                <span className="truncate flex-1">{item.label}</span>
                {item.screenNumber && (
                  <span className="text-[10px] font-mono text-slate-500 px-1 py-0.5 rounded bg-slate-900 border border-slate-800">
                    T{item.screenNumber}
                  </span>
                )}
                {item.id === 'environment' && !isResolveConnected && (
                  <span className="ml-auto w-2 h-2 rounded-full bg-amber-400 animate-pulse" />
                )}
              </button>
            );
          })}
        </nav>
      </div>

      {/* Connected Tools & Status */}
      <div className="p-4 border-t border-slate-800/70 bg-[#080d19]/80">
        <div className="flex items-center justify-between mb-2.5">
          <span className="text-[10px] font-bold uppercase tracking-wider text-slate-400">
            Ferramentas Conectadas
          </span>
          <button
            onClick={() => onSelectTab('environment')}
            className="text-[10px] text-cyan-400 hover:underline"
          >
            Checar
          </button>
        </div>

        <div className="space-y-2 text-[11px]">
          {/* DaVinci Resolve */}
          <div className="flex items-center justify-between text-slate-300">
            <span className="truncate">DaVinci Resolve Studio</span>
            <span className={`flex items-center gap-1 min-w-0 truncate text-[10px] font-medium ${TONE_TEXT[resolveBadge.tone]}`} title={resolveBadge.label}>
              {isResolveConnected ? <CheckCircle2 className="w-3 h-3" /> : <AlertCircle className="w-3 h-3" />}
              {resolveBadge.label}
            </span>
          </div>

          {/* Ferramentas de análise — estado real reportado pelo host (analysis.environment) */}
          <ToolStatusRow
            label={analysisEnv?.whisperModel ? `Whisper ${analysisEnv.whisperModel}` : 'Whisper'}
            status={toolAvailability(analysisEnv?.whisperAvailable, analysisError, analysisStaleReason)}
            detail={analysisEnv?.whisperAvailable && analysisEnv.cudaAvailable === true ? 'Disponível (GPU)' : undefined}
            reason={analysisError ?? analysisStaleReason}
          />
          <ToolStatusRow
            label="FFmpeg / FFprobe"
            status={toolAvailability(analysisEnv?.ffmpegAvailable, analysisError, analysisStaleReason)}
            reason={analysisError ?? analysisStaleReason}
          />
          {checkedAt && <p className="text-[10px] text-slate-500">Verificado às {checkedAt}</p>}
        </div>

        <div className="mt-3 pt-3 border-t border-slate-800/50 flex items-center justify-between text-[10px] text-slate-500 font-mono">
          <span>TravelCut v{APP_VERSION}</span>
          {ipc.isMock && <span className="text-amber-400 font-bold">MODO DEMO</span>}
        </div>
      </div>
    </aside>
  );
};
