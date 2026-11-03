import React from 'react';
import { Sparkles, RefreshCw } from 'lucide-react';
import type { MediaAnalysisDto, MediaAssetDto, PipelineStage, StageState } from '../../types/ipc';
import { ResultNotice } from './ResultNotice';
import { TranscriptPanel } from './TranscriptPanel';
import { SignalsList } from './SignalsPanel';
import { SceneCard } from './SceneCard';

interface ClipAnalysisPanelProps {
  asset: MediaAssetDto;
  analysis: MediaAnalysisDto | null;
  isLoading: boolean;
  error: string | null;
  mediaStageStates: Partial<Record<PipelineStage, StageState>> | null;
  selectedSec: number | null;
  onSeek: (sec: number) => void;
  onAnalyze: () => void;
  onRetry: () => void;
  analyzeDisabledReason: string | null;
  isStarting: boolean;
  isMock: boolean;
}

/**
 * Análise de um clipe selecionado. Todos os dados vêm de analysis.getMedia;
 * nada é sintetizado: cada seção mostra o estado real (não executado, indisponível, falhou ou concluído).
 */
export const ClipAnalysisPanel: React.FC<ClipAnalysisPanelProps> = ({
  asset,
  analysis,
  isLoading,
  error,
  mediaStageStates,
  selectedSec,
  onSeek,
  onAnalyze,
  onRetry,
  analyzeDisabledReason,
  isStarting,
  isMock
}) => {
  const fileName = asset.relativePath.split(/[/\\]/).pop() || asset.relativePath;
  const isRunningHere = mediaStageStates
    ? Object.values(mediaStageStates).some((s) => s === 'running')
    : false;

  const needsRun = analysis
    ? analysis.transcriptionStatus === 'not_run' || analysis.silenceStatus === 'not_run'
    : false;
  const hasFailure = analysis
    ? analysis.transcriptionStatus === 'failed' || analysis.silenceStatus === 'failed'
    : false;
  const showAnalyzeButton = needsRun || hasFailure;

  return (
    <div className="flex flex-col min-h-0 flex-1 p-4 gap-3">
      <div className="flex flex-wrap items-center justify-between gap-2 shrink-0">
        <div className="flex items-center gap-2 min-w-0">
          <span className="text-xs font-bold text-white">Análise do clipe</span>
          <span className="text-[10px] text-slate-400 font-mono truncate" title={asset.relativePath}>
            {fileName}
          </span>
          {isMock && (
            <span className="px-1.5 py-0.5 rounded bg-amber-500/15 border border-amber-500/40 text-amber-300 text-[9px] font-bold tracking-wider">
              DEMO
            </span>
          )}
        </div>
        {showAnalyzeButton && (
          <button
            type="button"
            onClick={onAnalyze}
            disabled={!!analyzeDisabledReason || isStarting}
            title={analyzeDisabledReason ?? undefined}
            className="px-3 py-1.5 rounded-xl bg-slate-800 hover:bg-slate-700 text-slate-200 text-xs font-medium flex items-center gap-1.5 border border-slate-700 disabled:opacity-50"
          >
            {hasFailure && !needsRun ? (
              <RefreshCw className="w-3.5 h-3.5 text-cyan-400" />
            ) : (
              <Sparkles className="w-3.5 h-3.5 text-cyan-400" />
            )}
            {isStarting ? 'Iniciando…' : hasFailure && !needsRun ? 'Tentar novamente' : 'Analisar este clipe'}
          </button>
        )}
      </div>

      {isRunningHere && (
        <p role="status" className="text-[11px] text-cyan-300 shrink-0">
          Análise em andamento para este clipe; os resultados serão atualizados ao final.
        </p>
      )}

      {isLoading && !analysis && (
        <p role="status" className="text-[11px] text-slate-400">Carregando análise do clipe…</p>
      )}

      {error && (
        <div role="alert" className="flex flex-wrap items-center gap-2 text-[11px] text-rose-300">
          <span>Não foi possível carregar a análise: {error}</span>
          <button
            type="button"
            onClick={onRetry}
            className="px-2 py-0.5 rounded-lg border border-slate-700 text-slate-200 hover:bg-slate-800"
          >
            Tentar novamente
          </button>
        </div>
      )}

      {analysis && (
        <div className="flex-1 min-h-0 flex flex-col xl:flex-row gap-4 overflow-hidden">
          <div className="flex-[3] min-w-0 min-h-0 flex flex-col">
            {analysis.transcriptionStatus === 'completed' && analysis.transcript ? (
              <TranscriptPanel
                transcript={analysis.transcript}
                hesitations={analysis.hesitations}
                selectedSec={selectedSec}
                onSeek={onSeek}
              />
            ) : analysis.transcriptionStatus === 'completed' ? (
              <p className="text-[11px] text-slate-500">Transcrição concluída sem dados para exibir.</p>
            ) : (
              <ResultNotice
                label="Transcrição"
                status={analysis.transcriptionStatus}
                reason={analysis.transcriptionReason}
              />
            )}
          </div>

          <div className="flex-[2] min-w-0 min-h-0 overflow-y-auto pr-1 space-y-4">
            <SignalsList
              silences={analysis.silences}
              silenceReason={analysis.silenceStatus === 'completed' ? null : analysis.silenceReason}
              silenceCompleted={analysis.silenceStatus === 'completed'}
              hesitations={analysis.hesitations}
              transcriptionCompleted={analysis.transcriptionStatus === 'completed'}
              transcriptionReason={analysis.transcriptionReason}
              selectedSec={selectedSec}
              onSeek={onSeek}
            />

            <section aria-labelledby="scene-title" className="space-y-2">
              <h4 id="scene-title" className="text-[11px] font-bold text-cyan-300 uppercase tracking-wider">
                Cena
              </h4>
              <SceneCard scene={analysis.scene} sceneStageState={mediaStageStates?.scene} />
            </section>
          </div>
        </div>
      )}

      {!analysis && !isLoading && !error && (
        <p className="text-[11px] text-slate-500">Sem dados de análise para este clipe.</p>
      )}
    </div>
  );
};
