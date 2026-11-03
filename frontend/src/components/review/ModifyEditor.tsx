import React, { useState } from 'react';
import type { EditSuggestion } from '../../types/ipc';
import { formatSeconds } from '../analysis/analysisFormat';
import {
  FRAME_STEP_SECONDS,
  TENTH_STEP_SECONDS,
  effectiveInterval,
  formatDraft,
  reviewWindowOf,
  stepDraft,
  validateModifyDraft
} from './reviewModel';

interface ModifyEditorProps {
  /** Candidato ativo. Usar com key={suggestionId} para reiniciar o rascunho ao trocar de item. */
  suggestion: EditSuggestion;
  busy: boolean;
  onSubmit: (startSeconds: number, endSeconds: number) => Promise<boolean>;
  onCancel: () => void;
}

const fieldButton =
  'px-2 py-1 rounded-md text-[10px] font-semibold bg-[#070d18] border border-slate-700 text-slate-300 hover:border-cyan-500 hover:text-cyan-200 disabled:opacity-50 focus-visible:outline focus-visible:outline-2 focus-visible:outline-cyan-400';

/**
 * Editor de intervalo ("Modificar" = aprovar com intervalo ajustado).
 * Validação local: entrada < saída e ambos dentro da janela de revisão. O host valida de novo.
 */
export const ModifyEditor: React.FC<ModifyEditorProps> = ({ suggestion, busy, onSubmit, onCancel }) => {
  const eff = effectiveInterval(suggestion);
  const allowed = reviewWindowOf(suggestion);
  const [inText, setInText] = useState<string>(formatDraft(eff.start));
  const [outText, setOutText] = useState<string>(formatDraft(eff.end));

  const validation = validateModifyDraft(inText, outText, allowed);
  const draftDuration = validation.ok ? validation.end - validation.start : null;

  const submit = async () => {
    if (!validation.ok) return;
    await onSubmit(validation.start, validation.end);
  };

  const resetToOriginal = () => {
    setInText(formatDraft(suggestion.startSeconds));
    setOutText(formatDraft(suggestion.endSeconds));
  };

  const renderField = (
    id: string,
    label: string,
    value: string,
    setValue: (next: string) => void
  ) => (
    <div className="space-y-1.5">
      <label htmlFor={id} className="block text-[10px] font-bold uppercase tracking-wider text-slate-400">
        {label} (s)
      </label>
      <input
        id={id}
        type="number"
        inputMode="decimal"
        step={TENTH_STEP_SECONDS}
        min={0}
        value={value}
        onChange={(e) => setValue(e.target.value)}
        disabled={busy}
        className="w-full rounded-lg bg-[#070d18] border border-slate-700 px-2.5 py-1.5 font-mono text-sm text-white focus-visible:outline focus-visible:outline-2 focus-visible:outline-cyan-400 disabled:opacity-50"
      />
      <div className="flex flex-wrap gap-1">
        <button type="button" className={fieldButton} disabled={busy} onClick={() => setValue(stepDraft(value, -TENTH_STEP_SECONDS))} aria-label={`${label}: diminuir 0,1 segundo`}>
          −0,1 s
        </button>
        <button type="button" className={fieldButton} disabled={busy} onClick={() => setValue(stepDraft(value, TENTH_STEP_SECONDS))} aria-label={`${label}: aumentar 0,1 segundo`}>
          +0,1 s
        </button>
        <button type="button" className={fieldButton} disabled={busy} onClick={() => setValue(stepDraft(value, -FRAME_STEP_SECONDS))} aria-label={`${label}: diminuir 1 quadro`}>
          −1 frame
        </button>
        <button type="button" className={fieldButton} disabled={busy} onClick={() => setValue(stepDraft(value, FRAME_STEP_SECONDS))} aria-label={`${label}: aumentar 1 quadro`}>
          +1 frame
        </button>
      </div>
    </div>
  );

  return (
    <section aria-label="Editor de intervalo" className="rounded-2xl border border-cyan-500/40 bg-[#0a1424] p-4 space-y-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h3 className="text-xs font-bold text-white">Modificar intervalo</h3>
        <span className="text-[10px] font-mono text-slate-400">
          Janela permitida: {formatDraft(allowed.start)} s → {formatDraft(allowed.end)} s · 1 frame = 1/30 s
        </span>
      </div>

      <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
        {renderField('review-modify-in', 'Entrada', inText, setInText)}
        {renderField('review-modify-out', 'Saída', outText, setOutText)}
      </div>

      <p className="text-[11px] text-slate-400">
        Duração do ajuste:{' '}
        <span className="font-mono text-slate-200">{draftDuration === null ? '—' : formatSeconds(draftDuration)}</span>
        {' · '}original: <span className="font-mono text-slate-200">{formatSeconds(suggestion.endSeconds - suggestion.startSeconds)}</span>
      </p>

      {!validation.ok && (
        <p role="alert" className="text-[11px] font-semibold text-amber-300">
          {validation.message}
        </p>
      )}

      <div className="flex flex-wrap gap-2">
        <button
          type="button"
          onClick={submit}
          disabled={busy || !validation.ok}
          className="px-3.5 py-1.5 rounded-xl bg-cyan-600 hover:bg-cyan-500 disabled:opacity-50 text-white text-xs font-semibold focus-visible:outline focus-visible:outline-2 focus-visible:outline-white"
        >
          {busy ? 'Gravando...' : 'Aplicar ajuste'}
        </button>
        <button
          type="button"
          onClick={resetToOriginal}
          disabled={busy}
          className="px-3 py-1.5 rounded-xl border border-slate-700 text-slate-300 text-xs font-semibold hover:border-slate-500 disabled:opacity-50"
        >
          Usar intervalo original
        </button>
        <button
          type="button"
          onClick={onCancel}
          disabled={busy}
          className="px-3 py-1.5 rounded-xl text-slate-400 text-xs font-semibold hover:text-white disabled:opacity-50"
        >
          Cancelar
        </button>
      </div>
    </section>
  );
};
