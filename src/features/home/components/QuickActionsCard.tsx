import React from 'react';
import { Play, RotateCcw, FileText } from 'lucide-react';
import { QuickAction } from '../types';

interface QuickActionsCardProps {
  actions: QuickAction[];
  onActionClick?: (action: QuickAction) => void;
}

export const QuickActionsCard: React.FC<QuickActionsCardProps> = ({
  actions,
  onActionClick,
}) => {
  const renderDaVinciIcon = () => (
    <svg className="w-4 h-4 shrink-0" viewBox="0 0 24 24" fill="none">
      <circle cx="8" cy="8" r="4.5" fill="#f43f5e" />
      <circle cx="16" cy="8" r="4.5" fill="#38bdf8" />
      <circle cx="12" cy="15" r="4.5" fill="#f59e0b" />
    </svg>
  );

  const renderIcon = (type: QuickAction['iconType']) => {
    switch (type) {
      case 'play':
        return <Play size={16} className="fill-slate-950 shrink-0" />;
      case 'continue':
        return <RotateCcw size={16} className="text-cyan-400 shrink-0" />;
      case 'davinci':
        return renderDaVinciIcon();
      case 'report':
        return <FileText size={16} className="text-cyan-400 shrink-0" />;
    }
  };

  return (
    <div className="w-full rounded-2xl bg-[#0d131f]/90 border border-slate-800/90 p-5 shadow-xl shadow-black/25">
      {/* Title */}
      <h3 className="text-base font-bold text-white tracking-tight mb-4">
        Ações rápidas
      </h3>

      {/* Button List */}
      <div className="flex flex-col gap-2.5">
        {actions.map((action) => {
          if (action.variant === 'primary') {
            return (
              <button
                key={action.id}
                type="button"
                onClick={() => onActionClick?.(action)}
                className="w-full py-3 px-4 rounded-xl bg-gradient-to-r from-cyan-400 via-cyan-400 to-cyan-500 hover:from-cyan-300 hover:to-cyan-400 text-slate-950 font-bold text-sm flex items-center justify-center gap-2.5 shadow-lg shadow-cyan-500/25 active:scale-[0.99] transition-all duration-150"
              >
                {renderIcon(action.iconType)}
                <span>{action.label}</span>
              </button>
            );
          }

          return (
            <button
              key={action.id}
              type="button"
              onClick={() => onActionClick?.(action)}
              className="w-full py-2.5 px-3.5 rounded-xl bg-slate-900/80 hover:bg-slate-800/90 text-slate-200 hover:text-white border border-slate-800/80 hover:border-slate-700 text-xs md:text-sm font-medium flex items-center gap-3 transition-all duration-150 active:scale-[0.99]"
            >
              {renderIcon(action.iconType)}
              <span className="truncate">{action.label}</span>
            </button>
          );
        })}
      </div>
    </div>
  );
};
