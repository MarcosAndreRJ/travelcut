import React from 'react';
import { Mic, Terminal, Sparkles, Bot } from 'lucide-react';
import { ToolIntegration } from '../types';

interface ToolStatusCardProps {
  tools: ToolIntegration[];
  onToolClick?: (tool: ToolIntegration) => void;
}

export const ToolStatusCard: React.FC<ToolStatusCardProps> = ({
  tools,
  onToolClick,
}) => {
  const renderDaVinciIcon = () => (
    <svg className="w-4 h-4 shrink-0" viewBox="0 0 24 24" fill="none">
      <circle cx="8" cy="8" r="4.5" fill="#f43f5e" />
      <circle cx="16" cy="8" r="4.5" fill="#38bdf8" />
      <circle cx="12" cy="15" r="4.5" fill="#f59e0b" />
    </svg>
  );

  const renderIcon = (type: ToolIntegration['iconType']) => {
    switch (type) {
      case 'davinci':
        return renderDaVinciIcon();
      case 'whisper':
        return <Mic size={15} className="text-cyan-400 shrink-0" />;
      case 'ffmpeg':
        return <Terminal size={15} className="text-purple-400 shrink-0" />;
      case 'hyperframes':
        return <Sparkles size={15} className="text-amber-400 shrink-0" />;
      case 'claude':
        return <Bot size={15} className="text-rose-400 shrink-0" />;
    }
  };

  return (
    <div className="w-full rounded-2xl bg-[#0d131f]/90 border border-slate-800/90 p-5 shadow-xl shadow-black/25">
      {/* Header */}
      <div className="flex items-center justify-between mb-4">
        <h3 className="text-base font-bold text-white tracking-tight">
          Status das ferramentas
        </h3>
        <div className="flex items-center gap-1.5 text-xs font-medium text-emerald-400">
          <span className="w-2 h-2 rounded-full bg-emerald-400 animate-pulse" />
          <span className="hidden sm:inline">Tudo pronto para edição</span>
          <span className="sm:hidden">Pronto</span>
        </div>
      </div>

      {/* Tool List */}
      <div className="divide-y divide-slate-800/60">
        {tools.map((tool) => {
          const isConnected = tool.status === 'connected';
          const isAvailable = tool.status === 'available';

          return (
            <div
              key={tool.id}
              onClick={() => onToolClick?.(tool)}
              className="py-2.5 first:pt-0 last:pb-0 flex items-center justify-between gap-3 group cursor-pointer hover:bg-slate-800/30 px-1 rounded-lg transition-colors"
            >
              <div className="flex items-center gap-2.5 min-w-0">
                <div className="p-1.5 rounded-lg bg-slate-900 border border-slate-800 shrink-0">
                  {renderIcon(tool.iconType)}
                </div>
                <span className="text-xs md:text-sm font-medium text-slate-200 group-hover:text-white transition-colors truncate">
                  {tool.name}
                </span>
              </div>

              <div className="flex items-center gap-1.5 shrink-0">
                <span
                  className={`w-1.5 h-1.5 rounded-full ${
                    isConnected
                      ? 'bg-emerald-400 shadow-[0_0_8px_rgba(52,211,153,0.6)]'
                      : isAvailable
                      ? 'bg-emerald-400/80'
                      : 'bg-slate-500'
                  }`}
                />
                <span
                  className={`text-xs font-medium ${
                    isConnected || isAvailable
                      ? 'text-emerald-400'
                      : 'text-slate-500'
                  }`}
                >
                  {tool.statusLabel}
                </span>
              </div>
            </div>
          );
        })}
      </div>
    </div>
  );
};
