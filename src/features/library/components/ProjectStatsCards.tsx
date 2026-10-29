import React from 'react';
import { Video, Image, MessageSquare, Scissors } from 'lucide-react';

interface StatsCardItem {
  id: string;
  icon: React.ComponentType<{ className?: string }>;
  iconBg: string;
  iconColor: string;
  borderColor: string;
  value: string;
  label: string;
  subtitle: string;
}

const STATS_DATA: StatsCardItem[] = [
  {
    id: 'raw-videos',
    icon: Video,
    iconBg: 'bg-blue-500/10',
    iconColor: 'text-blue-400',
    borderColor: 'border-blue-500/20',
    value: '48',
    label: 'Vídeos brutos',
    subtitle: '2h 34m de gravações',
  },
  {
    id: 'assets-available',
    icon: Image,
    iconBg: 'bg-emerald-500/10',
    iconColor: 'text-emerald-400',
    borderColor: 'border-emerald-500/20',
    value: '126',
    label: 'Assets disponíveis',
    subtitle: 'Vídeos, músicas, imagens',
  },
  {
    id: 'transcription-generated',
    icon: MessageSquare,
    iconBg: 'bg-cyan-500/10',
    iconColor: 'text-cyan-400',
    borderColor: 'border-cyan-500/20',
    value: '42min',
    label: 'Transcrição gerada',
    subtitle: 'Word timestamps (large-v3)',
  },
  {
    id: 'suggested-cuts',
    icon: Scissors,
    iconBg: 'bg-purple-500/10',
    iconColor: 'text-purple-400',
    borderColor: 'border-purple-500/20',
    value: '87',
    label: 'Cortes sugeridos',
    subtitle: '12 para revisão',
  },
];

export const ProjectStatsCards: React.FC = () => {
  return (
    <div className="grid grid-cols-2 lg:grid-cols-4 gap-3">
      {STATS_DATA.map((item) => {
        const IconComponent = item.icon;
        return (
          <div
            key={item.id}
            className="bg-slate-900/80 border border-slate-800/90 rounded-xl p-3.5 hover:border-slate-700/80 transition-all shadow-sm flex items-start space-x-3.5"
          >
            <div
              className={`w-10 h-10 rounded-xl flex items-center justify-center shrink-0 border ${item.iconBg} ${item.iconColor} ${item.borderColor}`}
            >
              <IconComponent className="w-5 h-5" />
            </div>

            <div className="flex flex-col min-w-0">
              <span className="text-xl font-extrabold text-white tracking-tight leading-none">
                {item.value}
              </span>
              <span className="text-xs font-semibold text-slate-300 mt-1 truncate">
                {item.label}
              </span>
              <span className="text-[11px] text-slate-500 truncate mt-0.5">
                {item.subtitle}
              </span>
            </div>
          </div>
        );
      })}
    </div>
  );
};
