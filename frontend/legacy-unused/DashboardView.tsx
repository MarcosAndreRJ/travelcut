import React, { useState } from 'react';
import {
  FolderCheck,
  FolderX,
  FileVideo,
  Clock,
  Scissors,
  CheckCircle2,
  Sparkles,
  ArrowRight,
  TrendingDown,
  Layers,
  Search
} from 'lucide-react';
import { ProjectSummary } from '../types/ipc';

interface DashboardViewProps {
  project: ProjectSummary | null;
  onOpenProject: (path: string) => Promise<void>;
  onNavigateToReview: () => void;
  onNavigateToTimeline: () => void;
  onNavigateToResolve: () => void;
}

export const DashboardView: React.FC<DashboardViewProps> = ({
  project,
  onOpenProject,
  onNavigateToReview,
  onNavigateToTimeline,
  onNavigateToResolve
}) => {
  const [inputPath, setInputPath] = useState(
    project?.rootPath || 'K:\\Trabalho\\Projetos\\TravelCutStudio\\demo-project'
  );
  const [isOpening, setIsOpening] = useState(false);

  const handleOpen = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!inputPath.trim()) return;
    setIsOpening(true);
    try {
      await onOpenProject(inputPath.trim());
    } finally {
      setIsOpening(false);
    }
  };

  const formatDuration = (totalSec: number) => {
    const hours = Math.floor(totalSec / 3600);
    const minutes = Math.floor((totalSec % 3600) / 60);
    const seconds = Math.floor(totalSec % 60);
    if (hours > 0) {
      return `${hours}h ${minutes}m ${seconds}s`;
    }
    return `${minutes}m ${seconds}s`;
  };

  // Sem corte real ainda (estimatedCutDurationSeconds null) não há desbaste a mostrar.
  const estimatedCut = project?.estimatedCutDurationSeconds ?? null;
  const savedTimeSec = project && estimatedCut !== null
    ? Math.max(0, project.totalOriginalDurationSeconds - estimatedCut)
    : 0;
  const cutPercentage = project && estimatedCut !== null && project.totalOriginalDurationSeconds > 0
    ? Math.round((savedTimeSec / project.totalOriginalDurationSeconds) * 100)
    : 0;

  return (
    <div className="flex-1 overflow-y-auto p-8 space-y-8 max-w-7xl mx-auto w-full">
      {/* Top Banner / Project Folder Selector */}
      <section className="bg-studio-900 border border-studio-800 rounded-xl p-6 shadow-sm">
        <div className="flex flex-col md:flex-row md:items-center justify-between gap-4">
          <div>
            <h1 className="text-xl font-bold text-slate-100 flex items-center space-x-2">
              <span>Projeto de Viagem & Desbaste Inteligente</span>
            </h1>
            <p className="text-sm text-slate-400 mt-1">
              Organização padronizada em 4 pastas e decupagem assistida com preservação de intenção narrativa.
            </p>
          </div>

          <form onSubmit={handleOpen} className="flex items-center space-x-2 w-full md:w-auto">
            <div className="relative flex-1 md:w-96">
              <input
                type="text"
                value={inputPath}
                onChange={(e) => setInputPath(e.target.value)}
                placeholder="Caminho raiz do projeto (ex: D:\Videos\ViagemChile)"
                className="w-full bg-studio-950 border border-studio-700/80 rounded-lg px-3.5 py-2 text-xs text-slate-200 placeholder-slate-500 focus:outline-none focus:border-amber-500 font-mono"
              />
            </div>
            <button
              type="submit"
              disabled={isOpening}
              className="bg-amber-600 hover:bg-amber-500 text-white text-xs font-medium px-4 py-2 rounded-lg transition-colors flex items-center space-x-1.5 shrink-0 disabled:opacity-50"
            >
              <Search className="w-3.5 h-3.5" />
              <span>{isOpening ? 'Carregando...' : 'Abrir Projeto'}</span>
            </button>
          </form>
        </div>

        {/* 4 Mandatory Folders Grid */}
        <div className="mt-6 pt-6 border-t border-studio-800">
          <div className="text-xs font-semibold text-slate-400 uppercase tracking-wider mb-3">
            Estrutura Canônica Obrigatória (Regra de Ouro)
          </div>
          <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-3">
            {[
              { id: '1-Bruto', label: '1-Bruto', desc: 'Mídias brutas da câmera e áudio' },
              { id: '2-Assets', label: '2-Assets', desc: 'Trilhas, SFX, logos e mapas' },
              { id: '3-Trabalho', label: '3-Trabalho', desc: 'Projetos Resolve, logs e planos' },
              { id: '4-Final', label: '4-Final', desc: 'Masters e exports aprovados' },
            ].map((folder) => {
              const isMissing = project && !project.fourFoldersValid && project.missingFolder === folder.id;
              return (
                <div
                  key={folder.id}
                  className={`p-3.5 rounded-lg border flex items-start space-x-3 transition-colors ${
                    isMissing
                      ? 'bg-rose-950/20 border-rose-800/60'
                      : 'bg-studio-950/40 border-studio-800/80 hover:border-studio-700'
                  }`}
                >
                  <div className="mt-0.5">
                    {isMissing ? (
                      <FolderX className="w-4 h-4 text-rose-400" />
                    ) : (
                      <FolderCheck className="w-4 h-4 text-emerald-400" />
                    )}
                  </div>
                  <div>
                    <div className="text-xs font-mono font-semibold text-slate-200">
                      {folder.label}
                    </div>
                    <div className="text-[11px] text-slate-400 mt-0.5">
                      {folder.desc}
                    </div>
                  </div>
                </div>
              );
            })}
          </div>
        </div>
      </section>

      {/* Metrics Row */}
      {project && (
        <section className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
          <div className="bg-studio-900 border border-studio-800 rounded-xl p-5">
            <div className="flex items-center justify-between text-slate-400 mb-2">
              <span className="text-xs font-medium">Arquivos Brutos</span>
              <FileVideo className="w-4 h-4 text-blue-400" />
            </div>
            <div className="text-2xl font-bold text-slate-100 font-mono">
              {project.mediaCount}
            </div>
            <div className="text-[11px] text-slate-400 mt-1">
              Vídeos em formato 4K/60 e 4K/30
            </div>
          </div>

          <div className="bg-studio-900 border border-studio-800 rounded-xl p-5">
            <div className="flex items-center justify-between text-slate-400 mb-2">
              <span className="text-xs font-medium">Duração Original</span>
              <Clock className="w-4 h-4 text-purple-400" />
            </div>
            <div className="text-2xl font-bold text-slate-100 font-mono">
              {formatDuration(project.totalOriginalDurationSeconds)}
            </div>
            <div className="text-[11px] text-slate-400 mt-1">
              Material total registrado
            </div>
          </div>

          <div className="bg-studio-900 border border-studio-800 rounded-xl p-5">
            <div className="flex items-center justify-between text-slate-400 mb-2">
              <span className="text-xs font-medium">Desbaste Estimado</span>
              <TrendingDown className="w-4 h-4 text-emerald-400" />
            </div>
            <div className="text-2xl font-bold text-emerald-400 font-mono">
              {estimatedCut !== null ? formatDuration(estimatedCut) : '—'}
            </div>
            <div className="text-[11px] text-emerald-500/80 mt-1 font-medium">
              {estimatedCut !== null ? `-${cutPercentage}% de material desnecessário` : 'Disponível após aprovar cortes'}
            </div>
          </div>

          <div className="bg-studio-900 border border-studio-800 rounded-xl p-5">
            <div className="flex items-center justify-between text-slate-400 mb-2">
              <span className="text-xs font-medium">Cortes Aprovados</span>
              <Scissors className="w-4 h-4 text-amber-400" />
            </div>
            <div className="text-2xl font-bold text-amber-400 font-mono">
              {project.approvedCutsCount} / {project.suggestionsCount}
            </div>
            <div className="text-[11px] text-slate-400 mt-1">
              Decisões editoriais validadas
            </div>
          </div>
        </section>
      )}

      {/* Editorial Flow & Next Steps */}
      <section className="bg-studio-900 border border-studio-800 rounded-xl p-6">
        <h2 className="text-base font-semibold text-slate-100 flex items-center space-x-2 mb-4">
          <Sparkles className="w-4 h-4 text-amber-400" />
          <span>Fluxo Editorial TravelCut</span>
        </h2>

        <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
          <div className="bg-studio-950/60 border border-studio-800/80 rounded-lg p-5 flex flex-col justify-between">
            <div>
              <div className="flex items-center space-x-2 text-amber-400 text-xs font-semibold uppercase tracking-wider mb-2">
                <span>Fase 1: Revisão de Cortes</span>
              </div>
              <h3 className="text-sm font-semibold text-slate-200">
                Análise de Silêncios e Hesitações
              </h3>
              <p className="text-xs text-slate-400 mt-2 leading-relaxed">
                Revise as sugestões automáticas geradas pelo VAD e pela transcrição. Aprove ou rejeite com 1 clique mantendo controle editorial total.
              </p>
            </div>
            <button
              onClick={onNavigateToReview}
              className="mt-5 w-full bg-studio-800 hover:bg-studio-700 text-slate-200 text-xs font-medium py-2 rounded-md transition-colors flex items-center justify-center space-x-1.5 border border-studio-700"
            >
              <span>Abrir Painel de Revisão</span>
              <ArrowRight className="w-3.5 h-3.5" />
            </button>
          </div>

          <div className="bg-studio-950/60 border border-studio-800/80 rounded-lg p-5 flex flex-col justify-between">
            <div>
              <div className="flex items-center space-x-2 text-blue-400 text-xs font-semibold uppercase tracking-wider mb-2">
                <span>Fase 2: Montagem</span>
              </div>
              <h3 className="text-sm font-semibold text-slate-200">
                Visualização e Trilha de Edição
              </h3>
              <p className="text-xs text-slate-400 mt-2 leading-relaxed">
                Inspecione a sequência montada com clipes mantidos, trechos acelerados (2x/4x) e marcadores de salto sutil de escala (1.05x).
              </p>
            </div>
            <button
              onClick={onNavigateToTimeline}
              className="mt-5 w-full bg-studio-800 hover:bg-studio-700 text-slate-200 text-xs font-medium py-2 rounded-md transition-colors flex items-center justify-center space-x-1.5 border border-studio-700"
            >
              <span>Inspecionar Timeline</span>
              <ArrowRight className="w-3.5 h-3.5" />
            </button>
          </div>

          <div className="bg-studio-950/60 border border-studio-800/80 rounded-lg p-5 flex flex-col justify-between">
            <div>
              <div className="flex items-center space-x-2 text-emerald-400 text-xs font-semibold uppercase tracking-wider mb-2">
                <span>Fase 3: DaVinci Resolve</span>
              </div>
              <h3 className="text-sm font-semibold text-slate-200">
                Aplicação no DaVinci Resolve
              </h3>
              <p className="text-xs text-slate-400 mt-2 leading-relaxed">
                Envie o EditPlan diretamente para o DaVinci Resolve Studio. A timeline é populada automaticamente com cortes, transições e velocidades.
              </p>
            </div>
            <button
              onClick={onNavigateToResolve}
              className="mt-5 w-full bg-amber-600 hover:bg-amber-500 text-white text-xs font-medium py-2 rounded-md transition-colors flex items-center justify-center space-x-1.5"
            >
              <span>Sincronizar com DaVinci</span>
              <ArrowRight className="w-3.5 h-3.5" />
            </button>
          </div>
        </div>
      </section>
    </div>
  );
};
