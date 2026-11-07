import React, { useSyncExternalStore } from 'react';
import type { ToolInstallProgressDto } from '../../types/ipc';
import {
  modelDownloadTracker,
  type ModelDownloadState
} from '../../services/modelDownloadTracker';
import { formatBytes } from '../import/importUtils';
import { ActiveJobBanner } from './ActiveJobBanner';

export function useModelDownload(): ModelDownloadState {
  return useSyncExternalStore(modelDownloadTracker.subscribe, modelDownloadTracker.getState, modelDownloadTracker.getState);
}

const formatDuration = (seconds: number): string => {
  if (seconds < 60) return `${seconds} s`;
  const minutes = Math.round(seconds / 60);
  if (minutes < 60) return `${minutes} min`;
  return `${Math.floor(minutes / 60)} h ${minutes % 60} min`;
};

/** Linhas de telemetria; só entra o que o host informou ou que foi medido entre amostras reais. */
export function modelDownloadMetrics(state: ModelDownloadState): string[] {
  const lines: string[] = [];
  const bytes = state.progress?.bytesDownloaded;
  const total = state.progress?.totalBytes;
  if (typeof bytes === 'number') {
    lines.push(typeof total === 'number' ? `${formatBytes(bytes)} de ${formatBytes(total)}` : `${formatBytes(bytes)} baixados`);
  }
  if (state.speedBytesPerSecond != null) lines.push(`${formatBytes(state.speedBytesPerSecond)}/s`);
  if (state.etaSeconds != null) lines.push(`cerca de ${formatDuration(state.etaSeconds)} restantes`);
  if (state.pollFailures > 0) lines.push('sem resposta do host no momento; o download pode continuar');
  return lines;
}

/** Progresso exibido nas fases em que o host ainda não entregou (ou deixou de entregar) dados do job. */
function bannerProgress(state: ModelDownloadState): ToolInstallProgressDto {
  if (state.progress) return state.progress;
  const terminal = state.phase !== 'confirming';
  return {
    jobId: state.jobId ?? '',
    toolKey: 'whisper',
    stage: terminal ? 'Encerrado' : 'Aguardando confirmação',
    progressPercent: null,
    currentMessage: terminal ? '' : 'Confirme o download na janela do Windows que foi aberta pelo TravelCut.',
    outputLogs: [],
    canCancel: false,
    isCompleted: terminal,
    isSuccess: false,
    isFailed: state.phase === 'failed' || state.phase === 'lost',
    isCanceled: state.phase === 'canceled',
    errorMessage: state.errorMessage
  };
}

export const ModelDownloadBannerView: React.FC<{
  state: ModelDownloadState;
  onCancel: () => void;
  onDismiss: () => void;
}> = ({ state, onCancel, onDismiss }) => {
  if (state.phase === 'idle') return null;

  const base = bannerProgress(state);
  const progress: ToolInstallProgressDto =
    state.phase === 'lost' ? { ...base, isCompleted: true, isFailed: true, errorMessage: state.errorMessage } : base;

  return (
    <ActiveJobBanner
      title={`Download do modelo Whisper (${state.modelName ?? 'large-v3'})`}
      progress={progress}
      isCanceling={state.phase === 'canceling'}
      onCancel={onCancel}
      onDismiss={progress.isCompleted ? onDismiss : undefined}
      metrics={modelDownloadMetrics(state)}
      notice={progress.isCompleted ? null : state.errorMessage}
      successDetail="O diagnóstico do ambiente é reconsultado automaticamente."
    />
  );
};

/** Faixa global: fica acima das telas para o estado do download continuar visível ao navegar. */
export const ModelDownloadBanner: React.FC = () => {
  const state = useModelDownload();
  if (state.phase === 'idle') return null;

  return (
    <div className="px-4 pt-3 shrink-0 max-h-[45vh] overflow-y-auto">
      <ModelDownloadBannerView
        state={state}
        onCancel={() => void modelDownloadTracker.cancel()}
        onDismiss={() => modelDownloadTracker.dismiss()}
      />
    </div>
  );
};
