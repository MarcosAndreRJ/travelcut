import React from 'react';
import { Moon, Folder, RefreshCw } from 'lucide-react';
import { ProjectSummary } from '../types/ipc';
import { ipc } from '../services/ipcBridge';

interface TopHeaderProps {
  project: ProjectSummary | null;
  onRefresh?: () => void;
  onOpenProjectDialog?: () => void;
}

export const TopHeader: React.FC<TopHeaderProps> = ({ project, onRefresh, onOpenProjectDialog }) => {
  return (
    <header className="h-14 bg-[#0a1120] border-b border-slate-800/80 px-6 flex items-center justify-between select-none font-sans shrink-0">
      {/* Projeto atual / breadcrumb */}
      <div className="flex items-center gap-3">
        <div className="flex items-center gap-2 text-xs font-semibold text-slate-300">
          <Folder className="w-4 h-4 text-cyan-400" />
          {project ? (
            <span className="text-white font-bold">{project.name}</span>
          ) : (
            <span className="text-slate-400">Nenhum projeto ativo</span>
          )}
        </div>

        {project && (
          <div className="hidden md:flex items-center gap-2 text-[11px] text-slate-400 border-l border-slate-800 pl-3">
            <span className="truncate max-w-xs">{project.rootPath}</span>
            <span className="bg-slate-800 text-slate-300 px-2 py-0.5 rounded-full text-[10px] font-medium">
              {project.mediaCount} mídias
            </span>
          </div>
        )}

        <button
          onClick={onOpenProjectDialog}
          className="text-[11px] text-cyan-400 hover:text-cyan-300 hover:underline font-medium ml-2"
        >
          {project ? 'Trocar projeto' : 'Abrir projeto'}
        </button>

        {/* Selo de demonstração só quando os dados vêm do mock do navegador */}
        {ipc.isMock && (
          <span className="hidden sm:inline-flex items-center px-2 py-0.5 rounded-full text-[10px] font-bold tracking-wider uppercase bg-amber-500/10 text-amber-300 border border-amber-500/30">
            DEMONSTRAÇÃO
          </span>
        )}
      </div>

      <div className="flex items-center gap-3">
        {onRefresh && (
          <button
            onClick={onRefresh}
            title="Atualizar status e dados"
            className="p-2 rounded-xl text-slate-400 hover:text-white hover:bg-slate-800/60 transition-colors"
          >
            <RefreshCw className="w-4 h-4" />
          </button>
        )}

        {/* Tema: sem efeito nesta versão; desabilitado para não parecer funcional. */}
        <button
          type="button"
          disabled
          title="Alternar tema claro/escuro — em breve"
          aria-label="Alternar tema claro/escuro — em breve"
          className="p-2 rounded-xl text-slate-600 cursor-not-allowed"
        >
          <Moon className="w-4 h-4" />
        </button>

        <div className="flex items-center gap-2 pl-2 border-l border-slate-800">
          <div className="w-8 h-8 rounded-full bg-gradient-to-tr from-cyan-500 to-blue-600 flex items-center justify-center text-white font-bold text-xs shadow-md shadow-cyan-950">
            TC
          </div>
        </div>
      </div>
    </header>
  );
};
