import React from 'react';
import { RotateCcw, ZoomIn, Eye, Crosshair } from 'lucide-react';

export interface EditorialZoomConfig {
  scale: number;          // 1.0, 1.1, 1.15, 1.2, 1.25
  anchor: 'center' | 'top-third' | 'custom';
  offsetX: number;        // -50% a +50%
  offsetY: number;        // -50% a +50%
  mode: 'cut' | 'hold' | 'reset';
  showSafeArea: boolean;
}

export const DEFAULT_ZOOM_CONFIG: EditorialZoomConfig = {
  scale: 1.0,
  anchor: 'center',
  offsetX: 0,
  offsetY: 0,
  mode: 'hold',
  showSafeArea: false
};

interface EditorialZoomControlsProps {
  value: EditorialZoomConfig;
  onChange: (config: EditorialZoomConfig) => void;
  disabled?: boolean;
}

export const PRESET_SCALES = [
  { label: '1.0× (Original)', value: 1.0, desc: 'Sem enquadramento' },
  { label: '1.10× (Suave)', value: 1.1, desc: 'Jump cut sutil' },
  { label: '1.15× (Ênfase)', value: 1.15, desc: 'Aproximação moderada' },
  { label: '1.20× (Foco)', value: 1.2, desc: 'Destaque no rosto' },
  { label: '1.25× (Close)', value: 1.25, desc: 'Close-up editorial' }
];

export const EditorialZoomControls: React.FC<EditorialZoomControlsProps> = ({
  value,
  onChange,
  disabled = false
}) => {
  const setScale = (scale: number) => {
    onChange({ ...value, scale });
  };

  const setAnchor = (anchor: 'center' | 'top-third') => {
    if (anchor === 'center') {
      onChange({ ...value, anchor: 'center', offsetX: 0, offsetY: 0 });
    } else {
      // Terço superior: eleva o enquadramento (-15% no eixo Y) para enquadrar olhos/rosto
      onChange({ ...value, anchor: 'top-third', offsetX: 0, offsetY: -15 });
    }
  };

  const resetZoom = () => {
    onChange({ ...DEFAULT_ZOOM_CONFIG });
  };

  return (
    <section aria-label="Enquadramento e Zoom Editorial" className="rounded-2xl border border-slate-800 bg-[#0a1222] p-4 space-y-3">
      <div className="flex flex-wrap items-center justify-between gap-2 border-b border-slate-800/80 pb-2.5">
        <div className="flex items-center gap-2">
          <ZoomIn className="w-4 h-4 text-cyan-400" aria-hidden="true" />
          <h3 className="text-xs font-bold uppercase tracking-wider text-white">Zoom e Enquadramento Editorial</h3>
          {value.scale > 1.0 && (
            <span className="px-1.5 py-0.5 rounded text-[10px] font-mono font-bold bg-cyan-950/80 border border-cyan-700/60 text-cyan-300">
              {value.scale.toFixed(2)}× ({value.anchor === 'top-third' ? 'Terço Superior' : value.anchor === 'center' ? 'Centro' : 'Personalizado'})
            </span>
          )}
        </div>

        <div className="flex items-center gap-2">
          <button
            type="button"
            onClick={() => onChange({ ...value, showSafeArea: !value.showSafeArea })}
            className={`px-2 py-1 rounded-lg text-[11px] font-semibold flex items-center gap-1.5 border transition-all ${
              value.showSafeArea
                ? 'bg-amber-950/60 border-amber-600/60 text-amber-300'
                : 'bg-slate-900 border-slate-800 text-slate-400 hover:text-slate-200'
            }`}
            title="Alternar guia de margens de segurança (Action Safe 90% / Title Safe 80%)"
          >
            <Crosshair className="w-3 h-3" aria-hidden="true" />
            Margem de Segurança
          </button>

          <button
            type="button"
            onClick={resetZoom}
            disabled={disabled || (value.scale === 1.0 && value.offsetX === 0 && value.offsetY === 0 && !value.showSafeArea)}
            className="px-2 py-1 rounded-lg text-[11px] font-semibold bg-slate-900 border border-slate-800 text-slate-400 hover:text-white disabled:opacity-40 flex items-center gap-1"
            title="Redefinir enquadramento para 1.0x centralizado"
          >
            <RotateCcw className="w-3 h-3" aria-hidden="true" />
            Redefinir (1.0×)
          </button>
        </div>
      </div>

      <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
        {/* Nível de Zoom */}
        <div className="space-y-1.5">
          <label className="text-[11px] font-medium text-slate-300 block">Nível de Zoom</label>
          <div className="flex flex-wrap gap-1.5">
            {PRESET_SCALES.map((preset) => {
              const active = Math.abs(value.scale - preset.value) < 0.01;
              return (
                <button
                  key={preset.value}
                  type="button"
                  onClick={() => setScale(preset.value)}
                  disabled={disabled}
                  title={preset.desc}
                  className={`px-2.5 py-1 rounded-lg text-xs font-mono font-semibold border transition-all ${
                    active
                      ? 'bg-cyan-600 text-white border-cyan-500 shadow-sm'
                      : 'bg-slate-900/90 text-slate-300 border-slate-800 hover:border-slate-700 hover:text-white'
                  }`}
                >
                  {preset.label}
                </button>
              );
            })}
          </div>
        </div>

        {/* Âncora de Enquadramento */}
        <div className="space-y-1.5">
          <label className="text-[11px] font-medium text-slate-300 block">Posição da Âncora</label>
          <div className="flex gap-2">
            <button
              type="button"
              onClick={() => setAnchor('center')}
              disabled={disabled || value.scale <= 1.0}
              className={`flex-1 px-2.5 py-1 rounded-lg text-xs font-semibold border transition-all disabled:opacity-40 ${
                value.anchor === 'center'
                  ? 'bg-cyan-600 text-white border-cyan-500'
                  : 'bg-slate-900/90 text-slate-300 border-slate-800 hover:border-slate-700 hover:text-white'
              }`}
            >
              Centralizado
            </button>
            <button
              type="button"
              onClick={() => setAnchor('top-third')}
              disabled={disabled || value.scale <= 1.0}
              className={`flex-1 px-2.5 py-1 rounded-lg text-xs font-semibold border transition-all disabled:opacity-40 ${
                value.anchor === 'top-third'
                  ? 'bg-cyan-600 text-white border-cyan-500'
                  : 'bg-slate-900/90 text-slate-300 border-slate-800 hover:border-slate-700 hover:text-white'
              }`}
            >
              Terço Superior (Rosto)
            </button>
          </div>
        </div>
      </div>

      {/* Ajuste Fino Offset se zoom estiver ativo */}
      {value.scale > 1.0 && (
        <div className="pt-2 border-t border-slate-800/60 grid grid-cols-1 sm:grid-cols-2 gap-4 text-xs">
          <div className="space-y-1">
            <div className="flex justify-between text-[11px] font-mono text-slate-400">
              <span>Deslocamento Horizontal (X)</span>
              <span>{value.offsetX > 0 ? `+${value.offsetX}%` : `${value.offsetX}%`}</span>
            </div>
            <input
              type="range"
              min={-50}
              max={50}
              step={1}
              value={value.offsetX}
              onChange={(e) => onChange({ ...value, anchor: 'custom', offsetX: Number(e.target.value) })}
              disabled={disabled}
              className="w-full h-1.5 bg-slate-800 rounded-lg appearance-none cursor-pointer accent-cyan-400"
            />
          </div>

          <div className="space-y-1">
            <div className="flex justify-between text-[11px] font-mono text-slate-400">
              <span>Deslocamento Vertical (Y)</span>
              <span>{value.offsetY > 0 ? `+${value.offsetY}%` : `${value.offsetY}%`}</span>
            </div>
            <input
              type="range"
              min={-50}
              max={50}
              step={1}
              value={value.offsetY}
              onChange={(e) => onChange({ ...value, anchor: 'custom', offsetY: Number(e.target.value) })}
              disabled={disabled}
              className="w-full h-1.5 bg-slate-800 rounded-lg appearance-none cursor-pointer accent-cyan-400"
            />
          </div>
        </div>
      )}
    </section>
  );
};
