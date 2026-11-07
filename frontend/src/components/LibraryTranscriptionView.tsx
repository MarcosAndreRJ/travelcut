import React, { useState, useEffect, useCallback, useRef } from 'react';
import {
  FolderOpen,
  Search,
  Clock,
  Video,
  Calendar,
  Camera,
  AlertTriangle,
  CloudUpload,
  CheckCircle2,
  RefreshCw
} from 'lucide-react';
import {
  ProjectSummary,
  MediaAssetDto,
  ScanMediaResultDto,
  MediaDetailsDto,
  ChronologyReportDto,
  MediaAnalysisDto,
  AnalysisEnvironmentDto
} from '../types/ipc';
import { ipc } from '../services/ipcBridge';
import { MediaImportView } from './MediaImportView';
import { EnvironmentBanner } from './analysis/EnvironmentBanner';
import { PipelineQueuePanel } from './analysis/PipelineQueuePanel';
import { ClipAnalysisPanel } from './analysis/ClipAnalysisPanel';
import { ClipTimelineStrip } from './analysis/SignalsPanel';
import { usePipeline } from './analysis/usePipeline';
import { MediaClipPlayer } from './media/MediaClipPlayer';
import { isAudioAsset, isImageAsset, type ClipSeekRequest } from './media/mediaClipModel';
import { activitySummaryLine, pipelineActivityView } from './analysis/pipelineActivity';
import { errorMessage, formatDuration } from './analysis/analysisFormat';

/**
 * Modo de entrada da view: 'auto' abre 'import' se a biblioteca estiver vazia, senão 'clips'.
 * Uma mudança em entryKey reaplica o entryMode.
 */
export type LibraryEntryMode = 'auto' | 'import' | 'clips';

interface LibraryTranscriptionViewProps {
  project: ProjectSummary | null;
  onNavigateToTab?: (tab: any) => void;
  entryMode?: LibraryEntryMode;
  entryKey?: number;
  onLibraryChanged?: () => void;
  /** Chamado quando a fila do pipeline termina (ex.: para recarregar sugestões). */
  onAnalysisFinished?: () => void;
}

export const LibraryTranscriptionView: React.FC<LibraryTranscriptionViewProps> = ({
  project,
  entryMode = 'auto',
  entryKey,
  onLibraryChanged,
  onAnalysisFinished
}) => {
  const [activeTab, setActiveTab] = useState<'clips' | 'chronology'>('clips');
  const [viewMode, setViewMode] = useState<'clips' | 'import'>('clips');
  const autoResolveRef = useRef(false);
  const [assets, setAssets] = useState<MediaAssetDto[]>([]);
  const [selectedAsset, setSelectedAsset] = useState<MediaAssetDto | null>(null);
  const [selectedForAnalysis, setSelectedForAnalysis] = useState<Set<string>>(new Set());

  const toggleSelectForAnalysis = (mediaId: string, e?: React.MouseEvent) => {
    if (e) e.stopPropagation();
    setSelectedForAnalysis((prev) => {
      const next = new Set(prev);
      if (next.has(mediaId)) next.delete(mediaId);
      else next.add(mediaId);
      return next;
    });
  };

  const selectAllVisible = () => {
    setSelectedForAnalysis((prev) => {
      const next = new Set(prev);
      filteredAssets.forEach((a) => next.add(a.mediaId));
      return next;
    });
  };

  const deselectAll = () => {
    setSelectedForAnalysis(new Set());
  };
  const [selectedDetails, setSelectedDetails] = useState<MediaDetailsDto | null>(null);
  const [chronology, setChronology] = useState<ChronologyReportDto | null>(null);

  const [searchQuery, setSearchQuery] = useState('');
  const [isScanning, setIsScanning] = useState(false);
  const [isLoadingDetails, setIsLoadingDetails] = useState(false);
  const [scanResult, setScanResult] = useState<ScanMediaResultDto | null>(null);

  const [environment, setEnvironment] = useState<AnalysisEnvironmentDto | null>(null);
  const [environmentError, setEnvironmentError] = useState<string | null>(null);
  const [mediaListError, setMediaListError] = useState<string | null>(null);

  const [analysis, setAnalysis] = useState<MediaAnalysisDto | null>(null);
  const [isLoadingAnalysis, setIsLoadingAnalysis] = useState(false);
  const [analysisError, setAnalysisError] = useState<string | null>(null);
  const [analysisRefreshKey, setAnalysisRefreshKey] = useState(0);
  const [analysisRetryKey, setAnalysisRetryKey] = useState(0);
  // Tempo atual: vem do vídeo. Pedidos de busca são separados, para não formar laço vídeo ↔ tela.
  const [focusedTime, setFocusedTime] = useState<number | null>(null);
  const [seekRequest, setSeekRequest] = useState<ClipSeekRequest | null>(null);
  const [forceReprocess, setForceReprocess] = useState(false);

  // Sem projeto não há dados: nenhuma chamada ao host é feita e a view mostra o estado vazio.
  const projectId = project?.projectId ?? null;
  const selectedMediaId = selectedAsset?.mediaId ?? null;

  const loadMediaList = useCallback(async (): Promise<MediaAssetDto[] | null> => {
    if (!projectId) return null;
    try {
      const list = await ipc.listMedia(projectId);
      const safeList = Array.isArray(list) ? list : ((list as any)?.media ?? []);
      setAssets(safeList);
      setMediaListError(null);
      setSelectedAsset((prev) => prev ?? safeList[0] ?? null);
      if (autoResolveRef.current) {
        autoResolveRef.current = false;
        setViewMode(safeList.length === 0 ? 'import' : 'clips');
      }
      return safeList;
    } catch (err) {
      console.error('Falha ao listar mídias:', err);
      const msg = err instanceof Error ? err.message : String(err);
      setMediaListError(msg);
      return null;
    }
  }, [projectId]);

  const loadChronology = useCallback(async () => {
    if (!projectId) return;
    try {
      const rep = await ipc.getChronology(projectId);
      setChronology(rep);
    } catch (err) {
      console.error('Falha ao carregar cronologia:', err);
    }
  }, [projectId]);

  // Fim da fila: atualiza biblioteca, análise do clipe selecionado e avisa o App (sugestões).
  const handlePipelineFinished = useCallback(() => {
    void loadMediaList();
    setAnalysisRefreshKey((k) => k + 1);
    onAnalysisFinished?.();
  }, [loadMediaList, onAnalysisFinished]);

  const pipeline = usePipeline(projectId, handlePipelineFinished);

  // Modo de entrada: explícito ou 'auto' (decidido após carregar media.list).
  useEffect(() => {
    if (!projectId) return;
    if (entryMode === 'import') {
      setViewMode('import');
    } else if (entryMode === 'clips') {
      setViewMode('clips');
    } else {
      autoResolveRef.current = true;
    }
    void loadMediaList();
  }, [projectId, entryMode, entryKey, loadMediaList]);

  // Troca de projeto: descarta dados do projeto anterior e recarrega cronologia.
  useEffect(() => {
    setSelectedAsset(null);
    setSelectedDetails(null);
    setAssets([]);
    setChronology(null);
    setAnalysis(null);
    void loadChronology();
  }, [loadChronology]);

  // Ambiente de análise (FFmpeg/Whisper): consultado uma vez; o backend responde honestamente.
  useEffect(() => {
    let cancelled = false;
    ipc
      .getAnalysisEnvironment()
      .then((env) => {
        if (!cancelled) {
          setEnvironment(env);
          setEnvironmentError(null);
        }
      })
      .catch((err) => {
        if (!cancelled) setEnvironmentError(errorMessage(err));
      });
    return () => {
      cancelled = true;
    };
  }, []);

  useEffect(() => {
    if (!selectedMediaId) return;
    let isCancelled = false;
    setIsLoadingDetails(true);

    ipc.getMediaDetails(selectedMediaId)
      .then((details) => {
        if (!isCancelled) {
          setSelectedDetails(details);
          setIsLoadingDetails(false);
        }
      })
      .catch((err) => {
        console.error('Falha ao obter detalhes da mídia:', err);
        if (!isCancelled) setIsLoadingDetails(false);
      });

    return () => {
      isCancelled = true;
    };
  }, [selectedMediaId]);

  // Análise do clipe selecionado (analysis.getMedia). Troca de clipe ou fim de fila recarrega.
  useEffect(() => {
    setAnalysis(null);
    setAnalysisError(null);
    setFocusedTime(null);
    setSeekRequest(null);
    if (!projectId || !selectedMediaId) {
      setIsLoadingAnalysis(false);
      return;
    }
    let cancelled = false;
    setIsLoadingAnalysis(true);
    ipc
      .getMediaAnalysis(projectId, selectedMediaId)
      .then((res) => {
        if (!cancelled) setAnalysis(res);
      })
      .catch((err) => {
        if (!cancelled) setAnalysisError(errorMessage(err));
      })
      .finally(() => {
        if (!cancelled) setIsLoadingAnalysis(false);
      });
    return () => {
      cancelled = true;
    };
  }, [projectId, selectedMediaId, analysisRefreshKey, analysisRetryKey]);

  const requestSeek = useCallback((seconds: number) => {
    setFocusedTime(seconds);
    setSeekRequest((prev) => ({ seconds, token: (prev?.token ?? 0) + 1 }));
  }, []);

  const handleLibraryChanged = useCallback(() => {
    void loadMediaList();
    onLibraryChanged?.();
  }, [loadMediaList, onLibraryChanged]);

  const handleScanMedia = async () => {
    if (!projectId) return;
    setIsScanning(true);
    setScanResult(null);
    try {
      const res = await ipc.scanMedia(projectId);
      setScanResult(res);
      await loadMediaList();
      await loadChronology();
    } catch (err) {
      console.error('Erro no escaneamento de mídia:', err);
    } finally {
      setIsScanning(false);
    }
  };

  const safeAssets = Array.isArray(assets) ? assets : [];
  const filteredAssets = safeAssets.filter((asset) => {
    const q = searchQuery.toLowerCase();
    return (
      asset.relativePath.toLowerCase().includes(q) ||
      (asset.sceneClassification ?? '').toLowerCase().includes(q)
    );
  });

  const formatFileSize = (bytes: number) => {
    if (bytes >= 1024 * 1024 * 1024) {
      return `${(bytes / (1024 * 1024 * 1024)).toFixed(1)} GB`;
    }
    return `${(bytes / (1024 * 1024)).toFixed(0)} MB`;
  };

  const selectedMediaStages =
    pipeline.status?.media.find((m) => m.mediaId === selectedMediaId)?.stageStates ?? null;

  let analyzeDisabledReason: string | null = null;
  if (pipeline.isActive) analyzeDisabledReason = 'Já existe uma análise em andamento';

  if (!project) {
    return (
      <div className="flex-1 flex flex-col items-center justify-center gap-3 p-8 text-center bg-[#070d18] text-slate-300 font-sans">
        <FolderOpen className="w-10 h-10 text-slate-600" aria-hidden="true" />
        <h2 className="text-sm font-bold text-white">Nenhum projeto aberto</h2>
        <p className="text-xs text-slate-400 max-w-sm">
          Crie ou abra um projeto para ver a biblioteca de mídias e importar arquivos.
        </p>
      </div>
    );
  }

  if (viewMode === 'import') {
    return (
      <MediaImportView
        project={project}
        libraryHasMedia={assets.length > 0}
        onImportFinished={handleLibraryChanged}
        onOpenLibrary={() => setViewMode('clips')}
      />
    );
  }

  return (
    <div className="flex-1 flex flex-col h-full min-h-0 bg-[#070d18] text-slate-100 font-sans overflow-hidden">
      {/* Top Bar with Mode Switcher & Scan Action */}
      <div className="px-6 py-3 border-b border-slate-800 bg-[#0a1222] flex items-center justify-between shrink-0">
        <div className="flex items-center gap-4">
          <div className="flex items-center gap-2">
            <FolderOpen className="w-5 h-5 text-cyan-400" />
            <h2 className="text-sm font-bold text-white">Biblioteca & Metadados Ffprobe</h2>
            {ipc.isMock && (
              <span className="px-1.5 py-0.5 rounded bg-amber-500/15 border border-amber-500/40 text-amber-300 text-[9px] font-bold tracking-wider">
                DEMO
              </span>
            )}
          </div>

          <div className="flex items-center rounded-lg bg-slate-900 border border-slate-700/80 p-0.5 text-xs font-semibold">
            <button
              onClick={() => setActiveTab('clips')}
              className={`px-3 py-1 rounded-md transition-all ${
                activeTab === 'clips'
                  ? 'bg-cyan-600 text-white shadow-sm'
                  : 'text-slate-400 hover:text-slate-200'
              }`}
            >
              Clipes & Transcrição
            </button>
            <button
              onClick={() => setActiveTab('chronology')}
              className={`px-3 py-1 rounded-md transition-all flex items-center gap-1.5 ${
                activeTab === 'chronology'
                  ? 'bg-cyan-600 text-white shadow-sm'
                  : 'text-slate-400 hover:text-slate-200'
              }`}
            >
              <Calendar className="w-3.5 h-3.5" />
              Sessões & Cronologia (F04)
            </button>
          </div>

          <span className="text-xs text-slate-400 hidden sm:inline">
            {assets.length} arquivos catalogados • {project.name}
          </span>
        </div>

        {/* Scan & Batch Actions */}
        <div className="flex items-center gap-3">
          {scanResult && (
            <span className="text-xs text-emerald-400 font-medium">
              +{scanResult.newAssetsAdded} novos ({formatFileSize(scanResult.totalBytes)})
            </span>
          )}
          <button
            onClick={() => setViewMode('import')}
            className="px-3 py-1.5 rounded-xl bg-slate-800 hover:bg-slate-700 text-slate-200 text-xs font-medium flex items-center gap-1.5 border border-slate-700 transition-all"
          >
            <CloudUpload className="w-3.5 h-3.5 text-cyan-400" /> Importar mídia
          </button>
          <button
            onClick={handleScanMedia}
            disabled={isScanning}
            className="px-3 py-1.5 rounded-xl bg-slate-800 hover:bg-slate-700 text-slate-200 text-xs font-medium flex items-center gap-1.5 border border-slate-700 transition-all disabled:opacity-50"
          >
            <RefreshCw className={`w-3.5 h-3.5 text-cyan-400 ${isScanning ? 'animate-spin' : ''}`} />
            {isScanning ? 'Escaneando 1-Bruto...' : 'Escanear Bruto'}
          </button>
        </div>
      </div>

      {activeTab === 'chronology' ? (
        /* Chronology View (F04.5) */
        <div className="flex-1 overflow-y-auto p-6 space-y-6">
          <div className="flex items-center justify-between bg-[#0a1424] p-4 rounded-2xl border border-slate-800">
            <div>
              <h3 className="text-sm font-bold text-white flex items-center gap-2">
                <Calendar className="w-4 h-4 text-cyan-400" />
                Agrupamento Cronológico de Gravações por Sessão
              </h3>
              <p className="text-xs text-slate-400 mt-1">
                Clipes organizados com tolerância temporal automática de corte entre momentos do dia.
              </p>
            </div>
            <div className="flex items-center gap-4 text-xs font-mono">
              <span className="text-slate-400">
                Total Sessões: <strong className="text-cyan-400">{chronology?.sessions.length ?? 0}</strong>
              </span>
              <span className="text-slate-400">
                Total Clipes: <strong className="text-white">{chronology?.totalClips ?? 0}</strong>
              </span>
              <span className="text-slate-400">
                Anomalias: <strong className="text-emerald-400">{chronology?.anomaliesDetected ?? 0}</strong>
              </span>
            </div>
          </div>

          <div className="grid grid-cols-1 gap-4">
            {chronology?.sessions.map((session) => (
              <div
                key={session.sessionIndex}
                className="bg-[#080e1b] border border-slate-800 rounded-2xl p-5 space-y-4 hover:border-slate-700 transition-all"
              >
                <div className="flex items-center justify-between border-b border-slate-800 pb-3">
                  <div className="flex items-center gap-3">
                    <span className="px-2.5 py-1 rounded-lg text-xs font-bold bg-cyan-500/20 text-cyan-300 border border-cyan-500/30">
                      Sessão #{session.sessionIndex}
                    </span>
                    <h4 className="text-sm font-bold text-white">{session.name}</h4>
                  </div>
                  <div className="flex items-center gap-3 text-xs text-slate-400">
                    <span className="flex items-center gap-1">
                      <Clock className="w-3.5 h-3.5 text-cyan-400" />
                      {formatDuration(session.totalDurationSeconds)}
                    </span>
                    <span className="flex items-center gap-1">
                      <Camera className="w-3.5 h-3.5 text-amber-400" />
                      {session.cameraModels.join(', ') || 'Câmera Desconhecida'}
                    </span>
                  </div>
                </div>

                <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-3">
                  {session.clips.map((clip) => (
                    <div
                      key={clip.mediaId}
                      className="p-3 rounded-xl bg-[#0a1424] border border-slate-800/80 flex flex-col justify-between"
                    >
                      <div className="flex items-start justify-between gap-2">
                        <div className="min-w-0">
                          <p className="text-xs font-bold text-white truncate" title={clip.relativePath}>
                            {clip.relativePath.split(/[/\\]/).pop()}
                          </p>
                          <p className="text-[10px] text-slate-400 mt-0.5 truncate">
                            {clip.relativePath}
                          </p>
                        </div>
                        <span className="px-1.5 py-0.5 rounded text-[9px] font-mono font-bold bg-slate-800 text-slate-300 uppercase shrink-0">
                          {clip.kind}
                        </span>
                      </div>

                      <div className="mt-3 pt-2 border-t border-slate-800 flex items-center justify-between text-[10px] text-slate-400">
                        <span className="font-mono">
                          {new Date(clip.recordedAtUtc).toLocaleTimeString()}
                        </span>
                        <span className="font-mono text-cyan-400">
                          {formatDuration(clip.durationSeconds)}
                        </span>
                        <span className="text-[9px] px-1.5 py-0.5 rounded bg-cyan-500/10 text-cyan-300">
                          {clip.confidence}
                        </span>
                      </div>
                    </div>
                  ))}
                </div>
              </div>
            ))}
          </div>
        </div>
      ) : (
        /* Clips & Transcription View */
        <>
          <EnvironmentBanner environment={environment} error={environmentError} />
          {mediaListError && (
            <div role="alert" className="mx-6 mt-3 p-3 rounded-xl bg-red-950/50 border border-red-800/80 text-red-200 text-xs flex items-center justify-between gap-3">
              <span className="flex-1">{mediaListError}</span>
              <button
                type="button"
                onClick={() => void loadMediaList()}
                className="px-3 py-1 bg-red-900/60 hover:bg-red-800 text-white rounded-lg text-xs font-medium transition shrink-0"
              >
                Tentar novamente
              </button>
            </div>
          )}

          <div className="flex-1 flex min-h-0 overflow-hidden">
            {/* Left Panel: Media Clip List */}
            <div className="w-72 lg:w-96 border-r border-slate-800 bg-[#080e1b] flex flex-col shrink-0 min-h-0">
              <div className="p-4 border-b border-slate-800/80">
                <div className="relative">
                  <Search className="w-4 h-4 text-slate-400 absolute left-3 top-2.5" aria-hidden="true" />
                  <input
                    type="text"
                    aria-label="Buscar arquivos ou classificação"
                    placeholder="Buscar arquivos ou classificação..."
                    value={searchQuery}
                    onChange={(e) => setSearchQuery(e.target.value)}
                    className="w-full pl-9 pr-3 py-2 bg-[#0a1424] border border-slate-700/80 rounded-xl text-xs text-white placeholder-slate-500 focus:outline-none focus:border-cyan-500"
                  />
                </div>
              </div>
              {/* Barra de seleção múltipla para análise */}
              <div className="px-3 py-2 bg-[#08101e] border-b border-slate-800 flex items-center justify-between text-xs shrink-0 select-none">
                <label className="flex items-center gap-2 cursor-pointer text-slate-300">
                  <input
                    type="checkbox"
                    checked={filteredAssets.length > 0 && filteredAssets.every((a) => selectedForAnalysis.has(a.mediaId))}
                    onChange={(e) => (e.target.checked ? selectAllVisible() : deselectAll())}
                    className="w-3.5 h-3.5 rounded border-slate-700 bg-slate-900 text-cyan-500 focus:ring-cyan-500 cursor-pointer accent-cyan-500"
                  />
                  <span className="font-medium text-[11px]">
                    {selectedForAnalysis.size === 0
                      ? "Nenhum vídeo marcado"
                      : `${selectedForAnalysis.size} de ${assets.length} marcado${selectedForAnalysis.size > 1 ? "s" : ""}`}
                  </span>
                </label>
                {selectedForAnalysis.size > 0 && (
                  <div className="flex items-center gap-2">
                    <button
                      type="button"
                      onClick={deselectAll}
                      className="text-[10px] text-slate-400 hover:text-white underline cursor-pointer"
                    >
                      Desmarcar
                    </button>
                    <button
                      type="button"
                      onClick={() => {
                        void pipeline.startAnalysis({ mediaIds: Array.from(selectedForAnalysis), force: forceReprocess });
                      }}
                      disabled={pipeline.isActive || pipeline.isStarting}
                      className="px-2 py-1 rounded-lg bg-cyan-600 hover:bg-cyan-500 text-white text-[10px] font-semibold flex items-center gap-1 shadow-sm transition disabled:opacity-50 cursor-pointer"
                      title={`Analisar apenas os ${selectedForAnalysis.size} vídeos selecionados`}
                    >
                      Analisar ({selectedForAnalysis.size})
                    </button>
                  </div>
                )}
              </div>

              <div className="flex-1 overflow-y-auto p-3 space-y-2">
                {assets.length === 0 && (
                  <div className="text-xs text-slate-500 text-center py-8 px-2">
                    Biblioteca vazia. Use "Importar mídia" para adicionar arquivos ao projeto.
                  </div>
                )}
                {assets.length > 0 && filteredAssets.length === 0 && (
                  <div className="text-xs text-slate-500 text-center py-8 px-2">Nenhum arquivo corresponde à busca.</div>
                )}
                {filteredAssets.map((asset) => {
                  const isSelected = selectedMediaId === asset.mediaId;
                  const isBatchSelected = selectedForAnalysis.has(asset.mediaId);
                  const fileName = asset.relativePath.split(/[/\\]/).pop() || asset.relativePath;

                  return (
                    <div
                      key={asset.mediaId}
                      onClick={(e) => { if (e.ctrlKey || e.metaKey) { toggleSelectForAnalysis(asset.mediaId, e); } else { setSelectedAsset(asset); } }}
                      className={`p-3 rounded-2xl cursor-pointer transition-all border ${
                        isSelected
                          ? 'bg-[#0d1d36] border-cyan-500 shadow-md shadow-cyan-950'
                          : isBatchSelected
                            ? 'bg-[#0b172a] border-cyan-500/60 ring-1 ring-cyan-500/40'
                            : 'bg-[#0a1222] border-slate-800/80 hover:border-slate-700'
                      }`}
                    >
                      <div className="flex gap-3">
                        <div className="w-20 h-14 rounded-lg bg-slate-900 border border-slate-800 overflow-hidden relative shrink-0 flex items-center justify-center">
                          {asset.thumbnailUrl ? (
                            <img src={asset.thumbnailUrl} alt={fileName} className="w-full h-full object-cover" />
                          ) : (
                            <Video className="w-6 h-6 text-slate-600" />
                          )}
                          <span className="absolute bottom-1 right-1 px-1 py-0.5 rounded text-[9px] font-mono bg-black/80 text-white font-bold">
                            {formatDuration(asset.durationSeconds)}
                          </span>
                        </div>

                        <div className="flex-1 min-w-0">
                          <div className="flex items-center gap-2">
                            <input
                              type="checkbox"
                              aria-label={`Marcar ${fileName} para análise`}
                              checked={isBatchSelected}
                              onChange={() => toggleSelectForAnalysis(asset.mediaId)}
                              onClick={(e) => e.stopPropagation()}
                              className="w-3.5 h-3.5 rounded border-slate-700 bg-slate-900 text-cyan-500 focus:ring-cyan-500 cursor-pointer accent-cyan-500 shrink-0"
                            />
                            <div className="text-xs font-bold text-white truncate flex-1" title={fileName}>
                              {fileName}
                            </div>
                          </div>
                          <div className="text-[10px] text-slate-400 mt-1 flex items-center gap-2 min-w-0">
                            <span>{asset.width > 0 ? `${asset.width}x${asset.height}` : asset.codec}</span>
                            {asset.fps > 0 && <span>{asset.fps.toFixed(0)} fps</span>}
                          </div>

                          <div className="mt-2 flex flex-wrap items-center gap-1.5">
                            {asset.hasTranscript && (
                              <span className="inline-flex items-center gap-1 text-[10px] text-emerald-400 font-semibold">
                                <CheckCircle2 className="w-3 h-3" aria-hidden="true" /> Transcrito
                              </span>
                            )}
                            {asset.hasVAD && (
                              <span className="text-[10px] text-amber-300 font-semibold px-1 rounded bg-amber-500/10">
                                Silêncios
                              </span>
                            )}
                            {asset.sceneClassification && (
                              <span className="text-[10px] text-cyan-300 px-1 rounded bg-cyan-500/10 truncate max-w-full">
                                {asset.sceneClassification}
                              </span>
                            )}
                            <span className="ml-auto text-[10px] text-slate-500 font-mono">
                              {formatFileSize(asset.byteLength)}
                            </span>
                          </div>
                        </div>
                      </div>
                    </div>
                  );
                })}
              </div>
            </div>

            {/* Right Panel: player, timeline and clip analysis */}
            <div className="flex-1 min-w-0 flex flex-col min-h-0 bg-[#070d18]">
              <div className="shrink-0 p-4 space-y-3 border-b border-slate-800 bg-[#050912]">
                {selectedAsset && projectId && !isImageAsset(selectedAsset) ? (
                  <MediaClipPlayer
                    key={`${projectId}:${selectedAsset.mediaId}`}
                    projectId={projectId}
                    mediaId={selectedAsset.mediaId}
                    posterUrl={selectedAsset.thumbnailUrl ?? null}
                    isVideo={!isAudioAsset(selectedAsset)}
                    durationHint={selectedAsset.durationSeconds}
                    seekRequest={seekRequest}
                    onTimeUpdate={setFocusedTime}
                    badges={
                      <>
                        <span className="px-2 py-0.5 rounded text-[10px] font-bold uppercase bg-slate-900/80 backdrop-blur border border-slate-700 text-slate-300">
                          {selectedAsset.codec || 'Desconhecido'}
                        </span>
                        {selectedDetails?.isVariableFrameRate && (
                          <span className="px-2 py-0.5 rounded text-[10px] font-bold uppercase bg-amber-500/20 border border-amber-500/40 text-amber-300 flex items-center gap-1">
                            <AlertTriangle className="w-3 h-3" aria-hidden="true" /> VFR
                          </span>
                        )}
                        {selectedDetails?.sourceDateEvidence && (
                          <span className="px-2 py-0.5 rounded text-[10px] font-bold bg-cyan-950/80 backdrop-blur border border-cyan-800 text-cyan-300 font-mono">
                            {selectedDetails.sourceDateEvidence}
                          </span>
                        )}
                      </>
                    }
                  />
                ) : (
                  <div className="relative h-36 xl:h-52 rounded-2xl overflow-hidden bg-black flex items-center justify-center border border-slate-800">
                    {selectedAsset?.thumbnailUrl ? (
                      <img src={selectedAsset.thumbnailUrl} alt="Pré-visualização do clipe" className="w-full h-full object-contain" />
                    ) : (
                      <div className="flex flex-col items-center gap-2 text-slate-600">
                        <Video className="w-10 h-10" aria-hidden="true" />
                        <span className="text-xs font-mono">{selectedAsset ? 'Sem visualização' : 'Selecione um clipe'}</span>
                      </div>
                    )}
                  </div>
                )}

                <ClipTimelineStrip
                  durationSeconds={selectedAsset?.durationSeconds ?? 0}
                  silences={analysis?.silenceStatus === 'completed' ? analysis.silences : []}
                  hesitations={analysis?.hesitations ?? []}
                  selectedSec={focusedTime}
                  onSeek={requestSeek}
                />

                <div className="flex items-center justify-between text-[11px] text-slate-400 font-mono">
                  <span>
                    Duração: {formatDuration(selectedAsset?.durationSeconds)}
                    <span className="ml-3 text-amber-300/80">■ silêncios</span>
                    <span className="ml-2 text-rose-300/80">■ hesitações</span>
                  </span>
                  <span className="flex items-center gap-3">
                    {isLoadingDetails && <span className="text-slate-500">Carregando metadados…</span>}
                    {selectedDetails && (
                      <>
                        <span>{selectedDetails.width}x{selectedDetails.height}</span>
                        <span>{selectedDetails.frameRate} fps</span>
                        <span>{selectedDetails.audioChannels}ch @ {selectedDetails.audioSampleRate}Hz</span>
                      </>
                    )}
                  </span>
                </div>
              </div>

              <div className="flex-1 min-h-0 flex flex-col bg-[#080d19]">
                <div className="px-4 py-2 border-b border-slate-800 bg-[#0a1222] shrink-0">
                  <span className="text-xs font-bold text-white">Transcrição e análise por clipe</span>
                  <span className="ml-2 text-[10px] text-slate-400">Dados reais do host; sem resultado simulado.</span>
                </div>

                {selectedAsset ? (
                  <ClipAnalysisPanel
                    key={selectedAsset.mediaId}
                    asset={selectedAsset}
                    analysis={analysis}
                    isLoading={isLoadingAnalysis}
                    error={analysisError}
                    mediaStageStates={selectedMediaStages}
                    selectedSec={focusedTime}
                    onSeek={requestSeek}
                    onAnalyze={() => {
                      void pipeline.startAnalysis({ mediaIds: [selectedAsset.mediaId], force: false });
                    }}
                    onRetry={() => setAnalysisRetryKey((k) => k + 1)}
                    analyzeDisabledReason={analyzeDisabledReason}
                    isStarting={pipeline.isStarting}
                    isMock={ipc.isMock}
                    activityLine={
                      pipeline.status?.currentMedia === selectedAsset.relativePath
                        ? activitySummaryLine(pipelineActivityView(pipeline.status, Date.now(), pipeline.isCanceling))
                        : null
                    }
                  />
                ) : (
                  <div className="flex-1 flex items-center justify-center text-xs text-slate-500">
                    Selecione um clipe para ver a análise.
                  </div>
                )}
              </div>
            </div>
          </div>

          <PipelineQueuePanel
            projectId={projectId}
            status={pipeline.status}
            statusError={pipeline.statusError}
            actionError={pipeline.actionError}
            isStarting={pipeline.isStarting}
            isCanceling={pipeline.isCanceling}
            hasMedia={assets.length > 0}
            isMock={ipc.isMock}
            force={forceReprocess}
            onForceChange={setForceReprocess}
            selectedCount={selectedForAnalysis.size}
            totalMediaCount={assets.length}
            onStartSelected={() => {
              void pipeline.startAnalysis({ mediaIds: Array.from(selectedForAnalysis), force: forceReprocess });
            }}
            onStart={() => {
              void pipeline.startAnalysis({ mediaIds: null, force: forceReprocess });
            }}
            onCancel={() => {
              void pipeline.cancelAnalysis();
            }}
            onRetry={pipeline.refresh}
          />
        </>
      )}
    </div>
  );
};
