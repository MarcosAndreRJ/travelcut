import React, { useState, useEffect } from 'react';
import { AlertTriangle, Check, ShieldAlert, Sparkles, X } from 'lucide-react';
import { ipc } from '../../services/ipcBridge';

export interface TransitionEditorModalProps {
  projectId: string;
  mediaIdA: string;
  mediaIdB: string;
  isOpen: boolean;
  onClose: () => void;
  onCreated?: (transition: any) => void;
}

const TRANSITION_TYPES = [
  { value: 'cut', label: 'Corte Seco (Cut)' },
  { value: 'video_fade_in', label: 'Video Fade In' },
  { value: 'video_fade_out', label: 'Video Fade Out' },
  { value: 'video_dissolve', label: 'Video Dissolve' },
  { value: 'audio_fade_in', label: 'Audio Fade In' },
  { value: 'audio_fade_out', label: 'Audio Fade Out' },
  { value: 'audio_crossfade', label: 'Audio Crossfade' }
];

const PRESETS = [
  { value: 'short', label: 'Curto (0.2s)', seconds: 0.2 },
  { value: 'standard', label: 'Padrão (0.4s)', seconds: 0.4 },
  { value: 'smooth', label: 'Suave (0.8s)', seconds: 0.8 },
  { value: 'custom', label: 'Personalizado', seconds: 0.4 }
];

export const TransitionEditorModal: React.FC<TransitionEditorModalProps> = ({
  projectId,
  mediaIdA,
  mediaIdB,
  isOpen,
  onClose,
  onCreated
}) => {
  const [type, setType] = useState('video_dissolve');
  const [preset, setPreset] = useState('standard');
  const [customSeconds, setCustomSeconds] = useState(0.4);
  const [validating, setValidating] = useState(false);
  const [validateResult, setValidateResult] = useState<any>(null);
  const [saving, setSaving] = useState(false);
  const [errorMsg, setErrorMsg] = useState<string | null>(null);

  const durationSeconds = preset === 'custom' ? customSeconds : (PRESETS.find((p) => p.value === preset)?.seconds ?? 0.4);

  useEffect(() => {
    if (!isOpen || !mediaIdA || !mediaIdB) return;
    let active = true;
    setValidating(true);
    ipc
      .send<any>('transition.validateHandles', {
        projectId,
        mediaIdA,
        mediaIdB,
        durationSeconds,
        type
      })
      .then((res) => {
        if (active) setValidateResult(res);
      })
      .catch(() => {
        if (active) {
          setValidateResult({
            isValid: true,
            hasEnoughHandles: true,
            warningMessage: null,
            alternativeAction: null
          });
        }
      })
      .finally(() => {
        if (active) setValidating(false);
      });
    return () => {
      active = false;
    };
  }, [isOpen, projectId, mediaIdA, mediaIdB, durationSeconds, type]);

  if (!isOpen) return null;

  const handleCreate = async () => {
    setSaving(true);
    setErrorMsg(null);
    try {
      const res = await ipc.send<any>('transition.createManual', {
        projectId,
        mediaIdA,
        mediaIdB,
        type,
        preset,
        durationSeconds
      });
      if (onCreated) onCreated(res?.transition);
      onClose();
    } catch (err: any) {
      setErrorMsg(err?.message || 'Falha ao criar transição.');
    } finally {
      setSaving(false);
    }
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/70 backdrop-blur-sm p-4">
      <div className="w-full max-w-md rounded-xl border border-slate-700 bg-[#0c1427] shadow-2xl overflow-hidden flex flex-col">
        <div className="flex items-center justify-between px-5 py-3.5 border-b border-slate-800 bg-slate-900/50">
          <div className="flex items-center gap-2">
            <Sparkles className="w-4 h-4 text-cyan-400" />
            <h3 className="text-sm font-semibold text-slate-100">Adicionar Transição Editorial</h3>
          </div>
          <button type="button" onClick={onClose} className="text-slate-400 hover:text-slate-200">
            <X className="w-4 h-4" />
          </button>
        </div>

        <div className="p-5 space-y-4">
          <div>
            <label className="block text-[11px] font-medium text-slate-300 mb-1">Tipo de Transição</label>
            <select
              value={type}
              onChange={(e) => setType(e.target.value)}
              className="w-full rounded bg-slate-800/80 border border-slate-700 text-xs text-slate-100 px-2.5 py-1.5 focus:border-cyan-500"
            >
              {TRANSITION_TYPES.map((t) => (
                <option key={t.value} value={t.value}>{t.label}</option>
              ))}
            </select>
          </div>

          <div>
            <label className="block text-[11px] font-medium text-slate-300 mb-1">Duração / Preset</label>
            <div className="grid grid-cols-2 gap-2 mb-2">
              {PRESETS.map((p) => (
                <button
                  key={p.value}
                  type="button"
                  onClick={() => setPreset(p.value)}
                  className={`px-2.5 py-1.5 rounded text-xs border text-left transition-all ${
                    preset === p.value
                      ? 'bg-cyan-950/40 border-cyan-500 text-cyan-200'
                      : 'bg-slate-800/50 border-slate-700 text-slate-300 hover:bg-slate-800'
                  }`}
                >
                  {p.label}
                </button>
              ))}
            </div>

            {preset === 'custom' && (
              <div className="flex items-center gap-2 mt-2">
                <input
                  type="number"
                  min={0.1}
                  max={5.0}
                  step={0.05}
                  value={customSeconds}
                  onChange={(e) => setCustomSeconds(parseFloat(e.target.value) || 0.4)}
                  className="w-24 rounded bg-slate-800 border border-slate-700 text-xs text-slate-100 px-2 py-1"
                />
                <span className="text-xs text-slate-400">segundos ({Math.round(customSeconds * 30)} frames)</span>
              </div>
            )}
          </div>

          {validating && (
            <p className="text-[11px] text-slate-400">Verificando handles de sobreposição...</p>
          )}

          {validateResult && !validateResult.hasEnoughHandles && (
            <div className="rounded-lg border border-amber-500/40 bg-amber-950/20 p-3 space-y-1">
              <div className="flex items-center gap-2 text-amber-300 text-xs font-semibold">
                <AlertTriangle className="w-4 h-4 shrink-0 text-amber-400" />
                <span>Material Insuficiente (Handles)</span>
              </div>
              <p className="text-[11px] text-amber-200/80 leading-relaxed">
                {validateResult.warningMessage}
              </p>
              {validateResult.alternativeAction && (
                <p className="text-[11px] text-slate-300 mt-1 italic">
                  Alternativa recomendada: {validateResult.alternativeAction}
                </p>
              )}
            </div>
          )}

          {errorMsg && (
            <div className="rounded-lg border border-red-500/40 bg-red-950/20 p-2.5 flex items-center gap-2 text-xs text-red-300">
              <ShieldAlert className="w-4 h-4 shrink-0 text-red-400" />
              <span>{errorMsg}</span>
            </div>
          )}
        </div>

        <div className="flex items-center justify-end gap-2 px-5 py-3 border-t border-slate-800 bg-slate-900/40">
          <button
            type="button"
            onClick={onClose}
            className="px-3 py-1.5 rounded text-xs text-slate-300 hover:bg-slate-800 border border-slate-700"
          >
            Cancelar
          </button>
          <button
            type="button"
            onClick={handleCreate}
            disabled={saving}
            className="px-4 py-1.5 rounded text-xs font-semibold bg-cyan-600 hover:bg-cyan-500 text-white disabled:opacity-50 flex items-center gap-1.5"
          >
            <Check className="w-3.5 h-3.5" />
            {saving ? 'Aplicando...' : 'Aplicar Transição'}
          </button>
        </div>
      </div>
    </div>
  );
};
