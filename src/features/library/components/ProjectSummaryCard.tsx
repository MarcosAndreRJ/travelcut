import React from 'react';
import {
  Clock,
  Video,
  Scissors,
  Layers,
  Sparkles,
  ArrowRight,
  Calendar,
  CheckCircle2,
  ExternalLink
} from 'lucide-react';
import { ProjectSummaryData } from '../types';

interface ProjectSummaryCardProps {
  summary: ProjectSummaryData;
  onContinueEditing?: () => void;
  onCreateDaVinciProject?: () => void;
}

export const ProjectSummaryCard: React.FC<ProjectSummaryCardProps> = ({
  summary,
  onContinueEditing,
  onCreateDaVinciProject
}) => {
  return (
    <div className="bg-slate-900/90 border border-slate-800/90 rounded-xl p-4 shadow-sm flex flex-col space-y-3.5">
      {/* Header */}
      <div className="flex items-center justify-between border-b border-slate-800/70 pb-2.5">
        <h3 className="text-sm font-bold text-white tracking-tight flex items-center space-x-2">
          <span>Resumo do Projeto</span>
        </h3>
        <span className="text-[11px] text-slate-500 font-mono">
          {summary.lastUpdate}
        </span>
      </div>

      {/* Metrics List */}
      <div className="flex flex-col space-y-2 text-xs divide-y divide-slate-800/50">
        {/* Estimated duration */}
        <div className="flex items-center justify-between pt-1">
          <span className="text-slate-400 flex items-center space-x-1.5">
            <Clock className="w-3.5 h-3.5 text-blue-400" />
            <span>Duração estimada (edição)</span>
          </span>
          <span className="text-white font-semibold">{summary.estimatedDuration}</span>
        </div>

        {/* Selected videos */}
        <div className="flex items-center justify-between pt-2">
          <span className="text-slate-400 flex items-center space-x-1.5">
            <Video className="w-3.5 h-3.5 text-emerald-400" />
            <span>Vídeos selecionados</span>
          </span>
          <span className="text-white font-semibold">
            {summary.selectedClipsCount} de {summary.totalClipsCount}
          </span>
        </div>

        {/* Automatic cuts */}
        <div className="flex items-center justify-between pt-2">
          <span className="text-slate-400 flex items-center space-x-1.5">
            <Scissors className="w-3.5 h-3.5 text-purple-400" />
            <span>Cortes automáticos</span>
          </span>
          <span className="text-white font-semibold">{summary.autoCutsCount}</span>
        </div>

        {/* Cuts for review */}
        <div className="flex items-center justify-between pt-2">
          <span className="text-slate-400 flex items-center space-x-1.5">
            <Scissors className="w-3.5 h-3.5 text-amber-400" />
            <span>Cortes para revisão</span>
          </span>
          <span className="text-amber-300 font-bold bg-amber-500/10 px-1.5 py-0.5 rounded text-[11px] border border-amber-500/20">
            {summary.reviewCutsCount} pendentes
          </span>
        </div>

        {/* Suggested assets */}
        <div className="flex items-center justify-between pt-2">
          <span className="text-slate-400 flex items-center space-x-1.5">
            <Layers className="w-3.5 h-3.5 text-cyan-400" />
            <span>Assets sugeridos</span>
          </span>
          <span className="text-slate-300 text-[11px] font-medium">
            {summary.suggestedAssets.music} músicas, {summary.suggestedAssets.videos} vídeos,{' '}
            {summary.suggestedAssets.images} imagens
          </span>
        </div>

        {/* DaVinci Project status */}
        <div className="flex items-center justify-between pt-2">
          <span className="text-slate-400 flex items-center space-x-1.5">
            <Sparkles className="w-3.5 h-3.5 text-rose-400" />
            <span>Projeto DaVinci</span>
          </span>
          <span className="text-slate-400 text-[11px]">{summary.daVinciProjectStatus}</span>
        </div>
      </div>

      {/* Primary Action CTA Button */}
      <div className="pt-2">
        <button
          onClick={onContinueEditing}
          className="w-full bg-blue-600 hover:bg-blue-500 text-white font-bold py-2.5 px-4 rounded-xl shadow-lg shadow-blue-600/25 transition-all flex items-center justify-center space-x-2 text-sm group"
        >
          <span>Continuar Edição</span>
          <ArrowRight className="w-4 h-4 group-hover:translate-x-1 transition-transform" />
        </button>
      </div>
    </div>
  );
};
