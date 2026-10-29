import React, { useState } from 'react';
import {
  Folder,
  FolderOpen,
  Pencil,
  Video,
  Clock,
  Image as ImageIcon,
  Calendar,
  MoreHorizontal,
  Check,
  X,
} from 'lucide-react';
import { ProjectInfo } from '../types';

interface ProjectHeroCardProps {
  project: ProjectInfo;
  onOpenFolder?: () => void;
  onEditTitle?: (newTitle: string) => void;
  onMoreOptions?: () => void;
}

export const ProjectHeroCard: React.FC<ProjectHeroCardProps> = ({
  project,
  onOpenFolder,
  onEditTitle,
  onMoreOptions,
}) => {
  const [isEditing, setIsEditing] = useState(false);
  const [editedTitle, setEditedTitle] = useState(project.name);

  const handleSaveTitle = () => {
    if (editedTitle.trim()) {
      onEditTitle?.(editedTitle.trim());
      setIsEditing(false);
    }
  };

  const handleCancelTitle = () => {
    setEditedTitle(project.name);
    setIsEditing(false);
  };

  return (
    <div className="relative w-full rounded-2xl overflow-hidden border border-slate-800/90 bg-[#090d16] shadow-xl shadow-black/40">
      {/* Background Image with Gradient Overlay */}
      <div className="absolute inset-0">
        <img
          src={project.coverImageUrl}
          alt={project.name}
          className="w-full h-full object-cover object-center opacity-40 select-none pointer-events-none"
        />
        {/* Multi-stage gradient to guarantee text contrast */}
        <div className="absolute inset-0 bg-gradient-to-r from-[#070b13] via-[#090e1a]/90 to-transparent" />
        <div className="absolute inset-0 bg-gradient-to-t from-[#070b13]/80 via-transparent to-transparent" />
      </div>

      {/* Content Container */}
      <div className="relative z-10 p-6 md:p-7 flex flex-col md:flex-row md:items-start justify-between gap-6">
        {/* Left Side: Information */}
        <div className="flex-1 space-y-3 max-w-2xl">
          {/* Project Tag */}
          <div className="flex items-center gap-2">
            <span className="text-[11px] font-bold tracking-widest text-cyan-400 uppercase">
              {project.tag}
            </span>
          </div>

          {/* Title Row */}
          <div className="flex items-center gap-3">
            {isEditing ? (
              <div className="flex items-center gap-2">
                <input
                  type="text"
                  value={editedTitle}
                  onChange={(e) => setEditedTitle(e.target.value)}
                  onKeyDown={(e) => {
                    if (e.key === 'Enter') handleSaveTitle();
                    if (e.key === 'Escape') handleCancelTitle();
                  }}
                  autoFocus
                  className="bg-slate-900/90 border border-cyan-500/60 rounded-lg px-3 py-1 text-white text-2xl font-bold focus:outline-none focus:ring-2 focus:ring-cyan-400"
                />
                <button
                  type="button"
                  onClick={handleSaveTitle}
                  title="Salvar título"
                  className="p-1.5 bg-emerald-500/20 text-emerald-400 hover:bg-emerald-500/30 rounded-lg border border-emerald-500/40 transition-colors"
                >
                  <Check size={18} />
                </button>
                <button
                  type="button"
                  onClick={handleCancelTitle}
                  title="Cancelar"
                  className="p-1.5 bg-slate-800 text-slate-300 hover:bg-slate-700 rounded-lg border border-slate-700 transition-colors"
                >
                  <X size={18} />
                </button>
              </div>
            ) : (
              <div className="flex items-center gap-2 group">
                <h1 className="text-2xl md:text-3xl font-extrabold text-white tracking-tight">
                  {project.name}
                </h1>
                <button
                  type="button"
                  onClick={() => setIsEditing(true)}
                  title="Editar nome do projeto"
                  className="p-1.5 text-slate-400 hover:text-cyan-300 hover:bg-slate-800/60 rounded-lg transition-colors"
                >
                  <Pencil size={15} />
                </button>
              </div>
            )}
          </div>

          {/* Folder Path */}
          <div className="flex items-center gap-2 text-slate-400 text-xs md:text-sm font-mono">
            <Folder size={14} className="text-cyan-400/80 shrink-0" />
            <span className="truncate">{project.folderPath}</span>
          </div>

          {/* Description */}
          <p className="text-xs md:text-sm text-slate-300 leading-relaxed">
            {project.description}
          </p>

          {/* Metadata Badges */}
          <div className="pt-2 flex flex-wrap items-center gap-2.5">
            <div className="flex items-center gap-1.5 px-3 py-1 bg-slate-900/70 backdrop-blur-md rounded-full border border-slate-700/50 text-slate-300 text-xs font-medium">
              <Video size={13} className="text-cyan-400" />
              <span>{project.videoCount} vídeos</span>
            </div>

            <div className="flex items-center gap-1.5 px-3 py-1 bg-slate-900/70 backdrop-blur-md rounded-full border border-slate-700/50 text-slate-300 text-xs font-medium">
              <Clock size={13} className="text-cyan-400" />
              <span>{project.totalDuration}</span>
            </div>

            <div className="flex items-center gap-1.5 px-3 py-1 bg-slate-900/70 backdrop-blur-md rounded-full border border-slate-700/50 text-slate-300 text-xs font-medium">
              <ImageIcon size={13} className="text-cyan-400" />
              <span>{project.assetsCount} assets</span>
            </div>

            <div className="flex items-center gap-1.5 px-3 py-1 bg-slate-900/70 backdrop-blur-md rounded-full border border-slate-700/50 text-slate-300 text-xs font-medium">
              <Calendar size={13} className="text-cyan-400" />
              <span>{project.lastModified}</span>
            </div>
          </div>
        </div>

        {/* Right Side: Actions */}
        <div className="flex items-center gap-2.5 shrink-0">
          <button
            type="button"
            onClick={onOpenFolder}
            className="flex items-center gap-2 px-4 py-2.5 bg-slate-900/80 hover:bg-slate-800 text-slate-200 hover:text-white rounded-xl border border-slate-700/70 text-xs md:text-sm font-medium backdrop-blur transition-all shadow-md hover:shadow-cyan-500/5 active:scale-[0.98]"
          >
            <FolderOpen size={16} className="text-cyan-400" />
            <span>Abrir pasta do projeto</span>
          </button>

          <button
            type="button"
            onClick={onMoreOptions}
            title="Mais opções do projeto"
            className="p-2.5 bg-slate-900/80 hover:bg-slate-800 text-slate-300 hover:text-white rounded-xl border border-slate-700/70 backdrop-blur transition-all shadow-md active:scale-[0.98]"
          >
            <MoreHorizontal size={18} />
          </button>
        </div>
      </div>
    </div>
  );
};
