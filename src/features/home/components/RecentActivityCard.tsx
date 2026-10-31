import React from 'react';
import { FileText, Database, Zap, FolderPlus, ChevronRight } from 'lucide-react';
import { RecentActivityItem } from '../types';

interface RecentActivityCardProps {
  activities: RecentActivityItem[];
  onViewAll?: () => void;
  onActivityClick?: (activity: RecentActivityItem) => void;
}

export const RecentActivityCard: React.FC<RecentActivityCardProps> = ({
  activities,
  onViewAll,
  onActivityClick,
}) => {
  const renderIcon = (type: RecentActivityItem['type']) => {
    switch (type) {
      case 'transcription':
        return (
          <div className="p-2 rounded-xl bg-emerald-500/10 border border-emerald-500/20 text-emerald-400 shrink-0">
            <FileText size={16} />
          </div>
        );
      case 'inventory':
        return (
          <div className="p-2 rounded-xl bg-emerald-500/10 border border-emerald-500/20 text-emerald-400 shrink-0">
            <Database size={16} />
          </div>
        );
      case 'connection':
        return (
          <div className="p-2 rounded-xl bg-cyan-500/10 border border-cyan-500/20 text-cyan-400 shrink-0">
            <Zap size={16} />
          </div>
        );
      case 'project':
      default:
        return (
          <div className="p-2 rounded-xl bg-blue-500/10 border border-blue-500/20 text-blue-400 shrink-0">
            <FolderPlus size={16} />
          </div>
        );
    }
  };

  return (
    <div className="w-full rounded-2xl bg-[#0d131f]/90 border border-slate-800/90 p-5 shadow-xl shadow-black/25">
      {/* Header */}
      <div className="flex items-center justify-between mb-4">
        <h3 className="text-base font-bold text-white tracking-tight">
          Atividade recente
        </h3>
        <button
          type="button"
          onClick={onViewAll}
          className="text-xs font-semibold text-cyan-400 hover:text-cyan-300 flex items-center gap-0.5 transition-colors group"
        >
          <span>Ver todas</span>
          <ChevronRight size={14} className="group-hover:translate-x-0.5 transition-transform" />
        </button>
      </div>

      {/* Activity List or Empty State */}
      {activities.length === 0 ? (
        <div className="py-6 px-3 text-center rounded-xl bg-slate-900/40 border border-dashed border-slate-800 text-slate-400 text-xs">
          Nenhuma atividade registrada no momento. As ações de escaneamento, transcrição e cortes aparecerão aqui.
        </div>
      ) : (
        <div className="divide-y divide-slate-800/60">
          {activities.map((activity) => (
            <div
              key={activity.id}
              onClick={() => onActivityClick?.(activity)}
              className="py-2.5 first:pt-0 last:pb-0 flex items-center justify-between gap-3 group cursor-pointer hover:bg-slate-800/30 px-1 rounded-lg transition-colors"
            >
              <div className="flex items-center gap-3 min-w-0">
                {renderIcon(activity.type)}
                <div className="min-w-0">
                  <div className="text-xs md:text-sm font-semibold text-slate-200 group-hover:text-white transition-colors truncate">
                    {activity.title}
                  </div>
                  <div className="text-[11px] text-slate-400 truncate mt-0.5">
                    {activity.description}
                  </div>
                </div>
              </div>

              <span className="text-[11px] text-slate-500 shrink-0 font-medium">
                {activity.timestamp}
              </span>
            </div>
          ))}
        </div>
      )}
    </div>
  );
};
