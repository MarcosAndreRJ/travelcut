import { useCallback, useEffect, useRef, useState } from 'react';
import type { PlaybackSourceDto } from '../../types/ipc';
import { ipc } from '../../services/ipcBridge';

export interface PlayerEventRecord {
  event: string;
  timeMs: number;
  currentTime: number;
  readyState: number;
  networkState: number;
}

export interface PlayerDiagnosticData {
  mediaId: string;
  url: string | null;
  status: string | null;
  mimeType: string | null;
  codec: string | null;
  proxyStatus: string | null;
  errorCode: number | null;
  errorMessage: string | null;
  readyState: number;
  networkState: number;
  videoWidth: number;
  videoHeight: number;
  duration: number;
  currentTime: number;
  timeToFailureMs: number | null;
  recentEvents: PlayerEventRecord[];
  timestampUtc: string;
  userAgent: string;
}

export function usePlayerDiagnostics(
  mediaId: string,
  source: PlaybackSourceDto | null,
  videoRef: React.RefObject<HTMLVideoElement | null>
) {
  const [diagnostic, setDiagnostic] = useState<PlayerDiagnosticData | null>(null);
  const eventsRef = useRef<PlayerEventRecord[]>([]);
  const startTimeRef = useRef<number>(Date.now());

  const recordEvent = useCallback((eventName: string, v: HTMLVideoElement) => {
    const record: PlayerEventRecord = {
      event: eventName,
      timeMs: Date.now() - startTimeRef.current,
      currentTime: v.currentTime,
      readyState: v.readyState,
      networkState: v.networkState,
    };
    eventsRef.current = [...eventsRef.current.slice(-15), record];
  }, []);

  useEffect(() => {
    const v = videoRef.current;
    if (!v) return;

    eventsRef.current = [];
    startTimeRef.current = Date.now();

    const onEvent = (e: Event) => {
      recordEvent(e.type, v);
    };

    const onError = () => {
      const err = v.error;
      recordEvent('error', v);
      const data: PlayerDiagnosticData = {
        mediaId,
        url: source?.url ?? v.src ?? null,
        status: source?.status ?? null,
        mimeType: source?.mimeType ?? null,
        codec: source?.codec ?? null,
        proxyStatus: source?.proxyStatus ?? null,
        errorCode: err?.code ?? null,
        errorMessage: err?.message || (err?.code ? `HTMLMediaElement error code ${err.code}` : 'Erro desconhecido'),
        readyState: v.readyState,
        networkState: v.networkState,
        videoWidth: v.videoWidth,
        videoHeight: v.videoHeight,
        duration: v.duration || 0,
        currentTime: v.currentTime || 0,
        timeToFailureMs: Date.now() - startTimeRef.current,
        recentEvents: [...eventsRef.current],
        timestampUtc: new Date().toISOString(),
        userAgent: navigator.userAgent,
      };
      setDiagnostic(data);
    };

    const events = ['loadstart', 'loadedmetadata', 'canplay', 'playing', 'pause', 'waiting', 'stalled'];
    events.forEach(name => v.addEventListener(name, onEvent));
    v.addEventListener('error', onError);

    return () => {
      events.forEach(name => v.removeEventListener(name, onEvent));
      v.removeEventListener('error', onError);
    };
  }, [mediaId, source, videoRef, recordEvent]);

  const copyDiagnostic = useCallback(async () => {
    if (!diagnostic) return false;
    try {
      const json = JSON.stringify(diagnostic, null, 2);
      await navigator.clipboard.writeText(json);
      return true;
    } catch {
      return false;
    }
  }, [diagnostic]);

  const openLogsFolder = useCallback(async () => {
    try {
      await ipc.openFolder('__logs__');
    } catch (ex) {
      console.error('Falha ao abrir pasta de logs:', ex);
    }
  }, []);

  return {
    diagnostic,
    copyDiagnostic,
    openLogsFolder,
    clearDiagnostic: () => setDiagnostic(null),
  };
}
