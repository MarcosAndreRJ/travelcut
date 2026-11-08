import React, { useEffect, useState, useMemo, useRef } from 'react';
import { getWaveform, previewErrorMessage } from '../../services/previewApi';
import type { WaveformDto } from '../../types/ipc';

export interface TimelineWaveformProps {
  projectId: string;
  mediaId: string;
  sourceInSeconds: number;
  sourceOutSeconds: number;
  speedRatio?: number;
  widthPx: number;
  heightPx?: number;
  tone?: 'cyan' | 'indigo' | 'amber' | 'emerald' | 'slate';
  isMuted?: boolean;
  className?: string;
}

const waveformPeakCache = new Map<string, number[]>();

export function buildWaveformCacheKey(mediaId: string, startSec: number, endSec: number, buckets: number): string {
  return `${mediaId}:${startSec.toFixed(3)}:${endSec.toFixed(3)}:${buckets}`;
}

export function getCachedWaveform(cacheKey: string): number[] | null {
  return waveformPeakCache.get(cacheKey) ?? null;
}

export function setCachedWaveform(cacheKey: string, peaks: number[]): void {
  waveformPeakCache.set(cacheKey, peaks);
}

export function computeWaveformBuckets(widthPx: number): number {
  return Math.max(16, Math.min(500, Math.round(widthPx / 3)));
}

export function clearWaveformCache(): void {
  waveformPeakCache.clear();
}

const TONE_COLORS: Record<string, { bar: string; mirror: string }> = {
  cyan: { bar: '#22d3ee', mirror: 'rgba(34, 211, 238, 0.4)' },
  indigo: { bar: '#818cf8', mirror: 'rgba(129, 140, 248, 0.4)' },
  amber: { bar: '#fbbf24', mirror: 'rgba(251, 191, 36, 0.4)' },
  emerald: { bar: '#34d399', mirror: 'rgba(52, 211, 153, 0.4)' },
  slate: { bar: '#94a3b8', mirror: 'rgba(148, 163, 184, 0.4)' }
};

export const TimelineWaveform: React.FC<TimelineWaveformProps> = ({
  projectId,
  mediaId,
  sourceInSeconds,
  sourceOutSeconds,
  speedRatio = 1,
  widthPx,
  heightPx = 28,
  tone = 'cyan',
  isMuted = false,
  className = ''
}) => {
  const duration = Math.max(0, sourceOutSeconds - sourceInSeconds);
  const effectiveBuckets = computeWaveformBuckets(widthPx);
  const cacheKey = buildWaveformCacheKey(mediaId, sourceInSeconds, sourceOutSeconds, effectiveBuckets);

  const [peaks, setPeaks] = useState<number[] | null>(() => getCachedWaveform(cacheKey));
  const [loading, setLoading] = useState(false);
  const [errorMsg, setErrorMsg] = useState<string | null>(null);
  const activeRequestRef = useRef<string | null>(null);

  useEffect(() => {
    if (!projectId || !mediaId || duration <= 0 || widthPx < 8) {
      setPeaks(null);
      setLoading(false);
      setErrorMsg(null);
      return;
    }

    const cached = getCachedWaveform(cacheKey);
    if (cached) {
      setPeaks(cached);
      setLoading(false);
      setErrorMsg(null);
      return;
    }

    let isMounted = true;
    activeRequestRef.current = cacheKey;
    setLoading(true);
    setErrorMsg(null);

    getWaveform({
      projectId,
      mediaId,
      startSeconds: sourceInSeconds,
      endSeconds: sourceOutSeconds,
      buckets: effectiveBuckets
    })
      .then((dto: WaveformDto) => {
        if (!isMounted || activeRequestRef.current !== cacheKey) return;
        if (dto.status === 'ready' && dto.peaks && dto.peaks.length > 0) {
          setCachedWaveform(cacheKey, dto.peaks);
          setPeaks(dto.peaks);
          setErrorMsg(null);
        } else {
          setPeaks(null);
          setErrorMsg(previewErrorMessage(dto.errorCode, dto.detail));
        }
      })
      .catch((err: unknown) => {
        if (!isMounted || activeRequestRef.current !== cacheKey) return;
        setPeaks(null);
        setErrorMsg(err instanceof Error ? err.message : 'Falha ao obter forma de onda');
      })
      .finally(() => {
        if (isMounted && activeRequestRef.current === cacheKey) {
          setLoading(false);
        }
      });

    return () => {
      isMounted = false;
    };
  }, [projectId, mediaId, sourceInSeconds, sourceOutSeconds, effectiveBuckets, cacheKey, duration, widthPx]);

  const colors = TONE_COLORS[tone] ?? TONE_COLORS.cyan;

  return (
    <div
      className={`relative w-full h-full overflow-hidden pointer-events-none select-none ${isMuted ? 'opacity-30' : 'opacity-90'} ${className}`}
      style={{ height: heightPx }}
    >
      {peaks && peaks.length > 0 ? (
        <svg
          viewBox={`0 0 ${peaks.length} 100`}
          preserveAspectRatio="none"
          className="w-full h-full block"
          aria-hidden="true"
        >
          {peaks.map((val, idx) => {
            const clamped = Math.max(0.04, Math.min(1.0, val));
            const halfH = clamped * 45;
            const yTop = 50 - halfH;
            const barH = halfH * 2;
            return (
              <rect
                key={idx}
                x={idx}
                y={yTop}
                width={0.7}
                height={barH}
                fill={colors.bar}
                rx={0.2}
              />
            );
          })}
        </svg>
      ) : loading ? (
        <div className="w-full h-full flex items-center justify-center opacity-40">
          <div className="w-full h-[2px] bg-slate-700/60 animate-pulse" />
        </div>
      ) : errorMsg ? (
        <div className="w-full h-full flex items-center justify-center px-1">
          <span className="text-[9px] text-slate-500 italic truncate" title={errorMsg}>
            Waveform indisponível
          </span>
        </div>
      ) : null}
    </div>
  );
};
