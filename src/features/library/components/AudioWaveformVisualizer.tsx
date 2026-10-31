import React, { useEffect, useRef, useState, useMemo } from 'react';
import { loadMediaWaveform, WaveformData } from '../../../services/desktopBridge';

interface AudioWaveformVisualizerProps {
  filePath?: string;
  currentTimeSec: number;
  durationSec: number;
  onSeek?: (timeSec: number) => void;
  className?: string;
  height?: number;
}

export const AudioWaveformVisualizer: React.FC<AudioWaveformVisualizerProps> = ({
  filePath,
  currentTimeSec,
  durationSec,
  onSeek,
  className = '',
  height = 36,
}) => {
  const canvasRef = useRef<HTMLCanvasElement | null>(null);
  const containerRef = useRef<HTMLDivElement | null>(null);
  const [waveformData, setWaveformData] = useState<WaveformData | null>(null);
  const [loading, setLoading] = useState<boolean>(false);

  // Carrega picos de audio via Rust FFmpeg
  useEffect(() => {
    let isMounted = true;
    if (!filePath) {
      setWaveformData(null);
      return;
    }

    setLoading(true);
    loadMediaWaveform(filePath)
      .then((data) => {
        if (isMounted) {
          setWaveformData(data);
          setLoading(false);
        }
      })
      .catch((err) => {
        console.warn('[AudioWaveform] Falha ao carregar waveform:', err);
        if (isMounted) {
          setWaveformData(null);
          setLoading(false);
        }
      });

    return () => {
      isMounted = false;
    };
  }, [filePath]);

  // Se nao houver picos do FFmpeg, gera uma forma de onda organica deterministica baseada na duracao
  const displayPeaks = useMemo<number[]>(() => {
    if (waveformData && waveformData.peaks && waveformData.peaks.length > 0) {
      return waveformData.peaks.map((p) => {
        if (typeof p === 'number') return p;
        if (typeof p === 'object' && p !== null) {
          const val = Math.max(Math.abs(p.max ?? 0), Math.abs(p.min ?? 0), p.rms ?? 0);
          return Math.min(1, Math.max(0.05, val));
        }
        return 0.1;
      });
    }

    // Picos sinteticos para visualizacao fluida de audio
    const totalBars = 100;
    const synthetic: number[] = [];
    const seed = filePath ? filePath.split('').reduce((acc, c) => acc + c.charCodeAt(0), 0) : 42;
    for (let i = 0; i < totalBars; i++) {
      const angle = (i / totalBars) * Math.PI * 4;
      const noise = Math.sin(angle + seed) * 0.3 + Math.cos(i * 0.5) * 0.2 + 0.5;
      synthetic.push(Math.max(0.08, Math.min(0.95, noise)));
    }
    return synthetic;
  }, [waveformData, filePath]);

  // Renderizacao em Canvas com Device Pixel Ratio
  useEffect(() => {
    const canvas = canvasRef.current;
    if (!canvas) return;

    const ctx = canvas.getContext('2d');
    if (!ctx) return;

    const dpr = window.devicePixelRatio || 1;
    const width = canvas.clientWidth || 300;
    const canvasHeight = height;

    canvas.width = width * dpr;
    canvas.height = canvasHeight * dpr;
    ctx.scale(dpr, dpr);

    ctx.clearRect(0, 0, width, canvasHeight);

    const peaks = displayPeaks;
    const numBars = peaks.length;
    if (numBars === 0) return;

    const barSpacing = 1;
    const totalSpacing = (numBars - 1) * barSpacing;
    const barWidth = Math.max(1, (width - totalSpacing) / numBars);

    const progressRatio = durationSec > 0 ? Math.min(1, Math.max(0, currentTimeSec / durationSec)) : 0;
    const playheadX = progressRatio * width;

    // Desenha cada barra do waveform centralizada verticalmente
    const midY = canvasHeight / 2;

    for (let i = 0; i < numBars; i++) {
      const x = i * (barWidth + barSpacing);
      const peakVal = peaks[i];
      const barH = Math.max(2, peakVal * (canvasHeight * 0.85));
      const y = midY - barH / 2;

      const isPlayed = x <= playheadX;

      if (isPlayed) {
        ctx.fillStyle = '#3b82f6'; // blue-500
      } else {
        ctx.fillStyle = '#475569'; // slate-600
      }

      ctx.beginPath();
      // cantos arredondados simples
      ctx.roundRect ? ctx.roundRect(x, y, barWidth, barH, 1) : ctx.rect(x, y, barWidth, barH);
      ctx.fill();
    }

    // Linha do playhead
    ctx.fillStyle = '#ffffff';
    ctx.fillRect(Math.max(0, playheadX - 1), 0, 2, canvasHeight);
  }, [displayPeaks, currentTimeSec, durationSec, height]);

  const handleClick = (e: React.MouseEvent<HTMLDivElement>) => {
    if (!onSeek || durationSec <= 0) return;
    const rect = e.currentTarget.getBoundingClientRect();
    const clickX = e.clientX - rect.left;
    const ratio = Math.max(0, Math.min(1, clickX / rect.width));
    onSeek(ratio * durationSec);
  };

  return (
    <div
      ref={containerRef}
      onClick={handleClick}
      style={{ height }}
      className={`relative w-full cursor-pointer select-none bg-slate-950/70 border border-slate-800/60 rounded-md overflow-hidden flex items-center ${className}`}
      title="Forma de onda de áudio (clique para posicionar a agulha)"
    >
      <canvas
        ref={canvasRef}
        style={{ width: '100%', height: `${height}px` }}
        className="w-full h-full block"
      />
      {loading && (
        <div className="absolute inset-0 bg-slate-950/60 flex items-center justify-center text-[10px] text-slate-400 font-mono">
          Extraindo áudio PCM...
        </div>
      )}
    </div>
  );
};
