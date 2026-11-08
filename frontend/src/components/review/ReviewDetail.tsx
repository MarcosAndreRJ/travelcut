import React, { useState } from 'react';
import { AlertTriangle } from 'lucide-react';
import type { EditSuggestion, MediaAnalysisDto } from '../../types/ipc';
import {
  confidenceText,
  confidenceTone,
  formatClock,
  formatSeconds,
  suggestedActionLabel
} from '../analysis/analysisFormat';
import { BeforeAfterCompare, type CompareMode } from './BeforeAfterCompare';
import { DecisionActions } from './DecisionActions';
import { ModifyEditor } from './ModifyEditor';
import { TranscriptContext } from './TranscriptContext';
import { EditorialZoomControls, DEFAULT_ZOOM_CONFIG, type EditorialZoomConfig } from './EditorialZoomControls';
import {
  effectiveInterval,
  isAdjusted,
  requiresHumanReview,
  reviewKindLabel,
  reviewWindowOf,
  statusLabelOf,
  type Interval
} from './reviewModel';
import type { AsyncState } from './useReviewData';

interface ReviewDetailProps {
  projectId: string;
  suggestion: EditSuggestion;
  /** true quando o candidato ativo não aparece na fila filtrada (ex.: acabou de ser decidido). */
  outOfFilter: boolean;
  mediaName: string;
  transcript: AsyncState<MediaAnalysisDto>;
  compareMode: CompareMode;
  onCompareModeChange: (mode: CompareMode) => void;
  seekTarget: number | null;
  currentSeconds: number | null;
  onTimeUpdate: (sourceSeconds: number) => void;
  onSeek: (sourceSeconds: number) => void;
  busy: boolean;
  modifyOpen: boolean;
  onToggleModify: () => void;
  onCloseModify: () => void;
  onModifySubmit: (startSeconds: number, endSeconds: number) => Promise<boolean>;
  onApprove: () => void;
  onReject: () => void;
  onDefer: () => void;
  onReopen: () => void;
  onUndo: () => void;
  onRedo: () => void;
}

const Field: React.FC<{ label: string; children: React.ReactNode }> = ({ label, children }) => (
  <div className="min-w-0">
    <dt className="text-[10px] uppercase tracking-wider font-bold text-slate-500">{label}</dt>
    <dd className="text-xs text-slate-200 mt-0.5 min-w-0 break-words">{children}</dd>
  </div>
);

/** Painel do candidato selecionado. Nenhum dado é inventado: campos ausentes aparecem como "—" ou motivo real. */
export const ReviewDetail: React.FC<ReviewDetailProps> = ({
  projectId,
  suggestion: s,
  outOfFilter,
  mediaName,
  transcript,
  compareMode,
  onCompareModeChange,
  seekTarget,
  currentSeconds,
  onTimeUpdate,
  onSeek,
  busy,
  modifyOpen,
  onToggleModify,
  onCloseModify,
  onModifySubmit,
  onApprove,
  onReject,
  onDefer,
  onReopen,
  onUndo,
  onRedo
}) => {
  const eff: Interval = effectiveInterval(s);
  const adjusted = isAdjusted(s);
  const human = requiresHumanReview(s);
  const originalDuration = s.endSeconds - s.startSeconds;
  const effectiveDuration = eff.end - eff.start;
  const delta = effectiveDuration - originalDuration;
  const approved = s.status === 'Approved';
  const [zoomConfig, setZoomConfig] = useState<EditorialZoomConfig>(DEFAULT_ZOOM_CONFIG);

  return (
    <div className="flex-1 min-w-0 min-h-0 flex flex-col">
      <div className="flex-1 min-h-0 overflow-y-auto p-4 md:p-5 space-y-4">
        {/* Cabeçalho e selos */}
        <header className="flex flex-wrap items-start justify-between gap-3">
          <div className="min-w-0">
            <p className="text-[10px] uppercase tracking-wider text-slate-500 font-bold">Candidato selecionado</p>
            <h2 className="text-base md:text-lg font-bold text-white break-words">{reviewKindLabel(s.kind)}</h2>
            <p className="text-[11px] text-slate-400 font-mono break-all" title={s.mediaId}>
              {mediaName}
            </p>
          </div>
          <div className="flex flex-wrap items-center gap-1.5">
            <span className="px-2 py-1 rounded-lg text-[10px] font-bold uppercase border border-slate-700 text-slate-200 bg-slate-800">
              {statusLabelOf(s)}
            </span>
            {outOfFilter && (
              <span className="px-2 py-1 rounded-lg text-[10px] font-bold uppercase border border-cyan-800/50 text-cyan-200 bg-cyan-950/50">
                Fora do filtro atual
              </span>
            )}
            <span className={`px-2 py-1 rounded-lg text-[10px] font-bold uppercase border ${confidenceTone(s.confidenceBand)}`}>
              Confiança {confidenceText(s.confidenceBand)}
            </span>
            {human && (
              <span
                role="status"
                className="flex items-center gap-1 px-2 py-1 rounded-lg text-[10px] font-bold uppercase border border-amber-500/40 bg-amber-500/10 text-amber-200"
              >
                <AlertTriangle className="w-3.5 h-3.5 text-amber-400" aria-hidden="true" />
                Revisão humana obrigatória
              </span>
            )}
          </div>
        </header>

        {modifyOpen && (
          <ModifyEditor
            key={s.suggestionId}
            suggestion={s}
            busy={busy}
            onSubmit={onModifySubmit}
            onCancel={onCloseModify}
          />
        )}

        {/* Vídeo e comparação primeiro: o usuário ouve e vê a transição antes de decidir. */}
        <BeforeAfterCompare
          projectId={projectId}
          mediaId={s.mediaId}
          interval={eff}
          mode={compareMode}
          onModeChange={onCompareModeChange}
          seekTarget={seekTarget}
          currentSeconds={currentSeconds}
          onTimeUpdate={onTimeUpdate}
          zoomTransform={zoomConfig}
        />

        {/* Controles de enquadramento / zoom editorial ao vivo (F14.1 Checkpoint E) */}
        <EditorialZoomControls
          value={zoomConfig}
          onChange={setZoomConfig}
          disabled={busy}
        />

        {/* Intervalos e resumo da decisão */}
        <section aria-label="Intervalo de origem" className="rounded-2xl border border-slate-800 bg-[#0a1222] p-4 space-y-3">
          <dl className="grid grid-cols-2 lg:grid-cols-4 gap-x-4 gap-y-3">
            <Field label="Intervalo original">
              <span className="font-mono">
                {formatClock(s.startSeconds)} → {formatClock(s.endSeconds)}
              </span>
              <span className="block text-[10px] text-slate-500 font-mono">{formatSeconds(originalDuration)}</span>
            </Field>
            <Field label={adjusted ? 'Intervalo efetivo (ajustado)' : 'Intervalo efetivo'}>
              <span className={`font-mono ${adjusted ? 'text-cyan-200' : ''}`}>
                {formatClock(eff.start)} → {formatClock(eff.end)}
              </span>
              <span className="block text-[10px] text-slate-500 font-mono">
                {formatSeconds(effectiveDuration)}
                {adjusted && ` (${delta >= 0 ? '+' : ''}${delta.toFixed(1)} s vs. original)`}
              </span>
            </Field>
            <Field label="Ação sugerida">{suggestedActionLabel(s.suggestedAction)}</Field>
            <Field label="Origem da sugestão">
              <span className="font-mono break-all">{s.author}</span>
            </Field>
          </dl>
          <p className="text-[10px] text-slate-500">
            Confiança qualitativa (heurística não calibrada). Aprovação registra o candidato por ID; o intervalo efetivo é o
            que o host gravou.
          </p>
        </section>

        <section aria-label="Justificativa" className="rounded-2xl border border-slate-800 bg-[#0a1222] p-4 space-y-1.5">
          <h3 className="text-[11px] font-bold uppercase tracking-wider text-slate-300">Justificativa</h3>
          <p className="text-xs leading-relaxed text-slate-300">{s.rationale || 'Sem justificativa registrada pelo host.'}</p>
        </section>

        <TranscriptContext
          state={transcript}
          interval={eff}
          reviewWindow={reviewWindowOf(s)}
          approved={approved}
          onSeek={onSeek}
        />
      </div>

      <footer className="shrink-0 border-t border-slate-800 bg-[#0a1424] p-3 md:px-5">
        <DecisionActions
          suggestion={s}
          busy={busy}
          modifyOpen={modifyOpen}
          onApprove={onApprove}
          onReject={onReject}
          onDefer={onDefer}
          onReopen={onReopen}
          onToggleModify={onToggleModify}
          onUndo={onUndo}
          onRedo={onRedo}
        />
      </footer>
    </div>
  );
};
