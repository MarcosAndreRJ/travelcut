import React from 'react';
import { AlertTriangle } from 'lucide-react';
import type { AnalysisEnvironmentDto } from '../../types/ipc';

interface EnvironmentBannerProps {
  environment: AnalysisEnvironmentDto | null;
  error: string | null;
}

/** Aviso discreto só quando FFmpeg ou Whisper estão indisponíveis. Não bloqueia botões. */
export const EnvironmentBanner: React.FC<EnvironmentBannerProps> = ({ environment, error }) => {
  if (error) {
    return (
      <div role="status" className="px-6 py-1.5 border-b border-slate-800 bg-[#0a1222] text-[11px] text-slate-400 shrink-0">
        Não foi possível verificar o ambiente de análise: {error}
      </div>
    );
  }
  if (!environment || (environment.ffmpegAvailable && environment.whisperAvailable)) return null;

  const missing: string[] = [];
  if (!environment.ffmpegAvailable) missing.push('FFmpeg');
  if (!environment.whisperAvailable) missing.push('Whisper');

  return (
    <div role="status" className="px-6 py-2 border-b border-amber-500/20 bg-amber-500/5 text-[11px] text-amber-200 shrink-0 space-y-1">
      <div className="flex flex-wrap items-center gap-x-3 gap-y-1">
        <AlertTriangle className="w-3.5 h-3.5 text-amber-400 shrink-0" aria-hidden="true" />
        <span className="font-semibold">Ambiente de análise incompleto: {missing.join(', ')} indisponível.</span>
        <span className="font-mono text-amber-300/80">Modelo: {environment.whisperModel}</span>
      </div>
      {environment.reasons.length > 0 ? (
        <ul className="pl-6 list-disc text-amber-100/80 space-y-0.5">
          {environment.reasons.map((reason, idx) => (
            <li key={idx}>{reason}</li>
          ))}
        </ul>
      ) : (
        <p className="pl-6 text-amber-100/80">Motivo não informado pelo host.</p>
      )}
    </div>
  );
};
