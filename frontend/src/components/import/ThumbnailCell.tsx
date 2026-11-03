import React, { useEffect, useRef, useState } from 'react';
import { Film, ImageOff } from 'lucide-react';
import { ipc } from '../../services/ipcBridge';
import type { ThumbnailResponse } from '../../types/ipc';
import { formatDurationClock, messageOf } from './importUtils';

type ThumbState =
  | { phase: 'idle' }
  | { phase: 'loading' }
  | { phase: 'done'; response: ThumbnailResponse }
  | { phase: 'error'; message: string };

// Somente miniaturas com status ok são reaproveitadas; demais são pedidas de novo.
const okThumbnailCache = new Map<string, ThumbnailResponse>();

const THUMBNAIL_STATUS_FALLBACK: Record<ThumbnailResponse['status'], string> = {
  ok: 'Miniatura indisponível',
  ffmpeg_unavailable: 'FFmpeg indisponível para gerar miniaturas',
  failed: 'Falha ao gerar miniatura',
  not_supported: 'Miniatura não suportada para este arquivo'
};

interface ThumbnailCellProps {
  projectId: string;
  path: string;
  fileName: string;
  durationSeconds: number | null;
  /** Tamanho compacto da tabela (56x32). */
  compact?: boolean;
}

/** Miniatura carregada apenas quando a linha entra na área visível (lazy). */
export const ThumbnailCell: React.FC<ThumbnailCellProps> = ({ projectId, path, fileName, durationSeconds, compact = false }) => {
  const containerRef = useRef<HTMLDivElement | null>(null);
  const [isVisible, setIsVisible] = useState(false);
  const [state, setState] = useState<ThumbState>({ phase: 'idle' });
  const cacheKey = `${projectId}::${path}`;

  useEffect(() => {
    if (isVisible) return;
    const el = containerRef.current;
    if (!el) return;
    if (typeof IntersectionObserver === 'undefined') {
      setIsVisible(true);
      return;
    }
    const observer = new IntersectionObserver(
      (entries) => {
        if (entries.some((entry) => entry.isIntersecting)) {
          setIsVisible(true);
          observer.disconnect();
        }
      },
      { rootMargin: '120px' }
    );
    observer.observe(el);
    return () => observer.disconnect();
  }, [isVisible]);

  useEffect(() => {
    if (!isVisible) return;
    const cached = okThumbnailCache.get(cacheKey);
    if (cached) {
      setState({ phase: 'done', response: cached });
      return;
    }
    let active = true;
    setState({ phase: 'loading' });
    ipc
      .getThumbnail(projectId, path)
      .then((response) => {
        if (response.status === 'ok') okThumbnailCache.set(cacheKey, response);
        if (active) setState({ phase: 'done', response });
      })
      .catch((err: unknown) => {
        if (active) setState({ phase: 'error', message: messageOf(err) });
      });
    return () => {
      active = false;
    };
  }, [isVisible, cacheKey, projectId, path]);

  let content: React.ReactNode;
  let title: string | undefined;

  if (state.phase === 'done' && state.response.status === 'ok' && state.response.dataUrl) {
    content = <img src={state.response.dataUrl} alt={`Miniatura de ${fileName}`} className="w-full h-full object-cover" />;
  } else if (state.phase === 'done') {
    title = state.response.reason ?? THUMBNAIL_STATUS_FALLBACK[state.response.status];
    content = <ImageOff className="w-5 h-5 text-slate-600" aria-hidden="true" />;
  } else if (state.phase === 'error') {
    title = state.message;
    content = <ImageOff className="w-5 h-5 text-slate-600" aria-hidden="true" />;
  } else if (state.phase === 'loading') {
    content = <div className="w-full h-full animate-pulse bg-slate-800/60" />;
  } else {
    content = <Film className="w-5 h-5 text-slate-600" aria-hidden="true" />;
  }

  const durationLabel = formatDurationClock(durationSeconds);

  return (
    <div
      ref={containerRef}
      title={title}
      aria-label={title ? `Miniatura indisponível: ${title}` : undefined}
      className={`relative ${
        compact ? 'w-14 h-8' : 'w-20 h-12'
      } rounded-md bg-slate-900 border border-slate-800 overflow-hidden shrink-0 flex items-center justify-center`}
    >
      {content}
      {durationLabel && (
        <span
          className={`absolute bottom-0 right-0 px-0.5 rounded-tl text-[8px] font-mono font-bold bg-black/80 text-white ${
            compact ? '' : 'text-[9px]'
          }`}
        >
          {durationLabel}
        </span>
      )}
    </div>
  );
};
