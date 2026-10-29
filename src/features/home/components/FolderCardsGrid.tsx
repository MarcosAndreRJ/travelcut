import React from 'react';
import { Folder, Image as ImageIcon, Sliders, CheckCircle } from 'lucide-react';
import { ProjectFolder } from '../types';

interface FolderCardsGridProps {
  folders: ProjectFolder[];
  onFolderClick?: (folder: ProjectFolder) => void;
}

export const FolderCardsGrid: React.FC<FolderCardsGridProps> = ({
  folders,
  onFolderClick,
}) => {
  const getThemeStyles = (theme: ProjectFolder['theme']) => {
    switch (theme) {
      case 'cyan':
        return {
          iconBox: 'bg-cyan-500/10 text-cyan-400 border-cyan-500/20',
          hoverBorder: 'hover:border-cyan-500/40',
          hoverGlow: 'hover:shadow-cyan-500/5',
        };
      case 'emerald':
        return {
          iconBox: 'bg-emerald-500/10 text-emerald-400 border-emerald-500/20',
          hoverBorder: 'hover:border-emerald-500/40',
          hoverGlow: 'hover:shadow-emerald-500/5',
        };
      case 'purple':
        return {
          iconBox: 'bg-purple-500/10 text-purple-400 border-purple-500/20',
          hoverBorder: 'hover:border-purple-500/40',
          hoverGlow: 'hover:shadow-purple-500/5',
        };
      case 'amber':
      default:
        return {
          iconBox: 'bg-amber-500/10 text-amber-400 border-amber-500/20',
          hoverBorder: 'hover:border-amber-500/40',
          hoverGlow: 'hover:shadow-amber-500/5',
        };
    }
  };

  const renderIcon = (type: ProjectFolder['iconType']) => {
    switch (type) {
      case 'folder':
        return <Folder size={20} className="shrink-0" />;
      case 'assets':
        return <ImageIcon size={20} className="shrink-0" />;
      case 'work':
        return <Sliders size={20} className="shrink-0" />;
      case 'delivery':
        return <CheckCircle size={20} className="shrink-0" />;
    }
  };

  return (
    <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
      {folders.map((folder) => {
        const theme = getThemeStyles(folder.theme);

        return (
          <div
            key={folder.id}
            onClick={() => onFolderClick?.(folder)}
            className={`group relative p-4 rounded-xl bg-[#0d131f]/80 hover:bg-[#121927] border border-slate-800/90 ${theme.hoverBorder} transition-all duration-200 cursor-pointer shadow-lg shadow-black/20 ${theme.hoverGlow} flex flex-col justify-between`}
          >
            <div>
              {/* Header: Icon & Title */}
              <div className="flex items-center gap-3 mb-2">
                <div
                  className={`p-2.5 rounded-xl border ${theme.iconBox} transition-transform group-hover:scale-105`}
                >
                  {renderIcon(folder.iconType)}
                </div>
                <div>
                  <h3 className="font-bold text-white text-base tracking-tight group-hover:text-cyan-200 transition-colors">
                    {folder.name}
                  </h3>
                  <p className="text-xs text-slate-400 font-medium">
                    {folder.subtitle}
                  </p>
                </div>
              </div>
            </div>

            {/* Bottom Detail Row */}
            <div className="pt-2 mt-2 border-t border-slate-800/50 flex items-center justify-between text-[11px] text-slate-500">
              <span className="truncate">{folder.detail}</span>
            </div>
          </div>
        );
      })}
    </div>
  );
};
