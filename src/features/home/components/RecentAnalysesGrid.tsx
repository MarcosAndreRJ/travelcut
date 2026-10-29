import React from 'react';
import { Check, ChevronRight } from 'lucide-react';
import { MediaAnalysisItem } from '../types';

interface RecentAnalysesGridProps {
  analyses: MediaAnalysisItem[];
  onViewAll?: () => void;
  onItemClick?: (item: MediaAnalysisItem) => void;
}

export const RecentAnalysesGrid: React.FC<RecentAnalysesGridProps> = ({
  analyses,
  onViewAll,
  onItemClick,
}) => {
  const getBadgeStyle = (theme: MediaAnalysisItem['categoryTheme']) => {
    switch (theme) {
      case 'emerald':
        return 'bg-emerald-950/60 text-emerald-300 border-emerald-500/30';
      case 'purple':
        return 'bg-purple-950/60 text-purple-300 border-purple-500/30';
      case 'amber':
        return 'bg-amber-950/60 text-amber-300 border-amber-500/30';
      case 'blue':
      default:
        return 'bg-blue-950/60 text-blue-300 border-blue-500/30';
    }
  };

  return (
    <div className="w-full rounded-2xl bg-[#0d131f]/90 border border-slate-800/90 p-6 shadow-xl shadow-black/25">
      {/* Header */}
      <div className="flex items-center justify-between mb-5">
        <h2 className="text-lg font-bold text-white tracking-tight">
          Últimas análises
        </h2>
        <button
          type="button"
          onClick={onViewAll}
          className="text-xs md:text-sm font-semibold text-cyan-400 hover:text-cyan-300 flex items-center gap-0.5 transition-colors group"
        >
          <span>Ver todas</span>
          <ChevronRight size={15} className="group-hover:translate-x-0.5 transition-transform" />
        </button>
      </div>

      {/* Grid of 4 Analysis Cards */}
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
        {analyses.map((item) => (
          <div
            key={item.id}
            onClick={() => onItemClick?.(item)}
            className="group cursor-pointer p-2.5 rounded-xl bg-slate-900/60 hover:bg-slate-800/80 border border-slate-800/80 hover:border-slate-700 transition-all duration-200 flex flex-col justify-between"
          >
            {/* Thumbnail */}
            <div className="relative aspect-video w-full rounded-lg overflow-hidden bg-slate-800">
              <img
                src={item.thumbnailUrl}
                alt={item.filename}
                className="w-full h-full object-cover group-hover:scale-105 transition-transform duration-300"
              />
              <div className="absolute inset-0 bg-gradient-to-t from-black/60 via-transparent to-transparent pointer-events-none" />

              {/* Status Badge in corner */}
              <div className="absolute bottom-2 left-2 w-5 h-5 rounded-full bg-emerald-500 text-slate-950 flex items-center justify-center shadow-md">
                <Check size={13} strokeWidth={3} />
              </div>
            </div>

            {/* Content Details */}
            <div className="pt-2.5 pb-1">
              <h4 className="font-semibold text-sm text-white truncate group-hover:text-cyan-300 transition-colors">
                {item.filename}
              </h4>
              <p className="text-xs text-slate-400 font-medium mt-0.5">
                {item.duration} • {item.resolution} • {item.device}
              </p>

              {/* Category Pill Tag */}
              <div className="mt-2.5">
                <span
                  className={`inline-block px-2.5 py-0.5 text-[11px] font-medium rounded-full border ${getBadgeStyle(
                    item.categoryTheme
                  )}`}
                >
                  {item.categoryLabel}
                </span>
              </div>
            </div>
          </div>
        ))}
      </div>
    </div>
  );
};
