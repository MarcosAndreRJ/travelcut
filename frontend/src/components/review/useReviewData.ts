import { useEffect, useState } from 'react';
import { ipc } from '../../services/ipcBridge';
import type { MediaAnalysisDto, MediaAssetDto } from '../../types/ipc';
import { errorMessage } from '../analysis/analysisFormat';

export type AsyncState<T> =
  | { status: 'idle' }
  | { status: 'loading' }
  | { status: 'ready'; data: T }
  | { status: 'error'; message: string };

/** Biblioteca do projeto indexada por mediaId (para nome do arquivo). Falha vira estado de erro, nunca dado inventado. */
export const useProjectMedia = (projectId: string | null): AsyncState<Record<string, MediaAssetDto>> => {
  const [state, setState] = useState<AsyncState<Record<string, MediaAssetDto>>>({ status: 'idle' });

  useEffect(() => {
    if (!projectId) {
      setState({ status: 'idle' });
      return;
    }
    let cancelled = false;
    setState({ status: 'loading' });
    ipc
      .listMedia(projectId)
      .then((list) => {
        if (cancelled) return;
        const byId: Record<string, MediaAssetDto> = {};
        for (const asset of list) byId[asset.mediaId] = asset;
        setState({ status: 'ready', data: byId });
      })
      .catch((err: unknown) => {
        if (!cancelled) setState({ status: 'error', message: errorMessage(err) });
      });
    return () => {
      cancelled = true;
    };
  }, [projectId]);

  return state;
};

/** Análise de uma mídia (transcrição com palavras). Recarrega ao trocar de mídia. */
export const useMediaAnalysis = (projectId: string | null, mediaId: string | null): AsyncState<MediaAnalysisDto> => {
  const [state, setState] = useState<AsyncState<MediaAnalysisDto>>({ status: 'idle' });

  useEffect(() => {
    if (!projectId || !mediaId) {
      setState({ status: 'idle' });
      return;
    }
    let cancelled = false;
    setState({ status: 'loading' });
    ipc
      .getMediaAnalysis(projectId, mediaId)
      .then((data) => {
        if (!cancelled) setState({ status: 'ready', data });
      })
      .catch((err: unknown) => {
        if (!cancelled) setState({ status: 'error', message: errorMessage(err) });
      });
    return () => {
      cancelled = true;
    };
  }, [projectId, mediaId]);

  return state;
};
