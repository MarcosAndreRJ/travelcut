import React, { useState } from 'react';
import {
  Layers,
  ChevronDown,
  ChevronUp,
  CheckCircle2,
  Clock,
  Loader2,
  Sparkles,
  RefreshCw
} from 'lucide-react';
import { ProcessingQueueTask } from '../types';

interface ProcessingQueueProps {
  tasks: ProcessingQueueTask[];
  onRerunTask?: (taskId: string) => void;
}

export const ProcessingQueue: React.FC<ProcessingQueueProps> = ({
  tasks,
  onRerunTask
}) => {
  const [isCollapsed, setIsCollapsed] = useState(false);

  const completedCount = tasks.filter((t) => t.status === 'completed').length;
  const inProgressTask = tasks.find((t) => t.status === 'in_progress');

  return (
    <div className="bg-slate-900/85 border border-slate-800/90 rounded-xl overflow-hidden shadow-sm transition-all">
      {/* Header */}
      <div
        onClick={() => setIsCollapsed(!isCollapsed)}
        className="px-4 py-3 flex items-center justify-between cursor-pointer hover:bg-slate-850/60 transition-colors border-b border-slate-800/70"
      >
        <div className="flex items-center space-x-2.5">
          <div className="w-7 h-7 rounded-lg bg-blue-500/10 border border-blue-500/20 text-blue-400 flex items-center justify-center">
            <Layers className="w-4 h-4" />
          </div>
          <div className="flex items-center space-x-2">
            <h3 className="text-sm font-bold text-white tracking-tight">Fila de Processamento</h3>
            <span className="text-[11px] px-2 py-0.5 rounded-full bg-slate-800 text-slate-300 font-medium">
              {completedCount} de {tasks.length} etapas concluídas
            </span>
          </div>
        </div>

        <div className="flex items-center space-x-3 text-xs text-slate-400">
          {inProgressTask && (
            <div className="hidden sm:flex items-center space-x-1.5 text-blue-400">
              <span className="w-2 h-2 rounded-full bg-blue-400 animate-ping" />
              <span className="text-[11px] font-medium">{inProgressTask.name}</span>
            </div>
          )}

          <button
            type="button"
            className="p-1 text-slate-400 hover:text-white rounded hover:bg-slate-800"
          >
            {isCollapsed ? (
              <ChevronDown className="w-4 h-4" />
            ) : (
              <ChevronUp className="w-4 h-4" />
            )}
          </button>
        </div>
      </div>

      {/* Task List */}
      {!isCollapsed && (
        <div className="p-3.5 divide-y divide-slate-800/50">
          {tasks.map((task) => {
            const isCompleted = task.status === 'completed';
            const isInProgress = task.status === 'in_progress';

            return (
              <div
                key={task.id}
                className="py-2.5 first:pt-1 last:pb-1 flex flex-col space-y-1.5 group"
              >
                <div className="flex items-center justify-between text-xs">
                  {/* Left: Step number & Title */}
                  <div className="flex items-center space-x-2.5 min-w-0">
                    <span
                      className={`w-5 h-5 rounded-md flex items-center justify-center font-bold text-[11px] shrink-0 ${
                        isCompleted
                          ? 'bg-emerald-500/20 text-emerald-400 border border-emerald-500/30'
                          : isInProgress
                          ? 'bg-blue-500/20 text-blue-400 border border-blue-500/40'
                          : 'bg-slate-800 text-slate-500 border border-slate-700/60'
                      }`}
                    >
                      {task.step}
                    </span>

                    <div className="flex flex-col min-w-0">
                      <div className="flex items-center space-x-2">
                        <span
                          className={`font-semibold truncate ${
                            isInProgress
                              ? 'text-white'
                              : isCompleted
                              ? 'text-slate-200'
                              : 'text-slate-400'
                          }`}
                        >
                          {task.name}
                        </span>
                      </div>
                      <span className="text-[11px] text-slate-500 truncate mt-0.5">
                        {task.description}
                      </span>
                    </div>
                  </div>

                  {/* Right: Badge & Status Text */}
                  <div className="flex items-center space-x-3 shrink-0 ml-2">
                    <span className="text-[11px] text-slate-400 font-mono hidden sm:inline">
                      {task.statusText}
                    </span>

                    {isCompleted ? (
                      <span className="inline-flex items-center space-x-1 px-2 py-0.5 rounded-full text-[10px] font-semibold bg-emerald-500/15 text-emerald-400 border border-emerald-500/30">
                        <CheckCircle2 className="w-3 h-3" />
                        <span>Concluído</span>
                      </span>
                    ) : isInProgress ? (
                      <span className="inline-flex items-center space-x-1 px-2 py-0.5 rounded-full text-[10px] font-semibold bg-blue-500/15 text-blue-400 border border-blue-500/30 animate-pulse">
                        <Loader2 className="w-3 h-3 animate-spin" />
                        <span>Em andamento</span>
                      </span>
                    ) : (
                      <span className="inline-flex items-center space-x-1 px-2 py-0.5 rounded-full text-[10px] font-medium bg-slate-800/80 text-slate-400 border border-slate-700/50">
                        <Clock className="w-3 h-3" />
                        <span>Pendente</span>
                      </span>
                    )}

                    {onRerunTask && isCompleted && (
                      <button
                        onClick={() => onRerunTask(task.id)}
                        title="Reexecutar etapa"
                        className="opacity-0 group-hover:opacity-100 p-1 text-slate-400 hover:text-blue-400 transition-opacity"
                      >
                        <RefreshCw className="w-3 h-3" />
                      </button>
                    )}
                  </div>
                </div>

                {/* Progress bar */}
                <div className="w-full bg-slate-950/80 h-1.5 rounded-full overflow-hidden border border-slate-800/60">
                  <div
                    className={`h-full rounded-full transition-all duration-500 ${
                      isCompleted
                        ? 'bg-emerald-500'
                        : isInProgress
                        ? 'bg-gradient-to-r from-blue-500 to-cyan-400 animate-pulse'
                        : 'bg-transparent'
                    }`}
                    style={{ width: `${task.progress}%` }}
                  />
                </div>
              </div>
            );
          })}
        </div>
      )}
    </div>
  );
};
