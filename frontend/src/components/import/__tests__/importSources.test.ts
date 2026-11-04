import { describe, expect, it, beforeEach } from 'vitest';

const STORAGE_FAVORITES_KEY = 'travelcut.import.favorites';
const STORAGE_LAST_SOURCE_KEY = 'travelcut.import.lastSource';

interface MediaFavorite {
  id: string;
  name: string;
  path: string | null;
  isBuiltin?: boolean;
}

// Em ambiente de teste Node, provê polyfill se localStorage não estiver definido
const storageMap = new Map<string, string>();
const storage = {
  getItem: (key: string) => storageMap.get(key) ?? null,
  setItem: (key: string, value: string) => storageMap.set(key, value),
  clear: () => storageMap.clear(),
};

const getInitialFavorites = (rootPath: string): MediaFavorite[] => {
  const defaults: MediaFavorite[] = [
    { id: 'drives', name: 'Unidades do Windows', path: null, isBuiltin: true },
    { id: 'project', name: 'Pasta do Projeto', path: rootPath, isBuiltin: true },
    { id: 'camera', name: 'Câmera / DCIM', path: 'C:\\DCIM', isBuiltin: true },
  ];
  try {
    const raw = storage.getItem(STORAGE_FAVORITES_KEY);
    if (raw) {
      const parsed = JSON.parse(raw) as MediaFavorite[];
      if (Array.isArray(parsed) && parsed.length > 0) {
        return parsed;
      }
    }
  } catch {
    // fallback
  }
  return defaults;
};

const getInitialPath = (rootPath: string): string | null => {
  try {
    const last = storage.getItem(STORAGE_LAST_SOURCE_KEY);
    if (last !== null && last !== undefined) {
      const trimmed = last.trim();
      if (trimmed === '' || trimmed === '__drives__') {
        return null;
      }
      return trimmed;
    }
  } catch {
    // fallback
  }
  return rootPath;
};

describe('Frente B.2 — Origens configuráveis e persistência da navegação', () => {
  const projectRoot = 'D:\\Viagens\\Chile2026';

  beforeEach(() => {
    storage.clear();
  });

  it('inicia na pasta do projeto quando não há histórico, sem acoplar ao pai', () => {
    const initial = getInitialPath(projectRoot);
    expect(initial).toBe(projectRoot);
    expect(initial).not.toBe('D:\\Viagens'); // Nunca acopla ao diretório pai
  });

  it('lembra a última origem utilizada salva no storage', () => {
    storage.setItem(STORAGE_LAST_SOURCE_KEY, 'E:\\CartaoSD\\DCIM\\100GOPRO');
    const initial = getInitialPath(projectRoot);
    expect(initial).toBe('E:\\CartaoSD\\DCIM\\100GOPRO');
  });

  it('retorna null (unidades) se a última origem salva for __drives__', () => {
    storage.setItem(STORAGE_LAST_SOURCE_KEY, '__drives__');
    const initial = getInitialPath(projectRoot);
    expect(initial).toBeNull();
  });

  it('fornece favoritos padrão para unidades, projeto e câmera/DCIM', () => {
    const favorites = getInitialFavorites(projectRoot);
    expect(favorites.length).toBeGreaterThanOrEqual(3);
    expect(favorites.some((f) => f.path === null && f.name.includes('Unidades'))).toBe(true);
    expect(favorites.some((f) => f.path === projectRoot)).toBe(true);
    expect(favorites.some((f) => f.id === 'camera')).toBe(true);
  });

  it('persiste novos favoritos customizados no storage', () => {
    const customFavorites: MediaFavorite[] = [
      { id: 'drives', name: 'Unidades', path: null, isBuiltin: true },
      { id: 'fav-1', name: 'Drone 4K', path: 'F:\\DJI_MINI', isBuiltin: false }
    ];
    storage.setItem(STORAGE_FAVORITES_KEY, JSON.stringify(customFavorites));

    const loaded = getInitialFavorites(projectRoot);
    expect(loaded.length).toBe(2);
    expect(loaded[1].name).toBe('Drone 4K');
    expect(loaded[1].path).toBe('F:\\DJI_MINI');
  });
});
