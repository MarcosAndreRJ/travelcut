import React, { useState } from 'react';
import {
  Loader2,
  AlertCircle,
  CheckCircle2,
  XCircle,
  ChevronDown,
  ChevronUp,
  Terminal,
  StopCircle
} from 'lucide-react';
import type { ToolInstallProgressDto } from '../../types/ipc';

interface ActiveJobBannerProps {
  title: string;
  progress: ToolInstallProgressDto;
  isCanceling: boolean;
  onCancel: () => void;
  onDismiss?: () => void;
}

export const ActiveJobBanner: React.FC<ActiveJobBannerProps> = ({
  title,
  progress,
  isCanceling,
  onCancel,
  onDismiss
}) => {
  const [showLogs, setShowLogs] = useState(false);

  const isFailed = progress.isFailed;
  const isSuccess = progress.isSuccess;
  const isCanceled = progress.isCanceled;
  const isCompleted = progress.isCompleted;
  const isRunning = !isCompleted;

  // Distingue cancelamento solicitado de concluído
  const getStatusText = () => {
    if (isCanceling && !isCanceled) {
      return 'Cancelamento solicitado... aguardando finalização do processo';
    }
    if (isCanceled) {
      return 'Operação cancelada pelo usuário';
    }
    if (isFailed) {
      return `Falha na operação: ${progress.errorMessage || 'Erro não especificado'}`;
    }
    if (isSuccess) {
      return 'Operação concluída com sucesso!';
    }
    return progress.currentMessage || 'Processando etapa...';
  };

  const getBorderColor = () => {
    if (isFailed) return 'border-red-500/40 bg-red-950/20';
    if (isCanceled) return 'border-amber-500/40 bg-amber-950/20';
    if (isSuccess) return 'border-emerald-500/40 bg-emerald-950/20';
    if (isCanceling) return 'border-amber-500/40 bg-amber-950/20';
    return 'border-cyan-500/40 bg-cyan-950/20';
  };

  return (
    <div
      role="region"
      aria-label="Progresso da operação"
      className={`rounded-2xl border p-5 space-y-4 transition-all duration-200 shadow-xl ${getBorderColor()}`}
    >
      <div className="flex items-start justify-between gap-4">
        <div className="flex items-center gap-3">
          <div className="w-10 h-10 rounded-xl bg-slate-900/60 border border-slate-700/60 flex items-center justify-center shrink-0">
            {isRunning ? (
              <Loader2 className="w-5 h-5 text-cyan-400 animate-spin" />
            ) : isSuccess ? (
              <CheckCircle2 className="w-5 h-5 text-emerald-400" />
            ) : isCanceled ? (
              <StopCircle className="w-5 h-5 text-amber-400" />
            ) : (
              <AlertCircle className="w-5 h-5 text-red-400" />
            )}
          </div>
          <div>
            <div className="flex items-center gap-2">
              <h4 className="text-sm font-bold text-white">{title}</h4>
              <span className="text-xs px-2 py-0.5 rounded-full bg-slate-800 text-slate-300 font-medium">
                {progress.stage}
              </span>
            </div>
            <p className="text-xs text-slate-300 mt-0.5" role="status">
              {getStatusText()}
            </p>
          </div>
        </div>

        {/* Action Button */}
        <div className="flex items-center gap-2">
          {isRunning && progress.canCancel && (
            <button
              onClick={onCancel}
              disabled={isCanceling}
              className="px-3 py-1.5 text-xs font-bold rounded-lg border border-red-500/40 bg-red-950/30 text-red-300 hover:bg-red-900/50 hover:text-white transition disabled:opacity-50 disabled:cursor-not-allowed flex items-center gap-1.5"
            >
              {isCanceling ? (
                <>
                  <Loader2 className="w-3.5 h-3.5 animate-spin" />
                  Cancelando...
                </>
              ) : (
                <>
                  <XCircle className="w-3.5 h-3.5" />
                  Cancelar
                </>
              )}
            </button>
          )}

          {isCompleted && onDismiss && (
            <button
              onClick={onDismiss}
              className="px-3 py-1.5 text-xs font-semibold rounded-lg bg-slate-800 text-slate-300 hover:text-white hover:bg-slate-700 transition"
            >
              Fechar
            </button>
          )}
        </div>
      </div>

      {/* Progress Bar */}
      <div className="space-y-1.5">
        <div className="flex justify-between text-xs text-slate-400 font-medium">
          <span>{progress.stage}</span>
          <span>{progress.progressPercent}%</span>
        </div>
        <div className="w-full h-2 rounded-full bg-slate-900 overflow-hidden border border-slate-800">
          <div
            className={`h-full transition-all duration-300 rounded-full ${
              isFailed
                ? 'bg-red-500'
                : isCanceled
                ? 'bg-amber-500'
                : isSuccess
                ? 'bg-emerald-500'
                : 'bg-gradient-to-r from-cyan-500 to-blue-500'
            }`}
            style={{ width: `${Math.max(2, Math.min(100, typeof progress.progressPercent === "number" ? progress.progressPercent : 0))}%` }}
          />
        </div>
      </div>

      {/* Output Logs Accordion */}
      {progress.outputLogs && progress.outputLogs.length > 0 && (
        <div className="border-t border-slate-800/80 pt-3">
          <button
            onClick={() => setShowLogs(!showLogs)}
            className="flex items-center gap-1.5 text-xs text-slate-400 hover:text-slate-200 transition font-medium"
          >
            <Terminal className="w-3.5 h-3.5 text-cyan-400" />
            <span>Logs de execução ({progress.outputLogs.length} linhas)</span>
            {showLogs ? (
              <ChevronUp className="w-3.5 h-3.5 ml-1" />
            ) : (
              <ChevronDown className="w-3.5 h-3.5 ml-1" />
            )}
          </button>

          {showLogs && (
            <div className="mt-2 p-3 rounded-xl bg-black/60 border border-slate-800 font-mono text-xs text-slate-300 max-h-48 overflow-y-auto space-y-1">
              {progress.outputLogs.map((log, idx) => (
                <div key={idx} className="whitespace-pre-wrap break-all leading-tight">
                  <span className="text-slate-600 select-none mr-2">{idx + 1}</span>
                  {log}
                </div>
              ))}
            </div>
          )}
        </div>
      )}
    </div>
  );
};
