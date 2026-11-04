import React, { useCallback, useEffect, useRef, useState } from 'react';
import {
  ArrowLeft,
  ArrowRight,
  ArrowUp,
  Camera,
  ChevronDown,
  ChevronRight,
  File as FileIcon,
  FileAudio,
  FileVideo,
  Folder,
  FolderOpen,
  HardDrive,
  Image as ImageIcon,
  Plus,
  RotateCw,
  ShieldCheck,
  Star,
  Video,
  X,
  type LucideIcon
} from 'lucide-react';
import { ipc } from '../../services/ipcBridge';
import type { DirectoryEntryDto } from '../../types/ipc';
import { MediaKind } from '../../types/ipc';
import { messageOf, parentDirectoryOf } from './importUtils';

const DRIVES_KEY = '__drives__';
const STORAGE_LAST_SOURCE_KEY = 'travelcut.import.lastSource';
const STORAGE_FAVORITES_KEY = 'travelcut.import.favorites';

type DirPhase = 'loading' | 'ready' | 'error';

interface DirState {
  phase: DirPhase;
  entries: DirectoryEntryDto[];
  error: string | null;
}

interface NavHistory {
  stack: Array<string | null>;
  index: number;
}

export interface MediaFavorite {
  id: string;
  name: string;
  path: string | null; // null = unidades reais do Windows
  isCustom?: boolean;
}

const keyOf = (path: string | null): string => path ?? DRIVES_KEY;

const KIND_ICON: Record<MediaKind, LucideIcon> = {
  Video: FileVideo,
  Audio: FileAudio,
  Image: ImageIcon
};

const sortEntries = (entries: DirectoryEntryDto[]): DirectoryEntryDto[] =>
  [...entries].sort((a, b) => {
    if (a.isDirectory !== b.isDirectory) return a.isDirectory ? -1 : 1;
    return a.name.localeCompare(b.name, undefined, { numeric: true, sensitivity: 'base' });
  });

const getInitialFavorites = (rootPath: string): MediaFavorite[] => {
  const defaults: MediaFavorite[] = [
    { id: 'drives', name: 'Unidades', path: null, isCustom: false },
    { id: 'project', name: 'Projeto', path: rootPath, isCustom: false },
    { id: 'camera', name: 'Câmera / DCIM', path: 'DCIM', isCustom: false }
  ];
  try {
    const raw = localStorage.getItem(STORAGE_FAVORITES_KEY);
    if (raw) {
      const parsed = JSON.parse(raw) as MediaFavorite[];
      if (Array.isArray(parsed) && parsed.length > 0) {
        // Assegura que Unidades e Projeto estejam sempre presentes
        const hasDrives = parsed.some((f) => f.path === null);
        const hasProject = parsed.some((f) => f.path === rootPath);
        const combined = [...parsed];
        if (!hasDrives) combined.unshift(defaults[0]);
        if (!hasProject) combined.splice(1, 0, defaults[1]);
        return combined;
      }
    }
  } catch {
    // fallback
  }
  return defaults;
};

const getInitialPath = (rootPath: string): string | null => {
  try {
    const last = localStorage.getItem(STORAGE_LAST_SOURCE_KEY);
    if (last !== null && last !== undefined) {
      const trimmed = last.trim();
      if (trimmed === '' || trimmed === DRIVES_KEY) {
        return null; // Inicia na lista de unidades reais do Windows
      }
      return trimmed;
    }
  } catch {
    // fallback
  }
  // Sem acoplamento ao diretório pai: inicia nas unidades ou no projeto
  return rootPath;
};

export interface FileNavigatorProps {
  rootPath: string;
  onAddFile: (path: string) => void;
  onAddFolder: (path: string) => void;
}

export const FileNavigator: React.FC<FileNavigatorProps> = ({ rootPath, onAddFile, onAddFolder }) => {
  const initialPath = useRef<string | null>(getInitialPath(rootPath)).current;

  const [dirs, setDirs] = useState<Record<string, DirState>>({});
  const [history, setHistory] = useState<NavHistory>(() => ({
    stack: [initialPath],
    index: 0
  }));
  const current = history.stack[history.index] ?? null;

  const [expanded, setExpanded] = useState<Set<string>>(() => {
    const set = new Set<string>();
    if (initialPath) set.add(initialPath);
    return set;
  });
  const [selectedKey, setSelectedKey] = useState<string | null>(null);
  const [addressDraft, setAddressDraft] = useState<string>(initialPath ?? '');
  const [favorites, setFavorites] = useState<MediaFavorite[]>(() => getInitialFavorites(rootPath));
  const treeRef = useRef<HTMLDivElement | null>(null);

  const saveFavorites = (newFavorites: MediaFavorite[]) => {
    setFavorites(newFavorites);
    try {
      localStorage.setItem(STORAGE_FAVORITES_KEY, JSON.stringify(newFavorites));
    } catch {
      // ignore
    }
  };

  const loadDirectory = useCallback(async (path: string | null) => {
    const key = keyOf(path);
    setDirs((prev) => ({
      ...prev,
      [key]: { phase: 'loading', entries: prev[key]?.entries ?? [], error: null }
    }));
    try {
      const res = await ipc.listDirectory(path);
      setDirs((prev) => ({
        ...prev,
        [key]: { phase: 'ready', entries: res.entries ?? [], error: null }
      }));
    } catch (err) {
      setDirs((prev) => ({
        ...prev,
        [key]: { phase: 'error', entries: [], error: messageOf(err) }
      }));
    }
  }, []);

  useEffect(() => {
    void loadDirectory(initialPath);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const visit = (target: string | null) => {
    setHistory((prev) => {
      const stack = [...prev.stack.slice(0, prev.index + 1), target];
      return { stack, index: stack.length - 1 };
    });
    setSelectedKey(null);
    setAddressDraft(target ?? '');
    void loadDirectory(target);
    try {
      localStorage.setItem(STORAGE_LAST_SOURCE_KEY, target ?? DRIVES_KEY);
    } catch {
      // storage unavailable
    }
  };

  const step = (delta: number) => {
    const index = history.index + delta;
    if (index < 0 || index >= history.stack.length) return;
    const target = history.stack[index] ?? null;
    setHistory({ ...history, index });
    setSelectedKey(null);
    setAddressDraft(target ?? '');
    void loadDirectory(target);
    try {
      localStorage.setItem(STORAGE_LAST_SOURCE_KEY, target ?? DRIVES_KEY);
    } catch {
      // storage unavailable
    }
  };

  const goUp = () => {
    if (current === null) return;
    const parent = parentDirectoryOf(current);
    visit(parent);
  };

  const refresh = () => {
    void loadDirectory(current);
    expanded.forEach((path) => {
      void loadDirectory(path);
    });
  };

  const submitAddress = () => {
    const target = addressDraft.trim();
    if (!target) {
      visit(null);
      return;
    }
    visit(target);
  };

  const toggleFolder = (entry: DirectoryEntryDto) => {
    const path = entry.fullPath;
    setSelectedKey(path);
    const isOpen = expanded.has(path);
    setExpanded((prev) => {
      const next = new Set(prev);
      if (next.has(path)) next.delete(path);
      else next.add(path);
      return next;
    });
    if (!isOpen && (!dirs[path] || dirs[path].phase === 'error')) {
      void loadDirectory(path);
    }
  };

  const addCurrentToFavorites = () => {
    if (!current) return;
    const trimmed = current.trim();
    if (favorites.some((f) => f.path === trimmed)) return;
    const segments = trimmed.replace(/[/\\]+$/, '').split(/[/\\]/);
    const name = segments[segments.length - 1] || trimmed;
    const next: MediaFavorite[] = [
      ...favorites,
      {
        id: `fav_${Date.now()}`,
        name,
        path: trimmed,
        isCustom: true
      }
    ];
    saveFavorites(next);
  };

  const removeFavorite = (id: string, e: React.MouseEvent) => {
    e.stopPropagation();
    saveFavorites(favorites.filter((f) => f.id !== id));
  };

  const focusSibling = (element: HTMLElement, delta: number) => {
    const rows = treeRef.current?.querySelectorAll<HTMLElement>('[role="treeitem"]');
    if (!rows) return;
    const index = Array.from(rows).indexOf(element);
    rows[index + delta]?.focus();
  };

  const handleRowKeyDown = (event: React.KeyboardEvent<HTMLDivElement>, entry: DirectoryEntryDto, isRoot: boolean) => {
    const target = event.currentTarget;
    switch (event.key) {
      case 'Enter':
      case ' ': {
        event.preventDefault();
        if (entry.isDirectory) {
          if (isRoot) visit(entry.fullPath);
          else toggleFolder(entry);
        } else {
          onAddFile(entry.fullPath);
        }
        break;
      }
      case 'ArrowRight': {
        if (!entry.isDirectory) return;
        event.preventDefault();
        if (!expanded.has(entry.fullPath)) toggleFolder(entry);
        else focusSibling(target, 1);
        break;
      }
      case 'ArrowLeft': {
        if (!entry.isDirectory) return;
        event.preventDefault();
        if (expanded.has(entry.fullPath)) toggleFolder(entry);
        else focusSibling(target, -1);
        break;
      }
      case 'ArrowDown': {
        event.preventDefault();
        focusSibling(target, 1);
        break;
      }
      case 'ArrowUp': {
        event.preventDefault();
        focusSibling(target, -1);
        break;
      }
    }
  };

  const renderRow = (entry: DirectoryEntryDto, depth: number, isRoot = false): React.ReactNode => {
    const isDir = entry.isDirectory;
    const isOpen = isDir && expanded.has(entry.fullPath);
    const isSelected = selectedKey === entry.fullPath;
    const isDrive = current === null && isDir;
    const Icon: LucideIcon = isDrive
      ? HardDrive
      : isDir
        ? isOpen
          ? FolderOpen
          : Folder
        : (entry.kind && KIND_ICON[entry.kind]) || FileIcon;

    return (
      <div
        key={entry.fullPath}
        role="treeitem"
        tabIndex={0}
        aria-expanded={isDir ? isOpen : undefined}
        aria-selected={isSelected}
        style={{ paddingLeft: depth * 14 + 6 }}
        onClick={() => {
          setSelectedKey(entry.fullPath);
          if (isDir) {
            if (isRoot || isDrive) visit(entry.fullPath);
            else toggleFolder(entry);
          }
        }}
        onDoubleClick={() => {
          if (isDir) visit(entry.fullPath);
          else onAddFile(entry.fullPath);
        }}
        onKeyDown={(event) => handleRowKeyDown(event, entry, isRoot)}
        className={`group flex items-center gap-1.5 pr-1 py-1 rounded-md cursor-pointer text-xs select-none outline-none focus-visible:ring-2 focus-visible:ring-cyan-500 transition-colors ${
          isSelected ? 'bg-cyan-500/15 text-cyan-100' : 'text-slate-300 hover:bg-slate-800/70'
        }`}
      >
        {isDir ? (
          isOpen ? (
            <ChevronDown className="w-3.5 h-3.5 shrink-0 text-slate-400" aria-hidden="true" />
          ) : (
            <ChevronRight className="w-3.5 h-3.5 shrink-0 text-slate-400" aria-hidden="true" />
          )
        ) : (
          <span className="w-3.5 shrink-0" aria-hidden="true" />
        )}
        <Icon
          className={`w-4 h-4 shrink-0 ${isDrive ? 'text-cyan-400' : isDir ? 'text-amber-400' : 'text-cyan-400'}`}
          aria-hidden="true"
        />
        <span className="truncate flex-1 min-w-0" title={entry.fullPath}>
          {entry.name}
        </span>
        {isDir && (
          <button
            type="button"
            title={`Adicionar pasta "${entry.name}" e todas as suas subpastas (recursivo)`}
            aria-label={`Adicionar pasta ${entry.name} recursivamente`}
            onClick={(event) => {
              event.stopPropagation();
              onAddFolder(entry.fullPath);
            }}
            className="opacity-0 group-hover:opacity-100 focus-visible:opacity-100 px-1.5 py-0.5 rounded text-[10px] font-medium text-cyan-300 hover:text-white bg-cyan-950/60 hover:bg-cyan-600/50 border border-cyan-500/40 transition-all flex items-center gap-0.5 shrink-0"
          >
            <Plus className="w-3 h-3 text-cyan-400" aria-hidden="true" />
            <span>+ Pasta (recursivo)</span>
          </button>
        )}
        {!isDir && (
          <button
            type="button"
            title={`Adicionar arquivo "${entry.name}"`}
            aria-label={`Adicionar arquivo ${entry.name}`}
            onClick={(event) => {
              event.stopPropagation();
              onAddFile(entry.fullPath);
            }}
            className="opacity-0 group-hover:opacity-100 focus-visible:opacity-100 px-1.5 py-0.5 rounded text-[10px] text-slate-300 hover:text-white bg-slate-800 hover:bg-slate-700 border border-slate-700 transition-all flex items-center gap-0.5 shrink-0"
          >
            <Plus className="w-3 h-3 text-cyan-400" aria-hidden="true" />
            <span>Adicionar</span>
          </button>
        )}
      </div>
    );
  };

  const renderChildren = (path: string | null, depth: number): React.ReactNode => {
    const state = dirs[keyOf(path)];
    if (!state) return null;
    if (state.phase === 'loading' && state.entries.length === 0) {
      return (
        <div style={{ paddingLeft: 22 + depth * 14 }} className="text-[11px] text-slate-500 py-1">
          Carregando…
        </div>
      );
    }
    if (state.phase === 'error') {
      return (
        <div style={{ paddingLeft: 22 + depth * 14 }} className="text-[11px] text-rose-300 py-1 pr-2" role="alert">
          {state.error}
        </div>
      );
    }
    const visible = sortEntries(state.entries).filter((entry) => entry.isDirectory || Boolean(entry.kind));
    if (visible.length === 0) {
      return (
        <div style={{ paddingLeft: 22 + depth * 14 }} className="text-[11px] text-slate-500 py-1">
          Nenhuma pasta ou mídia aqui.
        </div>
      );
    }
    return visible.map((entry) => (
      <React.Fragment key={entry.fullPath}>
        {renderRow(entry, depth)}
        {entry.isDirectory && expanded.has(entry.fullPath) && renderChildren(entry.fullPath, depth + 1)}
      </React.Fragment>
    ));
  };

  const currentState = dirs[keyOf(current)];
  const canGoBack = history.index > 0;
  const canGoForward = history.index < history.stack.length - 1;
  const canGoUp = current !== null;
  const isCurrentInFavorites = current !== null && favorites.some((f) => f.path === current);

  return (
    <div className="flex flex-col h-full min-h-0 gap-2">
      {/* Barra de Origens Rápidas / Favoritos */}
      <div className="flex items-center gap-1 overflow-x-auto pb-1 no-scrollbar text-xs">
        <span className="text-[10px] font-bold uppercase tracking-wider text-slate-500 px-1 shrink-0">
          Origens:
        </span>
        {favorites.map((fav) => {
          const isActive = current === fav.path;
          const isDrives = fav.path === null;
          return (
            <div
              key={fav.id}
              onClick={() => visit(fav.path)}
              className={`flex items-center gap-1 px-2 py-1 rounded-md text-[11px] font-medium cursor-pointer transition-colors shrink-0 border ${
                isActive
                  ? 'bg-cyan-500/20 text-cyan-200 border-cyan-500/50'
                  : 'bg-slate-900/80 text-slate-300 hover:text-white hover:bg-slate-800 border-slate-700/60'
              }`}
              title={fav.path ?? 'Todas as unidades do computador'}
            >
              {isDrives ? (
                <HardDrive className="w-3 h-3 text-cyan-400" aria-hidden="true" />
              ) : fav.id === 'camera' ? (
                <Camera className="w-3 h-3 text-amber-400" aria-hidden="true" />
              ) : (
                <Star
                  className={`w-3 h-3 ${fav.isCustom ? 'text-amber-300 fill-amber-300' : 'text-slate-400'}`}
                  aria-hidden="true"
                />
              )}
              <span>{fav.name}</span>
              {fav.isCustom && (
                <button
                  type="button"
                  onClick={(e) => removeFavorite(fav.id, e)}
                  title="Remover favorito"
                  className="p-0.5 hover:text-rose-400 transition-colors ml-0.5"
                >
                  <X className="w-2.5 h-2.5" />
                </button>
              )}
            </div>
          );
        })}
        {current !== null && !isCurrentInFavorites && (
          <button
            type="button"
            onClick={addCurrentToFavorites}
            className="flex items-center gap-1 px-1.5 py-1 rounded-md text-[10px] text-cyan-400 hover:text-cyan-200 hover:bg-cyan-950/40 border border-dashed border-cyan-500/40 transition-colors shrink-0"
            title="Fixar a pasta atual nos favoritos"
          >
            <Star className="w-2.5 h-2.5" />
            <span>+ Fixar pasta</span>
          </button>
        )}
      </div>

      {/* Barra de Navegação */}
      <div className="flex items-center gap-1.5">
        <button
          type="button"
          aria-label="Voltar"
          title="Voltar"
          disabled={!canGoBack}
          onClick={() => step(-1)}
          className="p-1.5 rounded-lg bg-slate-900 border border-slate-700 text-slate-300 hover:bg-slate-800 disabled:opacity-40 disabled:cursor-not-allowed focus-visible:ring-2 focus-visible:ring-cyan-500 outline-none shrink-0"
        >
          <ArrowLeft className="w-3.5 h-3.5" aria-hidden="true" />
        </button>
        <button
          type="button"
          aria-label="Avançar"
          title="Avançar"
          disabled={!canGoForward}
          onClick={() => step(1)}
          className="p-1.5 rounded-lg bg-slate-900 border border-slate-700 text-slate-300 hover:bg-slate-800 disabled:opacity-40 disabled:cursor-not-allowed focus-visible:ring-2 focus-visible:ring-cyan-500 outline-none shrink-0"
        >
          <ArrowRight className="w-3.5 h-3.5" aria-hidden="true" />
        </button>
        <button
          type="button"
          aria-label="Subir um nível"
          title={canGoUp ? 'Subir para pasta pai' : 'No nível mais alto (Unidades)'}
          disabled={!canGoUp}
          onClick={goUp}
          className="p-1.5 rounded-lg bg-slate-900 border border-slate-700 text-slate-300 hover:bg-slate-800 disabled:opacity-40 disabled:cursor-not-allowed focus-visible:ring-2 focus-visible:ring-cyan-500 outline-none shrink-0"
        >
          <ArrowUp className="w-3.5 h-3.5" aria-hidden="true" />
        </button>
        <button
          type="button"
          aria-label="Retornar à lista de unidades"
          title="Ver todas as unidades do Windows (C:\, D:\, etc.)"
          onClick={() => visit(null)}
          className={`p-1.5 rounded-lg border text-slate-300 transition-colors focus-visible:ring-2 focus-visible:ring-cyan-500 outline-none shrink-0 ${
            current === null
              ? 'bg-cyan-500/20 border-cyan-500 text-cyan-300'
              : 'bg-slate-900 border-slate-700 hover:bg-slate-800'
          }`}
        >
          <HardDrive className="w-3.5 h-3.5 text-cyan-400" aria-hidden="true" />
        </button>
        <input
          type="text"
          value={addressDraft}
          placeholder="Unidades do Windows (vazio = listar unidades)"
          aria-label="Caminho da pasta"
          onChange={(event) => setAddressDraft(event.target.value)}
          onKeyDown={(event) => {
            if (event.key === 'Enter') {
              event.preventDefault();
              submitAddress();
            }
          }}
          className="flex-1 min-w-0 px-2 py-1 rounded-lg bg-slate-900/90 border border-slate-700 text-xs text-white placeholder-slate-500 focus:outline-none focus:ring-1 focus:ring-cyan-500 focus:border-cyan-500"
        />
        <button
          type="button"
          aria-label="Recarregar pasta"
          title="Recarregar"
          onClick={refresh}
          className="p-1.5 rounded-lg bg-slate-900 border border-slate-700 text-slate-300 hover:bg-slate-800 focus-visible:ring-2 focus-visible:ring-cyan-500 outline-none shrink-0"
        >
          <RotateCw
            className={`w-3.5 h-3.5 ${currentState?.phase === 'loading' ? 'animate-spin' : ''}`}
            aria-hidden="true"
          />
        </button>
      </div>

      {/* Ação rápida para adicionar a pasta inteira aberta */}
      {current !== null && (
        <button
          type="button"
          title={`Adicionar todo o conteúdo da pasta atual e subpastas à lista de importação`}
          onClick={() => onAddFolder(current)}
          className="w-full flex items-center justify-center gap-1.5 py-1.5 px-3 rounded-lg bg-cyan-950/50 hover:bg-cyan-900/60 border border-cyan-500/40 text-cyan-300 hover:text-white text-xs font-semibold transition-all shadow-sm"
        >
          <Plus className="w-3.5 h-3.5 text-cyan-400" aria-hidden="true" />
          <span>Adicionar esta pasta inteira (recursivo)</span>
        </button>
      )}

      {/* Árvore de navegação */}
      <div
        ref={treeRef}
        role="tree"
        aria-label="Navegador de arquivos"
        className="flex-1 min-h-[200px] overflow-y-auto rounded-xl bg-[#070d18] border border-slate-800/80 p-1.5"
      >
        {current === null ? (
          renderChildren(null, 0)
        ) : (
          <>
            {renderRow(
              { name: current, fullPath: current, isDirectory: true, byteLength: null, kind: null },
              0,
              true
            )}
            {renderChildren(current, 1)}
          </>
        )}
      </div>

      {/* Garantia de Segurança */}
      <div className="flex items-center gap-1.5 px-1 py-0.5 text-[11px] text-slate-400 shrink-0">
        <ShieldCheck className="w-3.5 h-3.5 text-emerald-400 shrink-0" aria-hidden="true" />
        <span>Leitura segura: os arquivos de origem nunca são alterados nem movidos.</span>
      </div>
    </div>
  );
};
