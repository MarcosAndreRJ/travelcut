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
import type { DirectoryEntryDto, MediaKind } from '../../types/ipc';
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
  path: string | null; // null = unidades do Windows
  isBuiltin?: boolean;
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

const getInitialFavorites = (rootPath: string): MediaFavorite[] => {
  const defaults: MediaFavorite[] = [
    { id: 'drives', name: 'Unidades do Windows', path: null, isBuiltin: true },
    { id: 'project', name: 'Pasta do Projeto', path: rootPath, isBuiltin: true },
    { id: 'camera', name: 'Câmera / DCIM', path: 'C:\\DCIM', isBuiltin: true }
  ];

  try {
    const raw = localStorage.getItem(STORAGE_FAVORITES_KEY);
    if (raw) {
      const parsed = JSON.parse(raw) as MediaFavorite[];
      if (Array.isArray(parsed) && parsed.length > 0) {
        const hasDrives = parsed.some((f) => f.path === null);
        const hasProject = parsed.some((f) => f.path === rootPath);
        const combined = [...parsed];
        if (!hasDrives) combined.unshift(defaults[0]);
        if (!hasProject) combined.splice(1, 0, defaults[1]);
        return combined;
      }
    }
  } catch {
    // fallback para os padrões
  }

  return defaults;
};

const getInitialPath = (rootPath: string): string | null => {
  try {
    const last = localStorage.getItem(STORAGE_LAST_SOURCE_KEY);
    if (last !== null && last !== undefined) {
      const trimmed = last.trim();
      if (trimmed === '' || trimmed === DRIVES_KEY) {
        return null;
      }
      return trimmed;
    }
  } catch {
    // fallback
  }
  return rootPath;
};

/**
 * Navegador de arquivos da Tela 07.
 * Suporta navegação livre por qualquer unidade do Windows, favoritos persistentes em settings.json
 * e histórico mantido em sincronia com o backend.
 */
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
  const treeRef = useRef<HTMLDivElement>(null);

  // Sincronização e migração com settings.json do backend (Frente A + Frente B)
  useEffect(() => {
    let canceled = false;
    const syncWithBackendSettings = async () => {
      try {
        const settings = await ipc.getSettings();
        if (canceled || !settings || !settings.mediaSourceFolders) return;

        const currentFavs = [...favorites];
        let changed = false;

        // 1. Incorpora pastas de settings.json aos favoritos locais
        for (const folder of settings.mediaSourceFolders) {
          if (!currentFavs.some((f) => f.path?.toLowerCase() === folder.toLowerCase())) {
            const name = folder.split(/[\\/]/).filter(Boolean).pop() || folder;
            currentFavs.push({
              id: `folder-${Date.now()}-${Math.random().toString(36).substring(2, 7)}`,
              name,
              path: folder,
              isBuiltin: false
            });
            changed = true;
          }
        }

        // 2. Migra favoritos já existentes no localStorage para settings.json
        for (const fav of currentFavs) {
          if (fav.path && !fav.isBuiltin && !settings.mediaSourceFolders.some((f) => f.toLowerCase() === fav.path?.toLowerCase())) {
            try {
              await ipc.addMediaSourceFolder(fav.path);
            } catch {
              // Best effort
            }
          }
        }

        if (changed && !canceled) {
          setFavorites(currentFavs);
          try {
            localStorage.setItem(STORAGE_FAVORITES_KEY, JSON.stringify(currentFavs));
          } catch {
            // ignore
          }
        }
      } catch {
        // Fallback gracioso se ipc.getSettings() não estiver ativo (ex: mock ou dev)
      }
    };

    void syncWithBackendSettings();
    return () => {
      canceled = true;
    };
  }, []);

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
      [key]: {
        phase: 'loading',
        entries: prev[key]?.entries ?? [],
        error: null
      }
    }));

    try {
      const response = await ipc.listDirectory(path ?? null);
      setDirs((prev) => ({
        ...prev,
        [key]: {
          phase: 'ready',
          entries: sortEntries(response.entries),
          error: null
        }
      }));
    } catch (err) {
      setDirs((prev) => ({
        ...prev,
        [key]: {
          phase: 'error',
          entries: [],
          error: messageOf(err)
        }
      }));
    }
  }, []);

  useEffect(() => {
    void loadDirectory(current);
  }, [current, loadDirectory]);

  const navigateTo = (target: string | null) => {
    if (target === current) return;
    setHistory((prev) => {
      const nextStack = prev.stack.slice(0, prev.index + 1);
      nextStack.push(target);
      return {
        stack: nextStack,
        index: nextStack.length - 1
      };
    });
    setSelectedKey(null);
    setAddressDraft(target ?? '');
    void loadDirectory(target);
    try {
      localStorage.setItem(STORAGE_LAST_SOURCE_KEY, target ?? DRIVES_KEY);
    } catch {
      // ignore
    }
  };

  const goBack = () => {
    if (history.index <= 0) return;
    const nextIndex = history.index - 1;
    setHistory((prev) => ({ ...prev, index: nextIndex }));
    const target = history.stack[nextIndex] ?? null;
    setSelectedKey(null);
    setAddressDraft(target ?? '');
    void loadDirectory(target);
  };

  const goForward = () => {
    if (history.index >= history.stack.length - 1) return;
    const nextIndex = history.index + 1;
    setHistory((prev) => ({ ...prev, index: nextIndex }));
    const target = history.stack[nextIndex] ?? null;
    setSelectedKey(null);
    setAddressDraft(target ?? '');
    void loadDirectory(target);
  };

  const goUp = () => {
    const parent = current ? parentDirectoryOf(current) : null; if (parent) navigateTo(parent);
  };

  const refresh = () => {
    void loadDirectory(current);
    expanded.forEach((path) => {
      void loadDirectory(path);
    });
  };

  const toggleExpand = (entry: DirectoryEntryDto, e: React.MouseEvent) => {
    e.stopPropagation();
    if (!entry.isDirectory) return;

    setExpanded((prev) => {
      const next = new Set(prev);
      if (next.has(entry.fullPath)) {
        next.delete(entry.fullPath);
      } else {
        next.add(entry.fullPath);
        void loadDirectory(entry.fullPath);
      }
      return next;
    });
  };

  const handleRowClick = (entry: DirectoryEntryDto) => {
    setSelectedKey(entry.fullPath);
  };

  const handleRowDoubleClick = (entry: DirectoryEntryDto) => {
    if (entry.isDirectory) {
      navigateTo(entry.fullPath);
    } else {
      onAddFile(entry.fullPath);
    }
  };

  const handleAddressSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    const trimmed = addressDraft.trim();
    if (trimmed === '') {
      navigateTo(null);
    } else {
      navigateTo(trimmed);
    }
  };

  const handleAddCurrentAsFavorite = () => {
    if (!current) return;
    if (favorites.some((f) => f.path === current)) return;

    const parts = current.split(/[\\/]/).filter(Boolean);
    const name = parts[parts.length - 1] || current;
    const newFav: MediaFavorite = {
      id: `fav-${Date.now()}`,
      name,
      path: current,
      isBuiltin: false
    };
    saveFavorites([...favorites, newFav]);
    void ipc.addMediaSourceFolder(current).catch(() => {});
  };

  const handleRemoveFavorite = (id: string, e: React.MouseEvent) => {
    e.stopPropagation();
    const fav = favorites.find((f) => f.id === id);
    saveFavorites(favorites.filter((f) => f.id !== id));
    if (fav?.path) {
      void ipc.removeMediaSourceFolder(fav.path).catch(() => {});
    }
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
        event.preventDefault();
        if (entry.isDirectory) {
          navigateTo(entry.fullPath);
        } else {
          onAddFile(entry.fullPath);
        }
        break;
      case 'ArrowRight':
        event.preventDefault();
        if (entry.isDirectory) {
          if (!expanded.has(entry.fullPath)) {
            setExpanded((prev) => new Set(prev).add(entry.fullPath));
            void loadDirectory(entry.fullPath);
          } else {
            focusSibling(target, 1);
          }
        }
        break;
      case 'ArrowLeft':
        event.preventDefault();
        if (entry.isDirectory && expanded.has(entry.fullPath)) {
          setExpanded((prev) => {
            const next = new Set(prev);
            next.delete(entry.fullPath);
            return next;
          });
        } else if (!isRoot) {
          focusSibling(target, -1);
        }
        break;
      case 'ArrowDown':
        event.preventDefault();
        focusSibling(target, 1);
        break;
      case 'ArrowUp':
        event.preventDefault();
        focusSibling(target, -1);
        break;
      case 'Home':
        event.preventDefault();
        (treeRef.current?.querySelector('[role="treeitem"]') as HTMLElement | null)?.focus();
        break;
      case 'End': {
        event.preventDefault();
        const rows = treeRef.current?.querySelectorAll<HTMLElement>('[role="treeitem"]');
        if (rows && rows.length > 0) {
          rows[rows.length - 1].focus();
        }
        break;
      }
      default:
        break;
    }
  };

  const renderRow = (entry: DirectoryEntryDto, depth: number) => {
    const isSelected = selectedKey === entry.fullPath;
    const isExpanded = expanded.has(entry.fullPath);
    const Icon: LucideIcon = entry.isDirectory
      ? isExpanded
        ? FolderOpen
        : Folder
      : entry.kind
        ? KIND_ICON[entry.kind]
        : FileIcon;

    return (
      <div
        key={entry.fullPath}
        role="treeitem"
        tabIndex={0}
        aria-expanded={entry.isDirectory ? isExpanded : undefined}
        aria-selected={isSelected}
        onClick={() => handleRowClick(entry)}
        onDoubleClick={() => handleRowDoubleClick(entry)}
        onKeyDown={(e) => handleRowKeyDown(e, entry, depth === 0)}
        style={{ paddingLeft: 6 + depth * 14 }}
        className={`group flex items-center gap-1.5 py-1 px-2 rounded-lg text-xs cursor-pointer select-none transition-colors outline-none focus:ring-1 focus:ring-cyan-500/50 ${
          isSelected
            ? 'bg-cyan-500/20 text-cyan-200 border border-cyan-500/40'
            : 'text-slate-300 hover:bg-slate-800/60 hover:text-white'
        }`}
      >
        {entry.isDirectory ? (
          <button
            type="button"
            tabIndex={-1}
            onClick={(e) => toggleExpand(entry, e)}
            className="p-0.5 text-slate-500 hover:text-slate-300 rounded"
            aria-label={isExpanded ? 'Recolher pasta' : 'Expandir pasta'}
          >
            {isExpanded ? <ChevronDown className="w-3.5 h-3.5" /> : <ChevronRight className="w-3.5 h-3.5" />}
          </button>
        ) : (
          <span className="w-4 h-4 shrink-0" />
        )}

        <Icon
          className={`w-4 h-4 shrink-0 ${
            entry.isDirectory
              ? 'text-amber-400'
              : entry.kind === 'Video'
                ? 'text-cyan-400'
                : entry.kind === 'Audio'
                  ? 'text-emerald-400'
                  : entry.kind === 'Image'
                    ? 'text-purple-400'
                    : 'text-slate-500'
          }`}
          aria-hidden="true"
        />

        <span className="truncate flex-1 font-mono text-[11px]" title={entry.name}>
          {entry.name}
        </span>

        {entry.kind && (
          <span className="text-[10px] text-slate-500 uppercase px-1 py-0.5 rounded bg-slate-800/80 shrink-0">
            {entry.kind}
          </span>
        )}

        <div className="opacity-0 group-hover:opacity-100 flex items-center gap-1 shrink-0">
          {entry.isDirectory ? (
            <button
              type="button"
              tabIndex={-1}
              onClick={(e) => {
                e.stopPropagation();
                onAddFolder(entry.fullPath);
              }}
              className="px-1.5 py-0.5 rounded bg-cyan-600/30 hover:bg-cyan-500 text-cyan-200 hover:text-white text-[10px] flex items-center gap-1 transition-colors"
              title="Adicionar pasta inteira à fila de importação"
            >
              <Plus className="w-3 h-3" /> Pasta
            </button>
          ) : (
            <button
              type="button"
              tabIndex={-1}
              onClick={(e) => {
                e.stopPropagation();
                onAddFile(entry.fullPath);
              }}
              className="px-1.5 py-0.5 rounded bg-cyan-600/30 hover:bg-cyan-500 text-cyan-200 hover:text-white text-[10px] flex items-center gap-1 transition-colors"
              title="Adicionar arquivo à fila de importação"
            >
              <Plus className="w-3 h-3" /> Arquivo
            </button>
          )}
        </div>
      </div>
    );
  };

  const renderChildren = (parentPath: string | null, depth: number) => {
    const key = keyOf(parentPath);
    const state = dirs[key];

    if (!state || state.phase === 'loading') {
      return (
        <div style={{ paddingLeft: 22 + depth * 14 }} className="text-[11px] text-slate-500 py-1">
          Carregando...
        </div>
      );
    }

    if (state.phase === 'error') {
      return (
        <div style={{ paddingLeft: 22 + depth * 14 }} className="text-[11px] text-rose-400 py-1">
          {state.error ?? 'Falha ao listar pasta.'}
        </div>
      );
    }

    const visible = state.entries;
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
    <div className="flex flex-col h-full bg-[#0a1222] border border-slate-800 rounded-2xl overflow-hidden">
      {/* Barra de Acesso Rápido a Fontes / Favoritos */}
      <div className="px-3 py-2 bg-slate-900/60 border-b border-slate-800/80 flex flex-wrap items-center gap-1.5">
        <span className="text-[10px] font-bold text-slate-400 uppercase tracking-wider mr-1 flex items-center gap-1">
          <Star className="w-3 h-3 text-amber-400" /> Origens:
        </span>
        {favorites.map((fav) => {
          const isActive = current === fav.path;
          return (
            <div
              key={fav.id}
              className={`inline-flex items-center rounded-lg border text-xs transition-all ${
                isActive
                  ? 'bg-cyan-500/20 border-cyan-500/50 text-cyan-300 font-semibold shadow-sm'
                  : 'bg-slate-800/60 border-slate-700/60 text-slate-300 hover:bg-slate-800 hover:text-white'
              }`}
            >
              <button
                type="button"
                onClick={() => navigateTo(fav.path)}
                className="px-2.5 py-1 flex items-center gap-1.5 truncate max-w-[180px]"
                title={fav.path ?? 'Lista de unidades'}
              >
                {fav.id === 'drives' ? (
                  <HardDrive className="w-3.5 h-3.5 text-cyan-400 shrink-0" />
                ) : fav.id === 'camera' ? (
                  <Camera className="w-3.5 h-3.5 text-amber-400 shrink-0" />
                ) : (
                  <Folder className="w-3.5 h-3.5 text-slate-400 shrink-0" />
                )}
                <span className="truncate">{fav.name}</span>
              </button>
              {!fav.isBuiltin && (
                <button
                  type="button"
                  onClick={(e) => handleRemoveFavorite(fav.id, e)}
                  className="pr-1.5 pl-0.5 py-1 text-slate-500 hover:text-rose-400 transition-colors"
                  title="Remover favorito"
                  aria-label={`Remover favorito ${fav.name}`}
                >
                  <X className="w-3 h-3" />
                </button>
              )}
            </div>
          );
        })}
        {current && !favorites.some((f) => f.path === current) && (
          <button
            type="button"
            onClick={handleAddCurrentAsFavorite}
            className="px-2 py-1 text-[11px] rounded-lg border border-dashed border-slate-700 hover:border-cyan-500/60 text-slate-400 hover:text-cyan-300 flex items-center gap-1 transition-all"
            title="Fixar pasta atual como fonte favorita"
          >
            <Plus className="w-3 h-3" /> Fixar pasta
          </button>
        )}
      </div>

      {/* Barra de Navegação de Caminho */}
      <div className="p-2.5 border-b border-slate-800 flex items-center gap-1.5 bg-[#0b1325]">
        <button
          type="button"
          onClick={goBack}
          disabled={!canGoBack}
          className="p-1.5 rounded-lg border border-slate-800 bg-slate-900/60 text-slate-400 hover:text-white hover:bg-slate-800 disabled:opacity-30 disabled:pointer-events-none transition-colors"
          title="Voltar"
          aria-label="Voltar"
        >
          <ArrowLeft className="w-3.5 h-3.5" />
        </button>
        <button
          type="button"
          onClick={goForward}
          disabled={!canGoForward}
          className="p-1.5 rounded-lg border border-slate-800 bg-slate-900/60 text-slate-400 hover:text-white hover:bg-slate-800 disabled:opacity-30 disabled:pointer-events-none transition-colors"
          title="Avançar"
          aria-label="Avançar"
        >
          <ArrowRight className="w-3.5 h-3.5" />
        </button>
        <button
          type="button"
          onClick={goUp}
          disabled={!canGoUp}
          className="p-1.5 rounded-lg border border-slate-800 bg-slate-900/60 text-slate-400 hover:text-white hover:bg-slate-800 disabled:opacity-30 disabled:pointer-events-none transition-colors"
          title="Subir um nível"
          aria-label="Subir um nível"
        >
          <ArrowUp className="w-3.5 h-3.5" />
        </button>
        <button
          type="button"
          onClick={refresh}
          className="p-1.5 rounded-lg border border-slate-800 bg-slate-900/60 text-slate-400 hover:text-white hover:bg-slate-800 transition-colors"
          title="Atualizar"
          aria-label="Atualizar"
        >
          <RotateCw className="w-3.5 h-3.5" />
        </button>

        <form onSubmit={handleAddressSubmit} className="flex-1 min-w-0">
          <input
            type="text"
            value={addressDraft}
            onChange={(e) => setAddressDraft(e.target.value)}
            placeholder="Digite o caminho completo (ex.: D:\Videos ou vazio para unidades)"
            className="w-full px-2.5 py-1 text-xs bg-slate-950 border border-slate-800 rounded-lg text-slate-200 placeholder:text-slate-600 focus:outline-none focus:border-cyan-500/60 font-mono"
          />
        </form>

        {current && (
          <button
            type="button"
            onClick={() => onAddFolder(current)}
            className="px-2.5 py-1 rounded-lg bg-cyan-600 hover:bg-cyan-500 text-white text-xs font-semibold flex items-center gap-1.5 transition-colors shadow-sm shrink-0"
            title="Importar todos os arquivos desta pasta"
          >
            <Plus className="w-3.5 h-3.5" /> Importar pasta
          </button>
        )}
      </div>

      {/* Árvore / Lista de Arquivos */}
      <div
        ref={treeRef}
        role="tree"
        aria-label="Navegador de pastas do projeto e unidades"
        className="flex-1 overflow-y-auto p-2 space-y-0.5 text-xs font-sans"
      >
        {currentState?.phase === 'loading' && currentState.entries.length === 0 ? (
          <div className="flex items-center justify-center h-40 text-xs text-slate-500">
            Carregando arquivos...
          </div>
        ) : currentState?.phase === 'error' && currentState.entries.length === 0 ? (
          <div className="p-4 rounded-xl bg-rose-950/20 border border-rose-900/40 text-xs text-rose-300">
            <p className="font-semibold mb-1">Não foi possível acessar este caminho.</p>
            <p className="text-[11px] text-slate-400">{currentState.error}</p>
          </div>
        ) : (
          renderChildren(current, 0)
        )}
      </div>

      {/* Rodapé informativo */}
      <div className="p-2 border-t border-slate-800/80 bg-slate-900/40 text-[11px] text-slate-400 flex items-center justify-between">
        <div className="flex items-center gap-1.5">
          <Video className="w-3.5 h-3.5 text-cyan-400" />
          <span>Filtro ativo: apenas vídeos, áudios e fotos são adicionados à fila.</span>
        </div>
        <div className="flex items-center gap-2">
          <span>Duplo-clique para adicionar ou navegar.</span>
        </div>
      </div>
      <div className="px-3 py-1.5 bg-slate-950 border-t border-slate-800/50 text-[10px] text-slate-500 flex items-center gap-2">
        <ShieldCheck className="w-3.5 h-3.5 text-emerald-400 shrink-0" aria-hidden="true" />
        <span>Leitura segura: os arquivos de origem nunca são alterados nem movidos.</span>
      </div>
    </div>
  );
};
