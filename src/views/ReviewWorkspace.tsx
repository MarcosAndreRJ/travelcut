import React, { useState, useMemo } from 'react';
import {
  Scissors,
  Sparkles,
  Check,
  X,
  RotateCcw,
  Clock,
  Eye,
  Share2,
  Filter,
  CheckCircle2,
  Film,
  Layers,
  ChevronRight,
  TrendingDown,
  Volume2
} from 'lucide-react';
import { useEditingWorkspace } from '../features/editing/context/EditingContext';
import { IntegratedVideoPlayer } from '../features/editing/components/IntegratedVideoPlayer';
import { IntegratedTranscript } from '../features/editing/components/IntegratedTranscript';
import { IntegratedTimeline } from '../features/editing/components/IntegratedTimeline';
import { CutReason } from '../features/editing/types/editingTypes';
import { secondsToTimecode } from '../features/editing/utils/davinciExporter';

export interface ReviewWorkspaceProps {
  onNavigateToTab?: (tab: string) => void;
}

export const ReviewWorkspace: React.FC<ReviewWorkspaceProps> = ({ onNavigateToTab }) => {
  const {
    projectName,
    mediaAssets,
    selectedAssetId,
    selectAssetById,
    allCutSuggestions,
    acceptCut,
    rejectCut,
    resetAllCuts,
    acceptAllPendingCuts,
    exportData,
    history,
    viewModeAB,
    setViewModeAB
  } = useEditingWorkspace();

  const [activeCategoryFilter, setActiveCategoryFilter] = useState<CutReason | 'all'>('all');
  const [activeTabSubView, setActiveTabSubView] = useState<'editor' | 'audit'>('editor');

  // Filter cuts based on category filter
  const filteredCuts = useMemo(() => {
    if (activeCategoryFilter === 'all') return allCutSuggestions;
    return allCutSuggestions.filter((c) => c.reason === activeCategoryFilter);
  }, [allCutSuggestions, activeCategoryFilter]);

  const acceptedCount = allCutSuggestions.filter((c) => c.decisionState === 'accepted').length;
  const pendingCount = allCutSuggestions.filter((c) => c.decisionState === 'pending').length;
  const rejectedCount = allCutSuggestions.filter((c) => c.decisionState === 'rejected').length;

  return (
    <div className="flex-1 flex flex-col h-full bg-studio-bg overflow-hidden select-none">
      {/* Top Professional Review Header Bar */}
      <div className="h-14 border-b border-studio-border bg-studio-panel px-4 flex items-center justify-between shrink-0">
        <div className="flex items-center space-x-3">
          <div className="w-8 h-8 rounded-lg bg-indigo-600/20 border border-indigo-500/40 flex items-center justify-center text-indigo-400">
            <Scissors className="w-4 h-4" />
          </div>
          <div>
            <div className="flex items-center space-x-2">
              <h2 className="text-sm font-bold text-white tracking-tight">
                Espaço de Trabalho de Revisão & Refinamento de Cortes
              </h2>
              <span className="text-[10px] bg-indigo-900/60 border border-indigo-500/40 text-indigo-300 font-mono px-1.5 py-0.5 rounded">
                DaVinci Resolve Ready
              </span>
            </div>
            <p className="text-[11px] text-studio-muted">
              {projectName} • Sincronização em tempo real entre transcrição, vídeo e timeline NLE
            </p>
          </div>
        </div>

        {/* Global Stats & Metrics Pill */}
        <div className="hidden lg:flex items-center space-x-4 bg-studio-card border border-studio-border px-3 py-1.5 rounded-xl text-xs font-mono">
          <div className="flex items-center space-x-1.5 text-emerald-400">
            <TrendingDown className="w-3.5 h-3.5" />
            <span>-{exportData.savedSeconds}s economizados</span>
          </div>
          <span className="text-studio-border">|</span>
          <div className="text-studio-subtle">
            Original: <span className="text-zinc-200">{secondsToTimecode(exportData.totalOriginalSec)}</span>
          </div>
          <span className="text-studio-border">|</span>
          <div className="text-indigo-300">
            Montagem: <span className="text-white font-bold">{secondsToTimecode(exportData.totalEditedSec)}</span>
          </div>
        </div>

        {/* Navigation & Action Buttons */}
        <div className="flex items-center space-x-2">
          {/* Subview Toggle */}
          <div className="flex items-center bg-studio-card p-1 rounded-lg border border-studio-border text-xs">
            <button
              onClick={() => setActiveTabSubView('editor')}
              className={`px-3 py-1 rounded-md font-medium transition-colors ${
                activeTabSubView === 'editor'
                  ? 'bg-indigo-600 text-white shadow-glow-indigo'
                  : 'text-studio-muted hover:text-white'
              }`}
            >
              Editor NLE + Transcrição
            </button>
            <button
              onClick={() => setActiveTabSubView('audit')}
              className={`px-3 py-1 rounded-md font-medium transition-colors flex items-center space-x-1 ${
                activeTabSubView === 'audit'
                  ? 'bg-indigo-600 text-white shadow-glow-indigo'
                  : 'text-studio-muted hover:text-white'
              }`}
            >
              <span>Auditoria ({pendingCount})</span>
            </button>
          </div>

          <button
            onClick={() => onNavigateToTab?.('export')}
            className="px-3 py-1.5 bg-gradient-to-r from-emerald-600 to-teal-600 hover:from-emerald-500 hover:to-teal-500 text-white rounded-lg text-xs font-semibold flex items-center space-x-1.5 shadow-md shadow-emerald-900/40 transition-all"
          >
            <Share2 className="w-3.5 h-3.5" />
            <span>Exportar DaVinci</span>
          </button>
        </div>
      </div>

      {/* Clip Selector Ribbon */}
      <div className="h-9 border-b border-studio-border bg-studio-card/60 px-4 flex items-center justify-between shrink-0 text-xs overflow-x-auto">
        <div className="flex items-center space-x-2">
          <span className="text-[11px] font-mono text-studio-subtle uppercase">Clipe Ativo:</span>
          {mediaAssets.map((asset) => (
            <button
              key={asset.id}
              onClick={() => selectAssetById(asset.id)}
              className={`px-2.5 py-1 rounded-md text-xs font-medium transition-all flex items-center space-x-1.5 ${
                selectedAssetId === asset.id
                  ? 'bg-indigo-600 text-white font-bold shadow-sm'
                  : 'bg-studio-panel text-studio-muted hover:text-white border border-studio-border'
              }`}
            >
              <Film className="w-3 h-3" />
              <span className="truncate max-w-[120px]">{asset.displayName}</span>
              <span className="text-[10px] font-mono opacity-70">
                ({asset.cutSuggestions.length})
              </span>
            </button>
          ))}
        </div>

        {/* Batch Decision Controls */}
        <div className="flex items-center space-x-2">
          {pendingCount > 0 && (
            <button
              onClick={acceptAllPendingCuts}
              className="px-2.5 py-1 bg-amber-600/80 hover:bg-amber-500 text-white rounded text-[11px] font-semibold flex items-center space-x-1"
              title="Aprovar todas as sugestões de corte geradas pela IA"
            >
              <Check className="w-3 h-3" />
              <span>Aprovar Todos ({pendingCount})</span>
            </button>
          )}
          <button
            onClick={resetAllCuts}
            className="px-2 py-1 bg-studio-panel hover:bg-studio-card text-studio-muted hover:text-white rounded text-[11px] border border-studio-border flex items-center space-x-1"
            title="Resetar estado das sugestões"
          >
            <RotateCcw className="w-3 h-3" />
            <span>Resetar Decisões</span>
          </button>
        </div>
      </div>

      {/* Main Workspace Body */}
      {activeTabSubView === 'editor' ? (
        <div className="flex-1 flex flex-col overflow-hidden">
          {/* Top Split: Video Player on Left, Synchronized Transcript on Right */}
          <div className="flex-1 flex overflow-hidden">
            <IntegratedVideoPlayer />
            <IntegratedTranscript />
          </div>

          {/* Bottom Pane: Synchronized Multi-track Timeline */}
          <IntegratedTimeline />
        </div>
      ) : (
        /* Audit View: Comprehensive Cut Suggestions Inspector */
        <div className="flex-1 overflow-y-auto p-6 max-w-6xl mx-auto w-full space-y-6 custom-scrollbar">
          {/* Filter Pills */}
          <div className="flex items-center justify-between flex-wrap gap-2">
            <div className="flex items-center space-x-1 bg-studio-card p-1 rounded-xl border border-studio-border">
              {(
                [
                  { id: 'all', label: 'Todos os Cortes' },
                  { id: 'hesitation', label: 'Hesitações & Gaguejos' },
                  { id: 'silence', label: 'Pausas Longas / Silêncio' },
                  { id: 'repetition', label: 'Repetições de Frases' },
                  { id: 'unstable', label: 'Movimento Tremido' }
                ] as const
              ).map((cat) => (
                <button
                  key={cat.id}
                  onClick={() => setActiveCategoryFilter(cat.id)}
                  className={`px-3 py-1.5 rounded-lg text-xs font-medium transition-colors ${
                    activeCategoryFilter === cat.id
                      ? 'bg-indigo-600 text-white font-bold shadow-sm'
                      : 'text-studio-muted hover:text-white'
                  }`}
                >
                  {cat.label}
                </button>
              ))}
            </div>

            <span className="text-xs font-mono text-studio-subtle">
              Mostrando {filteredCuts.length} sugestões de corte
            </span>
          </div>

          {/* Cuts Grid / List */}
          <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
            {filteredCuts.map((cut) => {
              const asset = mediaAssets.find((a) => a.id === cut.mediaId);

              return (
                <div
                  key={cut.id}
                  className={`p-4 rounded-xl border transition-all flex flex-col justify-between space-y-3 ${
                    cut.decisionState === 'accepted'
                      ? 'bg-studio-card/90 border-emerald-500/40 shadow-sm'
                      : cut.decisionState === 'rejected'
                      ? 'bg-studio-card/30 border-studio-border/50 opacity-60'
                      : 'bg-studio-card border-studio-border hover:border-indigo-500/50'
                  }`}
                >
                  <div className="space-y-2">
                    <div className="flex items-center justify-between">
                      <div className="flex items-center space-x-2">
                        <span className="px-2 py-0.5 rounded text-[10px] font-bold uppercase tracking-wider bg-amber-950/60 text-amber-300 border border-amber-500/40">
                          {cut.reasonLabel}
                        </span>
                        <span className="text-[11px] font-mono text-studio-subtle">
                          Clip: {asset?.displayName}
                        </span>
                      </div>

                      <span className="text-[11px] font-mono font-bold text-emerald-400">
                        -{cut.durationSec}s
                      </span>
                    </div>

                    <p className="text-xs text-studio-text leading-relaxed font-medium">
                      "{cut.previewText}"
                    </p>

                    <p className="text-[11px] text-studio-muted">
                      {cut.reason}
                    </p>

                    <div className="flex items-center space-x-3 text-[10px] font-mono text-studio-subtle pt-1">
                      <span>Início: {secondsToTimecode(cut.startSec)}</span>
                      <span>•</span>
                      <span>Fim: {secondsToTimecode(cut.endSec)}</span>
                      <span>•</span>
                      <span>Confiança: {Math.round(cut.confidence * 100)}%</span>
                    </div>
                  </div>

                  {/* Actions */}
                  <div className="flex items-center justify-between pt-2 border-t border-studio-border/60">
                    <span className="text-[11px] font-mono">
                      Status:{' '}
                      <span
                        className={
                          cut.decisionState === 'accepted'
                            ? 'text-emerald-400 font-bold'
                            : cut.decisionState === 'rejected'
                            ? 'text-zinc-400'
                            : 'text-amber-300 font-bold'
                        }
                      >
                        {cut.decisionState === 'accepted'
                          ? 'Aprovado'
                          : cut.decisionState === 'rejected'
                          ? 'Descartado'
                          : 'Aguardando Avaliação'}
                      </span>
                    </span>

                    <div className="flex items-center space-x-2">
                      <button
                        onClick={() => rejectCut(cut.id)}
                        className={`px-2.5 py-1 rounded text-xs transition-colors ${
                          cut.decisionState === 'rejected'
                            ? 'bg-zinc-800 text-zinc-300 border border-zinc-700'
                            : 'bg-studio-panel hover:bg-studio-border text-studio-muted hover:text-white'
                        }`}
                      >
                        Descartar
                      </button>
                      <button
                        onClick={() => acceptCut(cut.id)}
                        className={`px-3 py-1 rounded text-xs font-semibold transition-colors flex items-center space-x-1 ${
                          cut.decisionState === 'accepted'
                            ? 'bg-emerald-600 text-white shadow-sm'
                            : 'bg-indigo-600 hover:bg-indigo-500 text-white shadow-glow-indigo'
                        }`}
                      >
                        <Check className="w-3 h-3" />
                        <span>{cut.decisionState === 'accepted' ? 'Aprovado' : 'Aprovar'}</span>
                      </button>
                    </div>
                  </div>
                </div>
              );
            })}
          </div>
        </div>
      )}
    </div>
  );
};
