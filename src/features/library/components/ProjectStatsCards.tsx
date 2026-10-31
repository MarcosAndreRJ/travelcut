import React from 'react';
import { Video, HardDrive, Clock, Film } from 'lucide-react';
import { useProject } from '../../../contexts/ProjectContext';

export const ProjectStatsCards: React.FC = () => {
  const { project } = useProject();
  const clips = project.clips || [];

  const rawVideosCount = clips.length;
  const totalDurationSec = clips.reduce((acc, c) => acc + (c.durationSec || 0), 0);
  const totalHours = Math.floor(totalDurationSec / 3600);
  const totalMinutes = Math.floor((totalDurationSec % 3600) / 60);
  const durationText =
    totalHours > 0
      ? `${totalHours}h ${totalMinutes}m de gravacoes`
      : `${totalMinutes}m de gravacoes`;

  const totalBytes = clips.reduce((acc, c) => acc + (c.fileSize || 0), 0);
  const totalGB = (totalBytes / (1024 * 1024 * 1024)).toFixed(1);
  const totalMB = (totalBytes / (1024 * 1024)).toFixed(0);
  const spaceText =
    totalBytes > 1024 * 1024 * 1024
      ? `${totalGB} GB em disco`
      : totalBytes > 0
      ? `${totalMB} MB em disco`
      : 'Calculando tamanho';

  const resolutionCounts: Record<string, number> = {};
  clips.forEach((c) => {
    const res = c.resolution || 'HD';
    resolutionCounts[res] = (resolutionCounts[res] || 0) + 1;
  });
  const mainResolution =
    Object.entries(resolutionCounts).sort((a, b) => b[1] - a[1])[0]?.[0] || '1080p';

  const statsItems = [
    {
      id: 'raw-videos',
      icon: Video,
      iconBg: 'bg-blue-500/10',
      iconColor: 'text-blue-400',
      borderColor: 'border-blue-500/20',
      value: String(rawVideosCount),
      label: 'Videos brutos',
      subtitle: durationText,
    },
    {
      id: 'disk-usage',
      icon: HardDrive,
      iconBg: 'bg-emerald-500/10',
      iconColor: 'text-emerald-400',
      borderColor: 'border-emerald-500/20',
      value: totalBytes > 1024 * 1024 * 1024 ? `${totalGB} GB` : `${totalMB} MB`,
      label: 'Espaco em disco',
      subtitle: spaceText,
    },
    {
      id: 'total-duration',
      icon: Clock,
      iconBg: 'bg-cyan-500/10',
      iconColor: 'text-cyan-400',
      borderColor: 'border-cyan-500/20',
      value: `${totalMinutes} min`,
      label: 'Duracao total',
      subtitle: `${totalDurationSec}s consolidados`,
    },
    {
      id: 'dominant-format',
      icon: Film,
      iconBg: 'bg-purple-500/10',
      iconColor: 'text-purple-400',
      borderColor: 'border-purple-500/20',
      value: mainResolution,
      label: 'Resolucao principal',
      subtitle: `${rawVideosCount} arquivos indexados`,
    },
  ];

  return (
    <div className="grid grid-cols-2 lg:grid-cols-4 gap-3">
      {statsItems.map((item) => {
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
