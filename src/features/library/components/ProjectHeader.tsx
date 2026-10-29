import React, { useState } from 'react';
import { Pencil, Folder, Check, Clock } from 'lucide-react';
import { ProjectHeaderData } from '../types';

interface ProjectHeaderProps {
  data: ProjectHeaderData;
  onUpdateTitle?: (newTitle: string) => void;
  onStepClick?: (stepNumber: number) => void;
}

export const ProjectHeader: React.FC<ProjectHeaderProps> = ({
  data,
  onUpdateTitle,
  onStepClick
}) => {
  const [isEditingTitle, setIsEditingTitle] = useState(false);
  const [titleInput, setTitleInput] = useState(data.title);

  const handleTitleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    if (titleInput.trim() && onUpdateTitle) {
      onUpdateTitle(titleInput.trim());
    }
    setIsEditingTitle(false);
  };

  return (
    <div className="flex flex-col lg:flex-row lg:items-center justify-between gap-4 pb-4 border-b border-slate-800/80">
      {/* Left: Project title & path */}
      <div className="flex flex-col">
        <div className="flex items-center space-x-2.5">
          {isEditingTitle ? (
            <form onSubmit={handleTitleSubmit} className="flex items-center space-x-2">
              <input
                type="text"
                value={titleInput}
                onChange={(e) => setTitleInput(e.target.value)}
                autoFocus
                onBlur={handleTitleSubmit}
                className="bg-slate-900 border border-blue-500 rounded px-2.5 py-1 text-xl font-bold text-white focus:outline-none"
              />
              <button
                type="submit"
                className="text-xs bg-blue-600 hover:bg-blue-500 text-white px-2.5 py-1 rounded font-medium"
              >
                Salvar
              </button>
            </form>
          ) : (
            <div className="flex items-center space-x-2 group">
              <h1 className="text-xl lg:text-2xl font-bold text-white tracking-tight">
                {data.title}
              </h1>
              <button
                onClick={() => setIsEditingTitle(true)}
                title="Renomear projeto"
                className="text-slate-400 hover:text-blue-400 transition-colors p-1 rounded hover:bg-slate-800/60"
              >
                <Pencil className="w-4 h-4" />
              </button>
            </div>
          )}
        </div>
        <div className="flex items-center space-x-2 text-xs text-slate-400 mt-1">
          <Folder className="w-3.5 h-3.5 text-slate-500 shrink-0" />
          <span className="font-mono text-slate-400 truncate max-w-md">{data.path}</span>
        </div>
      </div>

      {/* Right: 5-step Stepper Pipeline */}
      <div className="flex items-center space-x-1 sm:space-x-3 overflow-x-auto py-1">
        {data.steps.map((step, index) => {
          const isCompleted = step.status === 'completed';
          const isInProgress = step.status === 'in_progress';

          return (
            <React.Fragment key={step.number}>
              <button
                onClick={() => onStepClick?.(step.number)}
                className={`flex items-center space-x-2 px-2.5 py-1.5 rounded-lg transition-all text-xs font-medium shrink-0 ${
                  isInProgress
                    ? 'bg-blue-600/20 border border-blue-500/50 text-blue-300'
                    : isCompleted
                    ? 'text-emerald-400 hover:bg-slate-800/40'
                    : 'text-slate-500 hover:text-slate-400 hover:bg-slate-800/30'
                }`}
              >
                {/* Step circle */}
                <div
                  className={`w-5 h-5 rounded-full flex items-center justify-center text-[10px] font-bold ${
                    isCompleted
                      ? 'bg-emerald-500 text-slate-950'
                      : isInProgress
                      ? 'bg-blue-500 text-white animate-pulse'
                      : 'bg-slate-800 text-slate-400 border border-slate-700'
                  }`}
                >
                  {isCompleted ? <Check className="w-3 h-3 stroke-[3]" /> : step.number}
                </div>

                <div className="flex flex-col text-left">
                  <span className="font-semibold text-slate-200">
                    {step.number}. {step.title}
                  </span>
                  <span className="text-[10px] leading-tight text-slate-400">
                    {isCompleted ? 'Concluído' : isInProgress ? 'Em andamento' : 'Pendente'}
                  </span>
                </div>
              </button>

              {index < data.steps.length - 1 && (
                <div
                  className={`hidden sm:block w-4 h-0.5 shrink-0 ${
                    isCompleted ? 'bg-emerald-500/40' : 'bg-slate-800'
                  }`}
                />
              )}
            </React.Fragment>
          );
        })}
      </div>
    </div>
  );
};
