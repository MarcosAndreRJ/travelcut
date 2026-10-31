import React, { createContext, useContext, useState, useEffect, useCallback, useMemo } from 'react';
import {
  selectFolderDialog,
  selectFileDialog,
  saveProjectFile,
  loadProjectFile,
  getRecentProjects,
  addRecentProject,
  removeRecentProject as apiRemoveRecentProject,
  saveProjectState,
  loadProjectState,
  RecentProjectItem,
  ProjectStatePayload,
  mediaScanDirectory,
  mediaProbe,
} from '../services/desktopBridge';
import { LibraryClip } from '../features/library/types';

export interface ProjectState {
  name: string;
  folderPath: string;
  mediaCount: number;
  totalDurationSeconds: number;
  clips: LibraryClip[];
  isScanning: boolean;
  isLoading: boolean;
  error: string | null;
  lastSaved: string | null;
}

export interface ProjectContextValue {
  project: ProjectState;
  recentProjects: RecentProjectItem[];
  isScanning: boolean;
  isLoading: boolean;
  openFolderDialogAndLoad: () => Promise<string | null>;
  openFileDialogAndLoad: () => Promise<string | null>;
  loadProjectFromFolder: (folderPath: string, customName?: string) => Promise<void>;
  loadProjectFromFile: (filePath: string) => Promise<void>;
  createNewProject: (projectName?: string, folderPath?: string) => void;
  closeProject: () => void;
  saveCurrentProjectState: () => Promise<boolean>;
  deleteRecentProject: (path: string) => Promise<void>;
  renameProject: (newName: string) => void;
  setClips: React.Dispatch<React.SetStateAction<LibraryClip[]>>;
}

const ProjectContext = createContext<ProjectContextValue | null>(null);

export const INITIAL_EMPTY_PROJECT: ProjectState = {
  name: '',
  folderPath: '',
  mediaCount: 0,
  totalDurationSeconds: 0,
  clips: [],
  isScanning: false,
  isLoading: false,
  error: null,
  lastSaved: null,
};

export const ProjectProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const [project, setProject] = useState<ProjectState>(INITIAL_EMPTY_PROJECT);

  const [recentProjects, setRecentProjects] = useState<RecentProjectItem[]>([]);

  // Load recent projects list on mount
  useEffect(() => {
    let mounted = true;
    getRecentProjects()
      .then((items) => {
        if (mounted && Array.isArray(items)) {
          setRecentProjects(items);
        }
      })
      .catch(() => {
        // Fallback default
      });
    return () => {
      mounted = false;
    };
  }, []);

  const refreshRecents = useCallback(async () => {
    try {
      const items = await getRecentProjects();
      if (Array.isArray(items)) {
        setRecentProjects(items);
      }
    } catch {
      // Ignorar
    }
  }, []);

  const renameProject = useCallback((newName: string) => {
    setProject((prev) => ({
      ...prev,
      name: newName,
    }));
  }, []);

  const setClips: React.Dispatch<React.SetStateAction<LibraryClip[]>> = useCallback((action) => {
    setProject((prev) => {
      const nextClips = typeof action === 'function' ? action(prev.clips) : action;
      return {
        ...prev,
        clips: nextClips,
        mediaCount: nextClips.length,
        totalDurationSeconds: nextClips.reduce((acc, c) => acc + (c.durationSec || 0), 0),
      };
    });
  }, []);

  const saveCurrentProjectState = useCallback(async (): Promise<boolean> => {
    if (!project.folderPath) return false;

    const payload: ProjectStatePayload = {
      version: '1.0.0',
      updated_at: new Date().toISOString(),
      project_name: project.name,
      root_directory: project.folderPath,
      timeline_data: {},
      media_pool: project.clips,
      metadata: {
        mediaCount: project.mediaCount,
        totalDurationSeconds: project.totalDurationSeconds,
      },
    };

    try {
      // Try project directory atomic save
      await saveProjectState(project.folderPath, payload);

      // Record in recent projects
      const recentItem: RecentProjectItem = {
        id: `proj-${Date.now()}`,
        name: project.name,
        path: project.folderPath,
        lastOpened: new Date().toISOString(),
        mediaCount: project.mediaCount,
        totalDurationSeconds: project.totalDurationSeconds,
        previewThumbnail: project.clips[0]?.thumbnail || null,
      };
      await addRecentProject(recentItem);
      await refreshRecents();

      setProject((prev) => ({
        ...prev,
        lastSaved: new Date().toLocaleTimeString('pt-BR'),
      }));
      return true;
    } catch (err: unknown) {
      console.error('[ProjectContext] Erro ao salvar estado do projeto:', err);
      return false;
    }
  }, [project, refreshRecents]);

  const loadProjectFromFolder = useCallback(
    async (folderPath: string, customName?: string): Promise<void> => {
      setProject((prev) => ({
        ...prev,
        isScanning: true,
        isLoading: true,
        error: null,
      }));

      try {
        // Step 1: Check if an existing travelcut.json exists in the folder
        let existingState: ProjectStatePayload | null = null;
        try {
          existingState = await loadProjectState(folderPath);
        } catch {
          existingState = null;
        }

        let loadedClips: LibraryClip[] = [];
        let projectName = customName || folderPath.split(/[/\\]/).filter(Boolean).pop() || 'Novo Projeto';

        if (existingState && Array.isArray(existingState.media_pool) && existingState.media_pool.length > 0) {
          loadedClips = existingState.media_pool as LibraryClip[];
          if (existingState.project_name) {
            projectName = existingState.project_name;
          }
        } else {
          // Scan folder for raw video files
          const files = await mediaScanDirectory(folderPath, {
            recursive: true,
            max_depth: 3,
          });

          // Concurrently probe discovered files to retrieve real FFprobe technical metadata
          const probedClips: LibraryClip[] = await Promise.all(
            files.map(async (file, index) => {
              const fileName = file.path.split(/[/\\]/).pop() || `clip_${index + 1}.mp4`;
              let durationSec = 0;
              let resolution = '1920x1080';
              let camera = 'Câmera Principal';
              let durationFormatted = '00:00';
              let fileSize = file.size_bytes || 0;

              try {
                const metadata = await mediaProbe(file.path);
                durationSec = Math.round(metadata.duration_secs || 0);
                fileSize = metadata.file_size_bytes || fileSize;
                const minutes = Math.floor(durationSec / 60);
                const seconds = durationSec % 60;
                durationFormatted = `${String(minutes).padStart(2, '0')}:${String(seconds).padStart(2, '0')}`;

                if (metadata.video_streams && metadata.video_streams.length > 0) {
                  const mainStream = metadata.video_streams[0];
                  resolution = `${mainStream.width}x${mainStream.height}`;
                }

                if (metadata.camera_make || metadata.camera_model) {
                  camera = [metadata.camera_make, metadata.camera_model].filter(Boolean).join(' ');
                } else if (fileName.toLowerCase().includes('dji')) {
                  camera = 'DJI Drone';
                } else if (fileName.toLowerCase().includes('gopro')) {
                  camera = 'GoPro';
                } else if (fileName.toLowerCase().includes('sony')) {
                  camera = 'Sony Alpha';
                }
              } catch (probeErr) {
                console.warn(`[ProjectContext] Falha no probe do arquivo ${file.path}:`, probeErr);
              }

              const isDrone = fileName.toLowerCase().includes('dji') || fileName.toLowerCase().includes('drone');
              const isAction = fileName.toLowerCase().includes('gopro') || fileName.toLowerCase().includes('action');

              return {
                id: `clip-scanned-${index}-${Date.now()}`,
                filename: fileName,
                title: fileName.replace(/\.[^/.]+$/, ''),
                category: (isDrone ? 'Drone' : isAction ? 'Deslocamento' : 'Paisagem') as 'Drone' | 'Deslocamento' | 'Paisagem',
                duration: durationFormatted,
                durationSec,
                thumbnail: '',
                waveform: [],
                resolution,
                camera,
                location: folderPath,
                date: new Date().toISOString().split('T')[0],
                aiScore: 80,
                transcript: [],
                fileSize,
                filePath: file.path,
              };
            })
          );

          loadedClips = probedClips;
        }

        const totalDur = loadedClips.reduce((acc, c) => acc + (c.durationSec || 0), 0);

        setProject({
          name: projectName,
          folderPath,
          mediaCount: loadedClips.length,
          totalDurationSeconds: totalDur,
          clips: loadedClips,
          isScanning: false,
          isLoading: false,
          error: null,
          lastSaved: new Date().toLocaleTimeString('pt-BR'),
        });

        // Add to recents
        await addRecentProject({
          id: `proj-${Date.now()}`,
          name: projectName,
          path: folderPath,
          lastOpened: new Date().toISOString(),
          mediaCount: loadedClips.length,
          totalDurationSeconds: totalDur,
          previewThumbnail: loadedClips[0]?.thumbnail || null,
        });
        await refreshRecents();
      } catch (err: unknown) {
        const msg = err instanceof Error ? err.message : String(err);
        setProject((prev) => ({
          ...prev,
          isScanning: false,
          isLoading: false,
          error: msg,
        }));
      }
    },
    [refreshRecents]
  );

  const createNewProject = useCallback((projectName?: string, folderPath?: string) => {
    setProject({
      name: projectName || 'Novo Projeto',
      folderPath: folderPath || '',
      mediaCount: 0,
      totalDurationSeconds: 0,
      clips: [],
      isScanning: false,
      isLoading: false,
      error: null,
      lastSaved: null,
    });
  }, []);

  const closeProject = useCallback(() => {
    setProject(INITIAL_EMPTY_PROJECT);
  }, []);

  const loadProjectFromFile = useCallback(
    async (filePath: string): Promise<void> => {
      setProject((prev) => ({ ...prev, isLoading: true, error: null }));
      try {
        const state = await loadProjectFile(filePath);
        const clips = (Array.isArray(state.media_pool) ? state.media_pool : []) as LibraryClip[];
        const folder = state.root_directory || filePath.replace(/[^/\\]+$/, '');

        setProject({
          name: state.project_name || 'Projeto Carregado',
          folderPath: folder,
          mediaCount: clips.length,
          totalDurationSeconds: clips.reduce((acc, c) => acc + (c.durationSec || 0), 0),
          clips,
          isScanning: false,
          isLoading: false,
          error: null,
          lastSaved: new Date().toLocaleTimeString('pt-BR'),
        });

        await addRecentProject({
          id: `proj-${Date.now()}`,
          name: state.project_name || 'Projeto Carregado',
          path: filePath,
          lastOpened: new Date().toISOString(),
          mediaCount: clips.length,
          totalDurationSeconds: clips.reduce((acc, c) => acc + (c.durationSec || 0), 0),
        });
        await refreshRecents();
      } catch (err: unknown) {
        const msg = err instanceof Error ? err.message : String(err);
        setProject((prev) => ({ ...prev, isLoading: false, error: msg }));
      }
    },
    [refreshRecents]
  );

  const openFolderDialogAndLoad = useCallback(async (): Promise<string | null> => {
    try {
      const selected = await selectFolderDialog('Selecione a pasta de filmagens do projeto');
      if (selected) {
        await loadProjectFromFolder(selected);
        return selected;
      }
      return null;
    } catch (err) {
      console.error('[ProjectContext] Erro ao abrir diálogo de pasta:', err);
      return null;
    }
  }, [loadProjectFromFolder]);

  const openFileDialogAndLoad = useCallback(async (): Promise<string | null> => {
    try {
      const selected = await selectFileDialog(
        'Abrir arquivo de projeto TravelCut',
        undefined,
        'Projetos TravelCut (*.travelcut, *.json)',
        ['travelcut', 'json']
      );
      if (selected) {
        await loadProjectFromFile(selected);
        return selected;
      }
      return null;
    } catch (err) {
      console.error('[ProjectContext] Erro ao abrir diálogo de arquivo:', err);
      return null;
    }
  }, [loadProjectFromFile]);

  const deleteRecentProject = useCallback(
    async (path: string): Promise<void> => {
      await apiRemoveRecentProject(path);
      await refreshRecents();
    },
    [refreshRecents]
  );

  const value = useMemo<ProjectContextValue>(
    () => ({
      project,
      recentProjects,
      isScanning: project.isScanning,
      isLoading: project.isLoading,
      openFolderDialogAndLoad,
      openFileDialogAndLoad,
      loadProjectFromFolder,
      loadProjectFromFile,
      createNewProject,
      closeProject,
      saveCurrentProjectState,
      deleteRecentProject,
      renameProject,
      setClips,
    }),
    [
      project,
      recentProjects,
      openFolderDialogAndLoad,
      openFileDialogAndLoad,
      loadProjectFromFolder,
      loadProjectFromFile,
      createNewProject,
      closeProject,
      saveCurrentProjectState,
      deleteRecentProject,
      renameProject,
      setClips,
    ]
  );

  return <ProjectContext.Provider value={value}>{children}</ProjectContext.Provider>;
};

export const useProject = (): ProjectContextValue => {
  const ctx = useContext(ProjectContext);
  if (!ctx) {
    throw new Error('useProject deve ser usado dentro de um ProjectProvider');
  }
  return ctx;
};
