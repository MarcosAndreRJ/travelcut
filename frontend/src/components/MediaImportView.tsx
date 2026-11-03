import React, { useEffect, useMemo, useRef, useState } from 'react';
import {
  AlertTriangle,
  CircleCheck,
  CircleAlert,
  CloudUpload,
  FileVideo,
  FolderOpen,
  Image as ImageIcon,
  Info,
  Library,
  Video,
  X,
  type LucideIcon
} from 'lucide-react';
import { ipc } from '../services/ipcBridge';
import type {
  ImportDestination,
  ImportJobStatusDto,
  ProjectSummary,
  StartImportRequest
} from '../types/ipc';
import { FileNavigator } from './import/FileNavigator';
import { ImportFileTable } from './import/ImportFileTable';
import { ImportProgressOverlay } from './import/ImportProgressOverlay';
import { DestinationCard, OptionsCard, SummaryCard, TagsCard } from './import/ImportSidePanels';
import {
  appendCandidates,
  basenameOf,
  DEFAULT_IMPORT_OPTIONS,
  dirnameOf,
  formatBytes,
  formatDurationLong,
  ImportOptions,
  ImportRow,
  isSuccessfulResult,
  isTerminalJobState,
  joinPath,
  messageOf,
  pathKey,
  pluralItems,
  summarizeItems
} from './import/importUtils';

const POLL_INTERVAL_MS = 700;

type SourceTab = 'videos' | 'assets' | 'folder';

const SOURCE_TABS: ReadonlyArray<{ id: SourceTab; label: string; icon: LucideIcon }> = [
  { id: 'videos', label: 'Vídeos brutos', icon: Video },
  { id: 'assets', label: 'Assets (músicas, imagens, etc.)', icon: ImageIcon },
  { id: 'folder', label: 'Importar de pasta', icon: FolderOpen }
];

type NoticeTone = 'info' | 'warn' | 'error' | 'success';

interface Notice {
  tone: NoticeTone;
  text: string;
  showLibraryAction?: boolean;
}

const NOTICE_STYLE: Record<NoticeTone, { box: string; icon: LucideIcon }> = {
  info: { box: 'bg-cyan-500/10 border-cyan-500/30 text-cyan-100', icon: Info },
  warn: { box: 'bg-amber-500/10 border-amber-500/30 text-amber-100', icon: AlertTriangle },
  error: { box: 'bg-rose-500/10 border-rose-500/30 text-rose-100', icon: CircleAlert },
  success: { box: 'bg-emerald-500/10 border-emerald-500/30 text-emerald-100', icon: CircleCheck }
};

export interface MediaImportViewProps {
  project: ProjectSummary;
  libraryHasMedia: boolean;
  /** Chamado quando um job de importação chega a estado terminal. */
  onImportFinished: () => void;
  /** Volta para a biblioteca (modo clips). */
  onOpenLibrary: () => void;
}

export const MediaImportView: React.FC<MediaImportViewProps> = ({
  project,
  libraryHasMedia,
  onImportFinished,
  onOpenLibrary
}) => {
  const projectId = project.projectId;
  const isMock = ipc.isMock;

  const [source, setSource] = useState<SourceTab>('videos');
  const [destination, setDestination] = useState<ImportDestination>('1-Bruto');
  const [options, setOptions] = useState<ImportOptions>(DEFAULT_IMPORT_OPTIONS);
  const [tags, setTags] = useState<string[]>([]);
  const [rows, setRows] = useState<ImportRow[]>([]);
  const [pendingInspections, setPendingInspections] = useState(0);
  const [ffprobeAvailable, setFfprobeAvailable] = useState<boolean | null>(null);
  const [notice, setNotice] = useState<Notice | null>(null);
  const [isDragOver, setIsDragOver] = useState(false);

  const [jobId, setJobId] = useState<string | null>(null);
  const [job, setJob] = useState<ImportJobStatusDto | null>(null);
  const [overlayError, setOverlayError] = useState<string | null>(null);
  const [cancelRequested, setCancelRequested] = useState(false);
  const [isStarting, setIsStarting] = useState(false);

  // Mantém a referência mais recente do callback sem reiniciar o polling.
  const onImportFinishedRef = useRef(onImportFinished);
  onImportFinishedRef.current = onImportFinished;

  const selectedRows = useMemo(() => rows.filter((row) => row.selected), [rows]);
  const selectedItems = useMemo(() => selectedRows.map((row) => row.item), [selectedRows]);
  const summary = useMemo(() => summarizeItems(selectedItems), [selectedItems]);
  const selectedHasKnownDuration = summary.hasKnownDuration;
  const destinationPath = joinPath(project.rootPath, destination);
  const overlayVisible = jobId !== null;
  const jobIsRunning = overlayVisible && !(job !== null && isTerminalJobState(job.state));

  // Polling do status de importação até estado terminal (ou erro de consulta).
  useEffect(() => {
    if (jobId === null) return;
    let active = true;
    let timer: ReturnType<typeof setTimeout> | undefined;

    const poll = async (): Promise<void> => {
      try {
        const status = await ipc.getImportStatus(jobId);
        if (!active) return;
        setJob(status);
        if (isTerminalJobState(status.state)) {
          const successKeys = new Set(
            status.items.filter((item) => isSuccessfulResult(item.result)).map((item) => pathKey(item.sourcePath))
          );
          setRows((prev) =>
            prev.map((row) =>
              successKeys.has(row.key) && !row.item.alreadyInLibrary
                ? { ...row, item: { ...row.item, alreadyInLibrary: true } }
                : row
            )
          );
          const registered = status.items.filter((item) => isSuccessfulResult(item.result)).length;
          const failed = status.items.filter((item) => item.result === 'failed').length;
          const text =
            status.state === 'failed'
              ? 'A importação falhou. Veja a mensagem do backend no painel de progresso.'
              : `Importação finalizada: ${registered} de ${status.totalFiles} itens registrados${
                  failed > 0 ? `, ${failed} com falha` : ''
                }.`;
          setNotice({
            tone: status.state === 'completed' ? 'success' : status.state === 'canceled' ? 'info' : 'warn',
            text,
            showLibraryAction: true
          });
          onImportFinishedRef.current();
          return;
        }
        timer = setTimeout(() => {
          void poll();
        }, POLL_INTERVAL_MS);
      } catch (err) {
        if (active) {
          setOverlayError(
            `Não foi possível consultar o andamento: ${messageOf(err)}. A importação pode continuar no host; atualize a biblioteca depois.`
          );
        }
      }
    };

    void poll();
    return () => {
      active = false;
      if (timer !== undefined) clearTimeout(timer);
    };
  }, [jobId]);

  const addPaths = async (incoming: string[]): Promise<void> => {
    const unique: string[] = [];
    const seen = new Set<string>();
    for (const raw of incoming) {
      const path = raw.trim();
      if (!path) continue;
      const key = pathKey(path);
      if (seen.has(key)) continue;
      seen.add(key);
      unique.push(path);
    }
    if (unique.length === 0) {
      setNotice({ tone: 'warn', text: 'Nenhum caminho válido foi recebido.' });
      return;
    }
    const existing = new Set(rows.map((row) => row.key));
    const fresh = unique.filter((path) => !existing.has(pathKey(path)));
    if (fresh.length === 0) {
      setNotice({ tone: 'info', text: 'Esses itens já estão na lista.' });
      return;
    }

    setPendingInspections((count) => count + 1);
    setNotice(null);
    try {
      const res = await ipc.inspectCandidates(projectId, fresh);
      setFfprobeAvailable(res.ffprobeAvailable);
      setRows((prev) => appendCandidates(prev, res.items ?? []));

      const parts: string[] = [];
      if (res.items.length === 0) parts.push('Nenhum arquivo de mídia reconhecido nesse caminho.');
      const errors = res.errors ?? [];
      if (errors.length > 0) {
        const head = errors.slice(0, 3).join(' ');
        parts.push(errors.length > 3 ? `${head} (mais ${errors.length - 3} erro(s))` : head);
      }
      if (res.truncated) {
        parts.push('A lista foi truncada pelo limite do host. Adicione subpastas para completar.');
      }
      if (parts.length > 0) setNotice({ tone: 'warn', text: parts.join(' ') });
    } catch (err) {
      setNotice({ tone: 'error', text: `Falha ao analisar os arquivos: ${messageOf(err)}` });
    } finally {
      setPendingInspections((count) => count - 1);
    }
  };

  const chooseFiles = async (): Promise<void> => {
    try {
      const res = await ipc.selectFiles(source === 'assets' ? 'assets' : 'video');
      if (!res.canceled && res.paths?.length) await addPaths(res.paths);
    } catch (err) {
      setNotice({ tone: 'error', text: `Não foi possível abrir a seleção de arquivos: ${messageOf(err)}` });
    }
  };

  const chooseFolder = async (): Promise<void> => {
    try {
      const res = await ipc.selectFolder();
      if (!res.canceled && res.selectedPath) await addPaths([res.selectedPath]);
    } catch (err) {
      setNotice({ tone: 'error', text: `Não foi possível abrir a seleção de pasta: ${messageOf(err)}` });
    }
  };

  const handleSelectSource = (tab: SourceTab) => {
    setSource(tab);
    if (tab === 'videos') setDestination('1-Bruto');
    if (tab === 'assets') setDestination('2-Assets');
    if (tab === 'folder') void chooseFolder();
  };

  const handleDrop = async (event: React.DragEvent<HTMLDivElement>) => {
    event.preventDefault();
    setIsDragOver(false);
    // Captura os File sincronamente: a lista de arquivos some após o evento.
    const files = Array.from(event.dataTransfer?.files ?? []);
    if (files.length === 0) return;
    try {
      const paths = await ipc.resolveDroppedPaths(files);
      if (paths.length === 0) {
        setNotice({
          tone: 'warn',
          text: 'O host não devolveu os caminhos dos arquivos arrastados. Use "Adicionar arquivos" ou "Adicionar pasta".'
        });
        return;
      }
      await addPaths(paths);
    } catch (err) {
      setNotice({ tone: 'error', text: `Falha ao resolver os arquivos arrastados: ${messageOf(err)}` });
    }
  };

  const openFolderOf = (fullPath: string) => {
    ipc.openFolder(dirnameOf(fullPath)).catch((err: unknown) => {
      setNotice({ tone: 'error', text: `Não foi possível abrir a pasta: ${messageOf(err)}` });
    });
  };

  const addTags = (raw: string) => {
    const parts = raw
      .split(',')
      .map((part) => part.trim())
      .filter(Boolean);
    if (parts.length === 0) return;
    setTags((prev) => {
      const seen = new Set(prev.map((tag) => tag.toLowerCase()));
      const next = [...prev];
      for (const part of parts) {
        const key = part.toLowerCase();
        if (seen.has(key)) continue;
        seen.add(key);
        next.push(part);
      }
      return next;
    });
  };

  const startImport = async () => {
    if (selectedItems.length === 0 || isStarting || overlayVisible) return;
    const request: StartImportRequest = {
      projectId,
      paths: selectedItems.map((item) => item.fullPath),
      destination,
      subfolder: null,
      copyFiles: options.copyFiles,
      generateThumbnails: options.generateThumbnails,
      analyzeMetadata: options.analyzeMetadata,
      detectSpeech: options.detectSpeech,
      tags
    };
    setIsStarting(true);
    setNotice(null);
    try {
      const res = await ipc.startImport(request);
      setJob(null);
      setOverlayError(null);
      setCancelRequested(false);
      setJobId(res.jobId);
    } catch (err) {
      setNotice({ tone: 'error', text: `Não foi possível iniciar a importação: ${messageOf(err)}` });
    } finally {
      setIsStarting(false);
    }
  };

  const cancelImport = async () => {
    if (jobId === null || cancelRequested) return;
    try {
      await ipc.cancelImport(jobId);
      setCancelRequested(true);
    } catch (err) {
      setOverlayError(`Não foi possível cancelar a importação: ${messageOf(err)}`);
    }
  };

  const closeOverlay = () => {
    if (jobIsRunning) return;
    setJobId(null);
    setJob(null);
    setOverlayError(null);
    setCancelRequested(false);
  };

  const handleOpenLibraryFromOverlay = () => {
    closeOverlay();
    onOpenLibrary();
  };

  const handleCancelButton = () => {
    if (libraryHasMedia) {
      onOpenLibrary();
      return;
    }
    setRows([]);
    setNotice(null);
  };

  const updateOption = (key: keyof ImportOptions, value: boolean) => {
    setOptions((prev) => ({ ...prev, [key]: value }));
  };

  const NoticeIcon: LucideIcon = notice ? NOTICE_STYLE[notice.tone].icon : Info;

  const durationFooter = selectedItems.length === 0
    ? '—'
    : selectedHasKnownDuration
      ? formatDurationLong(summary.durationSecondsTotal)
      : '—';

  return (
    <div className="flex-1 flex flex-col h-full min-h-0 bg-[#070d18] text-slate-100 font-sans overflow-hidden">
      <div className="flex-1 min-h-0 overflow-y-auto px-6 pt-4 pb-5 space-y-4">
        {/* Cabeçalho */}
        <header className="flex flex-col xl:flex-row xl:items-start justify-between gap-4">
          <div className="min-w-0">
            <p className="text-[11px] text-slate-400 truncate" aria-label="Trilha de navegação">
              {project.name} / Mídia
            </p>
            <div className="flex items-center gap-2 flex-wrap mt-0.5">
              <h1 className="text-xl font-bold text-white">Importar mídia para o projeto</h1>
              {isMock && (
                <span className="px-2 py-0.5 rounded bg-amber-500/15 border border-amber-500/40 text-amber-300 text-[10px] font-bold tracking-wider">
                  DEMONSTRAÇÃO
                </span>
              )}
            </div>
            <p className="text-xs text-slate-400 mt-1">
              Adicione vídeos, arquivos de apoio e assets para começar a edição da sua viagem.
            </p>
          </div>
          <div className="flex items-center gap-3 px-4 py-2.5 rounded-2xl bg-[#0a1222] border border-slate-800 min-w-0 xl:max-w-sm w-full xl:w-auto">
            <FolderOpen className="w-5 h-5 text-amber-400 shrink-0" aria-hidden="true" />
            <div className="min-w-0">
              <p className="text-xs font-bold text-white truncate">Projeto: {project.name}</p>
              <p className="text-[10px] text-slate-400 font-mono truncate" title={project.rootPath}>
                {project.rootPath}
              </p>
            </div>
          </div>
        </header>

        {/* Abas de origem */}
        <nav aria-label="Origem da importação" className="flex flex-wrap gap-2">
          {SOURCE_TABS.map((tab) => {
            const Icon = tab.icon;
            const active = source === tab.id;
            return (
              <button
                key={tab.id}
                type="button"
                aria-pressed={active}
                onClick={() => handleSelectSource(tab.id)}
                className={`px-3.5 py-2 rounded-xl text-xs font-semibold flex items-center gap-2 border transition-all focus-visible:ring-2 focus-visible:ring-cyan-400 outline-none ${
                  active
                    ? 'bg-[#0d2a4a] border-cyan-500/70 text-cyan-100 shadow-md shadow-cyan-950'
                    : 'bg-[#0a1222] border-slate-800 text-slate-300 hover:border-slate-600 hover:text-white'
                }`}
              >
                <Icon className={`w-4 h-4 ${active ? 'text-cyan-300' : 'text-slate-400'}`} aria-hidden="true" />
                {tab.label}
              </button>
            );
          })}
        </nav>

        {/* Avisos */}
        {ffprobeAvailable === false && (
          <div role="alert" className="flex items-start gap-2 px-3 py-2 rounded-xl bg-amber-500/10 border border-amber-500/40 text-xs text-amber-100">
            <AlertTriangle className="w-4 h-4 mt-0.5 shrink-0 text-amber-400" aria-hidden="true" />
            <span>
              FFprobe não encontrado — duração e resolução ficarão indisponíveis até configurar o FFmpeg em Configurações.
            </span>
          </div>
        )}
        {notice && (
          <div
            role="status"
            className={`flex items-start gap-2 px-3 py-2 rounded-xl border text-xs ${NOTICE_STYLE[notice.tone].box}`}
          >
            <NoticeIcon className="w-4 h-4 mt-0.5 shrink-0" aria-hidden="true" />
            <span className="flex-1 min-w-0">{notice.text}</span>
            {notice.showLibraryAction && (
              <button
                type="button"
                onClick={onOpenLibrary}
                className="shrink-0 px-2.5 py-1 rounded-lg bg-emerald-600/80 hover:bg-emerald-500 text-white font-semibold flex items-center gap-1.5 focus-visible:ring-2 focus-visible:ring-cyan-400 outline-none"
              >
                <Library className="w-3.5 h-3.5" aria-hidden="true" /> Ver biblioteca
              </button>
            )}
            <button
              type="button"
              onClick={() => setNotice(null)}
              aria-label="Fechar aviso"
              className="shrink-0 p-0.5 rounded hover:bg-white/10 focus-visible:ring-2 focus-visible:ring-cyan-400 outline-none"
            >
              <X className="w-3.5 h-3.5" aria-hidden="true" />
            </button>
          </div>
        )}

        {/* Corpo em três colunas (empilha em telas menores) */}
        <div className="grid gap-4 xl:grid-cols-[220px_minmax(0,1fr)_260px] 2xl:grid-cols-[260px_minmax(0,1fr)_300px] items-start">
          {/* Navegador */}
          <section
            aria-labelledby="import-navigator-title"
            className="rounded-2xl bg-[#0a1222] border border-slate-800 p-3 flex flex-col xl:h-[600px] min-h-[360px]"
          >
            <h2 id="import-navigator-title" className="text-sm font-bold text-white mb-2 px-1">
              Navegar por arquivos
            </h2>
            <div className="flex-1 min-h-0">
              <FileNavigator rootPath={project.rootPath} onAddFile={(path) => void addPaths([path])} onAddFolder={(path) => void addPaths([path])} />
            </div>
          </section>

          {/* Zona de soltar + tabela */}
          <div className="flex flex-col gap-4 min-w-0">
            <div
              onDragEnter={(event) => {
                event.preventDefault();
                setIsDragOver(true);
              }}
              onDragOver={(event) => {
                event.preventDefault();
                event.dataTransfer.dropEffect = 'copy';
                if (!isDragOver) setIsDragOver(true);
              }}
              onDragLeave={(event) => {
                if (!event.currentTarget.contains(event.relatedTarget as Node | null)) setIsDragOver(false);
              }}
              onDrop={(event) => void handleDrop(event)}
              className={`relative rounded-2xl border-2 border-dashed px-6 py-8 text-center transition-all overflow-hidden ${
                isDragOver
                  ? 'border-cyan-400 bg-cyan-500/10'
                  : 'border-cyan-700/60 bg-gradient-to-b from-[#0b1a2e] to-[#0a1222]'
              }`}
            >
              <CloudUpload className="w-12 h-12 mx-auto text-cyan-400 drop-shadow-[0_0_12px_rgba(34,211,238,0.45)]" aria-hidden="true" />
              <p className="mt-3 text-sm font-bold text-white">Arraste e solte seus arquivos de mídia aqui</p>
              <p className="text-xs text-slate-400 mt-1">
                Ou use os botões abaixo para adicionar arquivos ou uma pasta inteira.
              </p>
              <div className="mt-4 flex flex-wrap items-center justify-center gap-3">
                <button
                  type="button"
                  onClick={() => void chooseFiles()}
                  className="px-4 py-2 rounded-xl bg-[#0d2a4a] hover:bg-[#123459] border border-cyan-600/60 text-cyan-100 text-xs font-semibold flex items-center gap-2 focus-visible:ring-2 focus-visible:ring-cyan-400 outline-none"
                >
                  <FileVideo className="w-4 h-4" aria-hidden="true" /> Adicionar arquivos
                </button>
                <button
                  type="button"
                  onClick={() => void chooseFolder()}
                  className="px-4 py-2 rounded-xl bg-slate-800 hover:bg-slate-700 border border-slate-600 text-slate-100 text-xs font-semibold flex items-center gap-2 focus-visible:ring-2 focus-visible:ring-cyan-400 outline-none"
                >
                  <FolderOpen className="w-4 h-4 text-amber-400" aria-hidden="true" /> Adicionar pasta
                </button>
              </div>
            </div>

            <ImportFileTable
              projectId={projectId}
              rows={rows}
              isInspecting={pendingInspections > 0}
              onToggleRow={(key) =>
                setRows((prev) => prev.map((row) => (row.key === key ? { ...row, selected: !row.selected } : row)))
              }
              onToggleAll={(checked) => setRows((prev) => prev.map((row) => ({ ...row, selected: checked })))}
              onRemoveRow={(key) => setRows((prev) => prev.filter((row) => row.key !== key))}
              onRemoveSelected={() => setRows((prev) => prev.filter((row) => !row.selected))}
              onClearAll={() => {
                setRows([]);
                setNotice(null);
              }}
              onOpenFolder={openFolderOf}
            />
          </div>

          {/* Painel direito */}
          <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-1 min-w-0">
            <DestinationCard rootPath={project.rootPath} destination={destination} onChange={setDestination} />
            <OptionsCard options={options} onChange={updateOption} />
            <TagsCard
              tags={tags}
              onAdd={addTags}
              onRemove={(tag) => setTags((prev) => prev.filter((existing) => existing !== tag))}
            />
            <SummaryCard summary={summary} />
          </div>
        </div>
      </div>

      {/* Faixa inferior */}
      <footer className="shrink-0 border-t border-slate-800 bg-[#0a1222] px-6 py-3 flex flex-wrap items-center justify-between gap-3">
        <div className="flex flex-wrap items-center gap-x-3 gap-y-1 text-xs text-slate-300">
          {selectedItems.length > 0 ? (
            <CircleCheck className="w-4 h-4 text-emerald-400" aria-hidden="true" />
          ) : (
            <CircleAlert className="w-4 h-4 text-slate-500" aria-hidden="true" />
          )}
          <span className="font-semibold text-white">
            {selectedItems.length} {pluralItems(selectedItems.length)} selecionados
          </span>
          <span className="text-slate-600">|</span>
          <span className="font-mono">{durationFooter}</span>
          <span className="text-slate-600">|</span>
          <span className="font-mono">{selectedItems.length > 0 ? formatBytes(summary.bytesTotal) : '—'}</span>
          <span className="text-slate-600 hidden md:inline">|</span>
          <span className="text-slate-500 hidden md:inline font-mono truncate max-w-[320px]" title={destinationPath}>
            Destino: {basenameOf(destinationPath)}
          </span>
        </div>
        <div className="flex items-center gap-2">
          <button
            type="button"
            onClick={handleCancelButton}
            className="px-4 py-2 rounded-xl bg-slate-800 hover:bg-slate-700 text-slate-200 text-xs font-semibold border border-slate-700 focus-visible:ring-2 focus-visible:ring-cyan-400 outline-none"
          >
            Cancelar
          </button>
          <button
            type="button"
            onClick={() => void startImport()}
            disabled={selectedItems.length === 0 || isStarting || overlayVisible}
            className="px-4 py-2 rounded-xl bg-cyan-600 hover:bg-cyan-500 text-white text-xs font-semibold flex items-center gap-2 shadow-md shadow-cyan-950 disabled:opacity-40 disabled:cursor-not-allowed focus-visible:ring-2 focus-visible:ring-cyan-300 outline-none"
          >
            <CloudUpload className="w-4 h-4" aria-hidden="true" /> Importar selecionados
          </button>
        </div>
      </footer>

      {overlayVisible && (
        <ImportProgressOverlay
          job={job}
          overlayError={overlayError}
          cancelRequested={cancelRequested}
          onCancel={() => void cancelImport()}
          onClose={closeOverlay}
          onOpenLibrary={handleOpenLibraryFromOverlay}
        />
      )}
    </div>
  );
};
