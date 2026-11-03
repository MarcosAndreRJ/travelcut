import { useCallback, useEffect, useState } from 'react';
import type { PlaybackSourceDto } from '../../types/ipc';
import { getPlaybackSource, previewErrorMessage } from '../../services/previewApi';

export type PlaybackSourceState =
  | { kind: 'loading' }
  | { kind: 'ready'; source: PlaybackSourceDto }
  | { kind: 'error'; message: string };

export interface PlaybackSourceHandle {
  state: PlaybackSourceState;
  reload: () => void;
}

/** Carrega media.getPlaybackSource para uma mídia. Respostas de uma mídia anterior são descartadas. */
export function usePlaybackSource(projectId: string, mediaId: string): PlaybackSourceHandle {
  const [state, setState] = useState<PlaybackSourceState>({ kind: 'loading' });
  const [reloadKey, setReloadKey] = useState<number>(0);

  useEffect(() => {
    let active = true;
    setState({ kind: 'loading' });
    getPlaybackSource(projectId, mediaId)
      .then((source) => {
        if (active) setState({ kind: 'ready', source });
      })
      .catch((err: unknown) => {
        if (active) setState({ kind: 'error', message: previewErrorMessage(err) });
      });
    return () => {
      active = false;
    };
  }, [projectId, mediaId, reloadKey]);

  const reload = useCallback(() => setReloadKey((key) => key + 1), []);

  return { state, reload };
}

/** Fonte com URL reproduzível pelo <video> (original ou proxy). */
export const isPlayableSource = (source: PlaybackSourceDto): boolean =>
  (source.status === 'original' || source.status === 'proxy') && !!source.url;
