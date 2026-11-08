import React from 'react';
import { Eye, CheckCircle, Clock, Video } from 'lucide-react';

export interface EffectPreviewOverlayProps {
  zoomScale?: number;
  focalPointX?: number; // 0..1
  focalPointY?: number; // 0..1
  isPushIn?: boolean;
  fadeOpacity?: number; // 0..1
  effectType?: string;
  isSuggested?: boolean;
  isApproved?: boolean;
  isActiveInPreview?: boolean;
  isExportSupported?: boolean;
  exportLimitationNote?: string | null;
}

export const EffectPreviewOverlay: React.FC<EffectPreviewOverlayProps> = ({
  zoomScale = 1.0,
  focalPointX = 0.5,
  focalPointY = 0.5,
  isPushIn = false,
  fadeOpacity = 1.0,
  effectType,
  isSuggested = true,
  isApproved = false,
  isActiveInPreview = true,
  isExportSupported = true,
  exportLimitationNote
}) => {
  const originX = `${focalPointX * 100}%`;
  const originY = `${focalPointY * 100}%`;

  return (
    <div className="pointer-events-none absolute inset-0 overflow-hidden flex flex-col justify-between p-3">
      {/* Badges de estado editorial e suporte de exportação */}
      <div className="flex flex-wrap items-center gap-1.5 z-20">
        {isSuggested && (
          <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[10px] font-semibold bg-amber-500/20 text-amber-300 border border-amber-500/30">
            <Clock className="w-3 h-3" />
            Sugerido
          </span>
        )}
        {isApproved && (
          <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[10px] font-semibold bg-emerald-500/20 text-emerald-300 border border-emerald-500/30">
            <CheckCircle className="w-3 h-3" />
            Aprovado
          </span>
        )}
        {isActiveInPreview && (
          <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[10px] font-semibold bg-cyan-500/20 text-cyan-300 border border-cyan-500/30">
            <Eye className="w-3 h-3" />
            Ativo na prévia
          </span>
        )}
        <span
          className={`inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[10px] font-semibold border ${
            isExportSupported
              ? 'bg-blue-500/20 text-blue-300 border-blue-500/30'
              : 'bg-red-500/20 text-red-300 border-red-500/30'
          }`}
          title={exportLimitationNote ?? undefined}
        >
          <Video className="w-3 h-3" />
          {isExportSupported ? 'Suportado no DaVinci' : 'Limitação no DaVinci'}
        </span>
      </div>

      {/* Nota explícita de limitação quando a composição de reprodução ou export não for suportada */}
      {!isExportSupported && exportLimitationNote && (
        <div className="self-end max-w-sm p-2 rounded bg-slate-900/90 border border-red-500/40 text-[11px] text-red-200 shadow-lg pointer-events-auto z-20">
          <p className="font-semibold text-red-300 mb-0.5">Aviso de Exportação DaVinci:</p>
          <p>{exportLimitationNote}</p>
        </div>
      )}
    </div>
  );
};
