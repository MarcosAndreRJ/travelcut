import React, { useCallback, useEffect, useRef, useState } from 'react';
import {
  ArrowLeft,
  ArrowRight,
  ArrowUp,
  ChevronDown,
  ChevronRight,
  File as FileIcon,
  FileAudio,
  FileVideo,
  Folder,
  FolderOpen,
  Image as ImageIcon,
  Plus,
  RotateCw,
  type LucideIcon
} from 'lucide-react';
import { ipc } from '../../services/ipcBridge';
import type { DirectoryEntryDto, MediaKind } from '../../types/ipc';
import { messageOf, parentDirectoryOf } from './importUtils';

const DRIVES_KEY = '__drives__';

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

const keyOf = (path: string | null): string => path ?? DRIVES_KEY;

const KIND_ICON: Record<MediaKind, LucideIcon> = {
  Video: FileVideo,
  Audio: FileAudio,
  Image: ImageIcon
};

const sortEntries = (entries: DirectoryEntryDto[]): DirectoryEntryDto[] =>
  [...entries].sort((a, b) => {
    if (a.isDirectory !== b.isDirectory) return a.isDirectory ? -1 : 1;
    return a.name.localeCompare(b.name, 'pt-BR', { numeric: true, sensitivity: 'base' });
  });

interface FileNavigatorProps {
  rootPath: string;
  onAddFile: (path: string) => void;
  onAddFolder: (path: string) => void;
}

/**
 * Navegador de arquivos da Tela 07. Começa na pasta pai do projeto, com a pasta do projeto expandida.
 * Carregamento preguiçoso via fs.listDirectory (path null = unidades).
 */
export const FileNavigator: React.FC<FileNavigatorProps> = ({ rootPath, onAddFile, onAddFolder }) => {
  const initialRoot: string = parentDirectoryOf(rootPath) ?? rootPath;

  const [history, setHistory] = useState<NavHistory>({ stack: [initialRoot], index: 0 });
  const current = history.stack[history.index] ?? null;
  const [dirs, setDirs] = useState<Record<string, DirState>>({});
  const [expanded, setExpanded] = useState<Set<string>>(() => new Set([rootPath]));
  const [selectedKey, setSelectedKey] = useState<string | null>(null);
  const [addressDraft, setAddressDraft] = useState<string>(initialRoot);
  const treeRef = useRef<HTMLDivElement | null>(null);

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
    void loadDirectory(initialRoot);
    if (rootPath !== initialRoot) void loadDirectory(rootPath);
    // Carga inicial única: a navegação posterior é feita pelas ações abaixo.
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
  };

  const step = (delta: number) => {
    const index = history.index + delta;
    if (index < 0 || index >= history.stack.length) return;
    const target = history.stack[index] ?? null;
    setHistory({ ...history, index });
    setSelectedKey(null);
    setAddressDraft(target ?? '');
    void loadDirectory(target);
  };

  const goUp = () => {
    if (current === null) return;
    visit(parentDirectoryOf(current));
  };

  const refresh = () => {
    void loadDirectory(current);
    expanded.forEach((path) => {
      void loadDirectory(path);
    });
  };

  const submitAddress = () => {
    const target = addressDraft.trim();
    if (!target) return;
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

  const focusSibling = (element: HTMLElement, delta: number) => {
    const rows = treeRef.current?.querySelectorAll<HTMLElement>('[role="treeitem"]');
    if (!rows) return;
    const index = Array.from(rows).indexOf(element);
    rows[index + delta]?.focus();
  };

  const handleRowKeyDown = (event: React.KeyboardEvent<HTMLDivElement>, entry: DirectoryEntryDto, isRoot: boolean) => {
    // Ignora teclas vindas de botões internos (ex.: "+").
    if (event.target !== event.currentTarget) return;
    const isOpen = expanded.has(entry.fullPath);
    switch (event.key) {
      case 'Enter':
      case ' ':
        event.preventDefault();
        if (entry.isDirectory) {
          if (!isRoot) toggleFolder(entry);
        } else {
          onAddFile(entry.fullPath);
        }
        break;
      case 'ArrowRight':
        if (entry.isDirectory && !isRoot && !isOpen) {
          event.preventDefault();
          toggleFolder(entry);
        }
        break;
      case 'ArrowLeft':
        if (entry.isDirectory && !isRoot && isOpen) {
          event.preventDefault();
          toggleFolder(entry);
        }
        break;
      case 'ArrowDown':
        event.preventDefault();
        focusSibling(event.currentTarget, 1);
        break;
      case 'ArrowUp':
        event.preventDefault();
        focusSibling(event.currentTarget, -1);
        break;
      default:
        break;
    }
  };

  const renderRow = (entry: DirectoryEntryDto, depth: number, isRoot = false): React.ReactNode => {
    const isDir = entry.isDirectory;
    // A pasta atual (raiz do painel) fica sempre aberta e não alterna.
    const isOpen = isDir && (isRoot || expanded.has(entry.fullPath));
    const isSelected = selectedKey === entry.fullPath;
    const Icon: LucideIcon = isDir
      ? isOpen
        ? FolderOpen
        : Folder
      : entry.kind
        ? KIND_ICON[entry.kind] ?? FileIcon
        : FileIcon;

    return (
      <div
        key={entry.fullPath}
        role="treeitem"
        aria-level={depth + 1}
        aria-expanded={isDir ? isOpen : undefined}
        aria-selected={isSelected}
        tabIndex={0}
        title={entry.fullPath}
        onClick={() => {
          if (isRoot) setSelectedKey(entry.fullPath);
          else if (isDir) toggleFolder(entry);
          else setSelectedKey(entry.fullPath);
        }}
        onDoubleClick={() => {
          if (!isDir) onAddFile(entry.fullPath);
        }}
        onKeyDown={(event) => handleRowKeyDown(event, entry, isRoot)}
        style={{ paddingLeft: 6 + depth * 14 }}
        className={`group flex items-center gap-1.5 pr-1 py-1 rounded-md cursor-pointer text-xs select-none outline-none focus-visible:ring-2 focus-visible:ring-cyan-500 ${
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
          className={`w-4 h-4 shrink-0 ${isDir ? 'text-amber-400' : 'text-cyan-400'}`}
          aria-hidden="true"
        />
        <span className="truncate flex-1 min-w-0">{entry.name}</span>
        {isDir && (
          <button
            type="button"
            title="Adicionar a pasta inteira"
            aria-label={`Adicionar pasta ${entry.name} à lista`}
            onClick={(event) => {
              event.stopPropagation();
              onAddFolder(entry.fullPath);
            }}
            className="p-0.5 rounded text-cyan-300 opacity-60 hover:opacity-100 hover:bg-cyan-500/20 focus-visible:opacity-100 focus-visible:ring-2 focus-visible:ring-cyan-500 outline-none"
          >
            <Plus className="w-3.5 h-3.5" aria-hidden="true" />
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

  return (
    <div className="flex flex-col h-full min-h-0 gap-2">
      <div className="flex items-center gap-1.5">
        <button
          type="button"
          aria-label="Voltar"
          title="Voltar"
          disabled={!canGoBack}
          onClick={() => step(-1)}
          className="p-1.5 rounded-lg bg-slate-900 border border-slate-700 text-slate-300 hover:bg-slate-800 disabled:opacity-40 disabled:cursor-not-allowed focus-visible:ring-2 focus-visible:ring-cyan-500 outline-none"
        >
          <ArrowLeft className="w-3.5 h-3.5" aria-hidden="true" />
        </button>
        <button
          type="button"
          aria-label="Avançar"
          title="Avançar"
          disabled={!canGoForward}
          onClick={() => step(1)}
          className="p-1.5 rounded-lg bg-slate-900 border border-slate-700 text-slate-300 hover:bg-slate-800 disabled:opacity-40 disabled:cursor-not-allowed focus-visible:ring-2 focus-visible:ring-cyan-500 outline-none"
        >
          <ArrowRight className="w-3.5 h-3.5" aria-hidden="true" />
        </button>
        <button
          type="button"
          aria-label="Subir um nível"
          title="Subir um nível"
          disabled={!canGoUp}
          onClick={goUp}
          className="p-1.5 rounded-lg bg-slate-900 border border-slate-700 text-slate-300 hover:bg-slate-800 disabled:opacity-40 disabled:cursor-not-allowed focus-visible:ring-2 focus-visible:ring-cyan-500 outline-none"
        >
          <ArrowUp className="w-3.5 h-3.5" aria-hidden="true" />
        </button>
        <input
          type="text"
          value={addressDraft}
          onChange={(event) => setAddressDraft(event.target.value)}
          onKeyDown={(event) => {
            if (event.key === 'Enter') submitAddress();
            if (event.key === 'Escape') setAddressDraft(current ?? '');
          }}
          onBlur={() => setAddressDraft(current ?? '')}
          aria-label="Caminho da pasta"
          placeholder="Digite um caminho e pressione Enter"
          className="flex-1 min-w-0 px-2 py-1 bg-[#0a1424] border border-slate-700 rounded-lg text-[11px] font-mono text-white placeholder-slate-500 focus:outline-none focus:border-cyan-500"
        />
        <button
          type="button"
          aria-label="Atualizar"
          title="Atualizar"
          onClick={refresh}
          className="p-1.5 rounded-lg bg-slate-900 border border-slate-700 text-cyan-400 hover:bg-slate-800 focus-visible:ring-2 focus-visible:ring-cyan-500 outline-none"
        >
          <RotateCw className={`w-3.5 h-3.5 ${currentState?.phase === 'loading' ? 'animate-spin' : ''}`} aria-hidden="true" />
        </button>
      </div>

      <div
        ref={treeRef}
        role="tree"
        aria-label="Navegador de arquivos"
        className="flex-1 min-h-[220px] overflow-y-auto rounded-xl bg-[#070d18] border border-slate-800/80 p-1.5"
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
    </div>
  );
};
