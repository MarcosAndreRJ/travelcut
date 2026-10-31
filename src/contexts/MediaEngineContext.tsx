import React, { createContext, useContext, useState, useEffect, useCallback, useMemo } from 'react';
import {
  isTauriDesktop,
  getDesktopDiagnostics,
  mediaGetCapabilities,
  mediaInitialize,
  mediaProbe,
  mediaScanDirectory,
  mediaGenerateThumbnail,
  mediaGenerateFilmstrip,
  mediaExtractWaveform,
  mediaTranscode,
  mediaCancelJob,
  mediaCancelAllJobs,
  mediaGetJobStatus,
  SystemDiagnostics,
} from '../services/desktopBridge';
import type {
  DiscoveredFile,
  FfmpegCapabilities,
  FilmstripOptions,
  InventoryOptions,
  JobStatus,
  MediaEngineConfig,
  MediaMetadata,
  ThumbnailOptions,
  TranscodeOptions,
  TranscodeResult,
  WaveformData,
  WaveformOptions,
} from '../types/mediaEngine';

export interface MediaEngineContextValue {
  isNative: boolean;
  capabilities: FfmpegCapabilities | null;
  diagnostics: SystemDiagnostics | null;
  isReady: boolean;
  isScanning: boolean;
  lastError: string | null;

  // Actions
  refreshCapabilities: () => Promise<FfmpegCapabilities>;
  refreshDiagnostics: () => Promise<SystemDiagnostics>;
  initializeEngine: (config?: Partial<MediaEngineConfig>) => Promise<FfmpegCapabilities>;
  probeMedia: (path: string) => Promise<MediaMetadata>;
  scanDirectory: (path: string, options?: InventoryOptions) => Promise<DiscoveredFile[]>;
  generateThumbnail: (path: string, options?: ThumbnailOptions) => Promise<string>;
  generateFilmstrip: (path: string, options?: FilmstripOptions) => Promise<string[]>;
  extractWaveform: (path: string, options?: WaveformOptions) => Promise<WaveformData>;
  transcodeMedia: (options: TranscodeOptions) => Promise<TranscodeResult>;
  cancelJob: (jobId: string) => Promise<boolean>;
  cancelAllJobs: () => Promise<void>;
  getJobStatus: (jobId: string) => Promise<JobStatus | null>;
}

const MediaEngineContext = createContext<MediaEngineContextValue | null>(null);

export const MediaEngineProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const isNative = useMemo(() => isTauriDesktop(), []);
  const [capabilities, setCapabilities] = useState<FfmpegCapabilities | null>(null);
  const [diagnostics, setDiagnostics] = useState<SystemDiagnostics | null>(null);
  const [isReady, setIsReady] = useState<boolean>(false);
  const [isScanning, setIsScanning] = useState<boolean>(false);
  const [lastError, setLastError] = useState<string | null>(null);

  const refreshCapabilities = useCallback(async (): Promise<FfmpegCapabilities> => {
    try {
      const caps = await mediaGetCapabilities();
      setCapabilities(caps);
      return caps;
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : String(err);
      setLastError(msg);
      throw err;
    }
  }, []);

  const refreshDiagnostics = useCallback(async (): Promise<SystemDiagnostics> => {
    try {
      const diag = await getDesktopDiagnostics();
      setDiagnostics(diag);
      return diag;
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : String(err);
      setLastError(msg);
      throw err;
    }
  }, []);

  const initializeEngine = useCallback(async (config?: Partial<MediaEngineConfig>): Promise<FfmpegCapabilities> => {
    try {
      const caps = await mediaInitialize(config);
      setCapabilities(caps);
      setIsReady(true);
      return caps;
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : String(err);
      setLastError(msg);
      throw err;
    }
  }, []);

  const probeMedia = useCallback(async (path: string): Promise<MediaMetadata> => {
    return mediaProbe(path);
  }, []);

  const scanDirectory = useCallback(async (path: string, options?: InventoryOptions): Promise<DiscoveredFile[]> => {
    setIsScanning(true);
    try {
      const files = await mediaScanDirectory(path, options);
      return files;
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : String(err);
      setLastError(msg);
      throw err;
    } finally {
      setIsScanning(false);
    }
  }, []);

  const generateThumbnail = useCallback(async (path: string, options?: ThumbnailOptions): Promise<string> => {
    return mediaGenerateThumbnail(path, options);
  }, []);

  const generateFilmstrip = useCallback(async (path: string, options?: FilmstripOptions): Promise<string[]> => {
    return mediaGenerateFilmstrip(path, options);
  }, []);

  const extractWaveform = useCallback(async (path: string, options?: WaveformOptions): Promise<WaveformData> => {
    return mediaExtractWaveform(path, options);
  }, []);

  const transcodeMedia = useCallback(async (options: TranscodeOptions): Promise<TranscodeResult> => {
    return mediaTranscode(options);
  }, []);

  const cancelJob = useCallback(async (jobId: string): Promise<boolean> => {
    return mediaCancelJob(jobId);
  }, []);

  const cancelAllJobs = useCallback(async (): Promise<void> => {
    return mediaCancelAllJobs();
  }, []);

  const getJobStatus = useCallback(async (jobId: string): Promise<JobStatus | null> => {
    return mediaGetJobStatus(jobId);
  }, []);

  // Inicialização na montagem
  useEffect(() => {
    let mounted = true;

    async function init() {
      try {
        const [caps, diag] = await Promise.all([
          mediaGetCapabilities().catch(() => null),
          getDesktopDiagnostics().catch(() => null),
        ]);

        if (mounted) {
          if (caps) setCapabilities(caps);
          if (diag) setDiagnostics(diag);
          setIsReady(true);
        }
      } catch (err) {
        console.warn('[MediaEngineProvider] Falha ao carregar estado inicial do motor:', err);
      }
    }

    init();

    return () => {
      mounted = false;
    };
  }, []);

  const contextValue = useMemo<MediaEngineContextValue>(() => ({
    isNative,
    capabilities,
    diagnostics,
    isReady,
    isScanning,
    lastError,
    refreshCapabilities,
    refreshDiagnostics,
    initializeEngine,
    probeMedia,
    scanDirectory,
    generateThumbnail,
    generateFilmstrip,
    extractWaveform,
    transcodeMedia,
    cancelJob,
    cancelAllJobs,
    getJobStatus,
  }), [
    isNative,
    capabilities,
    diagnostics,
    isReady,
    isScanning,
    lastError,
    refreshCapabilities,
    refreshDiagnostics,
    initializeEngine,
    probeMedia,
    scanDirectory,
    generateThumbnail,
    generateFilmstrip,
    extractWaveform,
    transcodeMedia,
    cancelJob,
    cancelAllJobs,
    getJobStatus,
  ]);

  return (
    <MediaEngineContext.Provider value={contextValue}>
      {children}
    </MediaEngineContext.Provider>
  );
};

export const useMediaEngine = (): MediaEngineContextValue => {
  const context = useContext(MediaEngineContext);
  if (!context) {
    throw new Error('useMediaEngine deve ser utilizado dentro de um MediaEngineProvider');
  }
  return context;
};
