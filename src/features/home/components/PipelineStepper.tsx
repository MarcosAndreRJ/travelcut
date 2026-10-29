import React from 'react';
import { Check, Loader2 } from 'lucide-react';
import { PipelineProgress, PipelineStep } from '../types';

interface PipelineStepperProps {
  pipeline: PipelineProgress;
  onStepClick?: (step: PipelineStep) => void;
}

export const PipelineStepper: React.FC<PipelineStepperProps> = ({
  pipeline,
  onStepClick,
}) => {
  return (
    <div className="w-full rounded-2xl bg-[#0d131f]/90 border border-slate-800/90 p-6 shadow-xl shadow-black/25">
      {/* Card Header */}
      <div className="flex items-center justify-between mb-8">
        <h2 className="text-lg font-bold text-white tracking-tight">
          Pipeline de edição
        </h2>
      </div>

      {/* Stepper Steps Row */}
      <div className="relative mb-8">
        <div className="flex items-center justify-between relative z-10">
          {pipeline.steps.map((step, index) => {
            const isCompleted = step.status === 'completed';
            const isInProgress = step.status === 'in_progress';
            const isPending = step.status === 'pending';

            return (
              <div
                key={step.id}
                onClick={() => onStepClick?.(step)}
                className="flex-1 flex flex-col items-center text-center cursor-pointer group"
              >
                {/* Circle Indicator */}
                <div className="relative flex items-center justify-center">
                  {isCompleted && (
                    <div className="w-9 h-9 rounded-full bg-emerald-500/20 border-2 border-emerald-500 text-emerald-400 flex items-center justify-center shadow-lg shadow-emerald-500/20 group-hover:scale-105 transition-transform">
                      <Check size={18} strokeWidth={2.5} />
                    </div>
                  )}

                  {isInProgress && (
                    <div className="w-9 h-9 rounded-full bg-cyan-500/20 border-2 border-cyan-400 text-cyan-300 flex items-center justify-center shadow-lg shadow-cyan-500/30 group-hover:scale-105 transition-transform animate-pulse">
                      <span className="text-xs font-bold">{step.number}</span>
                    </div>
                  )}

                  {isPending && (
                    <div className="w-9 h-9 rounded-full bg-slate-800/80 border border-slate-700/80 text-slate-500 flex items-center justify-center group-hover:border-slate-600 transition-colors">
                      <span className="text-xs font-medium">{step.number}</span>
                    </div>
                  )}
                </div>

                {/* Step Title & Status */}
                <div className="mt-2.5">
                  <div
                    className={`text-xs font-semibold ${
                      isInProgress
                        ? 'text-cyan-300'
                        : isCompleted
                        ? 'text-white'
                        : 'text-slate-400'
                    }`}
                  >
                    {step.title}
                  </div>
                  <div
                    className={`text-[11px] font-medium mt-0.5 ${
                      isCompleted
                        ? 'text-emerald-400'
                        : isInProgress
                        ? 'text-cyan-400'
                        : 'text-slate-500'
                    }`}
                  >
                    {step.statusLabel}
                  </div>
                </div>
              </div>
            );
          })}
        </div>

        {/* Connecting Progress Lines */}
        <div className="absolute top-4 left-6 right-6 h-[2px] -z-0 bg-slate-800">
          {/* Active / Completed portion (Steps 1 & 2 done, 3 in progress = approx 45%) */}
          <div
            className="h-full bg-gradient-to-r from-emerald-500 via-emerald-400 to-cyan-400"
            style={{ width: '42%' }}
          />
        </div>
      </div>

      {/* Active Task Status Box */}
      <div className="space-y-2 pt-2 border-t border-slate-800/60">
        <div className="flex items-center justify-between text-xs md:text-sm">
          <span className="font-semibold text-white flex items-center gap-2">
            <Loader2 size={15} className="animate-spin text-cyan-400 shrink-0" />
            {pipeline.currentTask}
          </span>
          <span className="font-bold text-cyan-400 text-sm">
            {pipeline.progressPercentage}%
          </span>
        </div>

        {/* Progress Bar */}
        <div className="w-full h-2.5 bg-slate-800/90 rounded-full overflow-hidden p-0.5 border border-slate-700/50">
          <div
            className="h-full bg-gradient-to-r from-cyan-500 to-blue-500 rounded-full transition-all duration-500 shadow-sm shadow-cyan-400/40"
            style={{ width: `${pipeline.progressPercentage}%` }}
          />
        </div>

        {/* Processing Filename & ETA */}
        <div className="flex items-center justify-between text-[11px] text-slate-400 pt-0.5">
          <span className="truncate">{pipeline.currentItem}</span>
          <span className="shrink-0">{pipeline.estimatedTime}</span>
        </div>
      </div>
    </div>
  );
};
