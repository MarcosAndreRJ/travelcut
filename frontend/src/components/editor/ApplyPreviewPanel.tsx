import React from 'react';
import { AlertTriangle, Ban, Send } from 'lucide-react';
import type { EditorApplyPreview } from '../../types/ipc';
import {
  blockingReasonLabel,
  resultStatusBadge,
  revisionLabel,
  SWITCH_CONFIRMATION_TEXT,
  sendBlockedReason
} from './editorModel';
import { EditorBadge, EditorSection, primaryButtonClass, tableCellClass, tableHeadClass } from './EditorPrimitives';

interface ApplyPreviewPanelProps {
  preview: EditorApplyPreview;
  confirmed: boolean;
  onConfirmChange: (value: boolean) => void;
  sending: boolean;
  onSend: () => void;
}

/** Resultado do dry-run: o que seria criado no DaVinci. Nada aqui é enviado sem o botão de envio. */
export const ApplyPreviewPanel: React.FC<ApplyPreviewPanelProps> = ({
  preview,
  confirmed,
  onConfirmChange,
  sending,
  onSend
}) => {
  const blockedReason = sendBlockedReason(preview, confirmed, sending);
  const sendEnabled = blockedReason === null;

  return (
    <EditorSection
      title="Simulação seca"
      description="Mostra o que seria criado no DaVinci. Nenhum projeto ou timeline é criado nesta etapa."
      actions={<EditorBadge badge={resultStatusBadge(preview.status)} />}
      testId="editor-preview"
    >
      <dl className="grid grid-cols-1 md:grid-cols-2 gap-x-6 gap-y-2 text-xs">
        <div className="space-y-0.5 min-w-0">
          <dt className="text-[10px] uppercase tracking-wider text-slate-500">Projeto que será criado</dt>
          <dd className="font-mono text-slate-200 break-all">{preview.editorProjectName}</dd>
        </div>
        <div className="space-y-0.5 min-w-0">
          <dt className="text-[10px] uppercase tracking-wider text-slate-500">Timeline</dt>
          <dd className="font-mono text-slate-200 break-all">{preview.timelineName}</dd>
        </div>
        <div className="space-y-0.5">
          <dt className="text-[10px] uppercase tracking-wider text-slate-500">Taxa da timeline</dt>
          <dd className="font-mono text-slate-200">{preview.timelineFrameRate} fps</dd>
        </div>
        <div className="space-y-0.5">
          <dt className="text-[10px] uppercase tracking-wider text-slate-500">Revisão do plano</dt>
          <dd className="font-mono text-slate-200">{revisionLabel(preview.revision)}</dd>
        </div>
        <div className="space-y-0.5 md:col-span-2 min-w-0">
          <dt className="text-[10px] uppercase tracking-wider text-slate-500">Manifesto para aplicação manual no host</dt>
          <dd className="font-mono text-slate-300 break-all">{preview.manifestRelativePath}</dd>
        </div>
      </dl>

      {preview.blockingReasons.length > 0 && (
        <div role="alert" className="rounded-xl border border-rose-500/40 bg-rose-950/40 p-3 space-y-1.5">
          <p className="text-xs font-semibold text-rose-200 flex items-center gap-1.5">
            <Ban className="w-3.5 h-3.5" aria-hidden="true" /> Bloqueios para o envio
          </p>
          <ul className="space-y-1 text-xs text-rose-200 list-disc pl-5">
            {preview.blockingReasons.map((code) => (
              <li key={code}>
                <span className="font-mono text-[10px] text-rose-300 mr-1">{code}</span>
                {blockingReasonLabel(code)}
              </li>
            ))}
          </ul>
        </div>
      )}

      <div className="space-y-2">
        <h4 className="text-xs font-semibold text-slate-200">Clipes ({preview.clips.length})</h4>
        {preview.clips.length === 0 ? (
          <p className="text-xs text-slate-400">Nenhum clipe na montagem.</p>
        ) : (
          <div className="overflow-x-auto max-h-72 overflow-y-auto rounded-lg border border-slate-800">
            <table className="w-full min-w-[40rem] border-collapse">
              <caption className="sr-only">Clipes da montagem com frames da fonte e da timeline</caption>
              <thead className="sticky top-0 bg-[#0a1222]">
                <tr>
                  <th scope="col" className={tableHeadClass}>Arquivo</th>
                  <th scope="col" className={tableHeadClass}>Entrada (fonte, frame)</th>
                  <th scope="col" className={tableHeadClass}>Saída (fonte, frame exclusivo)</th>
                  <th scope="col" className={tableHeadClass}>Posição (timeline, frame)</th>
                  <th scope="col" className={tableHeadClass}>Duração (frames)</th>
                </tr>
              </thead>
              <tbody>
                {preview.clips.map((clip) => (
                  <tr key={clip.clipId} data-testid="preview-clip">
                    <td className={`${tableCellClass} font-mono break-all max-w-[16rem]`}>{clip.relativePath}</td>
                    <td className={`${tableCellClass} font-mono`}>{clip.sourceStartFrame}</td>
                    <td className={`${tableCellClass} font-mono`}>{clip.sourceEndFrameExclusive}</td>
                    <td className={`${tableCellClass} font-mono`}>{clip.recordFrame}</td>
                    <td className={`${tableCellClass} font-mono`}>{clip.durationFrames}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {preview.rejected.length > 0 && (
        <div className="rounded-xl border border-rose-500/40 bg-rose-950/30 p-3 space-y-1.5">
          <p className="text-xs font-semibold text-rose-200">Rejeitados ({preview.rejected.length})</p>
          <ul className="space-y-1 text-xs text-rose-200">
            {preview.rejected.map((r, i) => (
              <li key={`${r.code}-${i}`} className="flex flex-wrap gap-x-2">
                <span className="font-mono text-[10px] text-rose-300">{r.code}</span>
                <span>
                  {r.message}
                  {r.clipId ? ` (clipe ${r.clipId})` : ''}
                </span>
              </li>
            ))}
          </ul>
        </div>
      )}

      {preview.warnings.length > 0 && (
        <div className="rounded-xl border border-amber-500/40 bg-amber-950/30 p-3 space-y-1.5">
          <p className="text-xs font-semibold text-amber-200 flex items-center gap-1.5">
            <AlertTriangle className="w-3.5 h-3.5" aria-hidden="true" /> Avisos ({preview.warnings.length})
          </p>
          <ul className="space-y-1 text-xs text-amber-200">
            {preview.warnings.map((w, i) => (
              <li key={`${w.code}-${i}`} className="flex flex-wrap gap-x-2">
                <span className="font-mono text-[10px] text-amber-300">{w.code}</span>
                <span>
                  {w.message}
                  {w.clipId ? ` (clipe ${w.clipId})` : ''}
                </span>
              </li>
            ))}
          </ul>
        </div>
      )}

      <div className="rounded-xl border border-slate-800 bg-[#070d18] p-3 space-y-3">
        {preview.requiresProjectSwitchConfirmation && (
          <label className="flex items-start gap-2 text-xs text-slate-200 cursor-pointer">
            <input
              type="checkbox"
              checked={confirmed}
              onChange={(e) => onConfirmChange(e.target.checked)}
              disabled={sending}
              className="mt-0.5 h-4 w-4 accent-cyan-500 focus:outline-none focus-visible:ring-2 focus-visible:ring-cyan-400"
              data-testid="switch-confirmation"
            />
            <span>{SWITCH_CONFIRMATION_TEXT}</span>
          </label>
        )}

        <div className="flex flex-wrap items-center gap-3">
          <button
            type="button"
            onClick={onSend}
            disabled={!sendEnabled}
            className={`${primaryButtonClass} flex items-center gap-1.5`}
          >
            <Send className="w-3.5 h-3.5" aria-hidden="true" />
            <span>{sending ? 'Enviando…' : 'Enviar ao DaVinci'}</span>
          </button>
          {blockedReason && !sending && (
            <p className="text-[11px] text-slate-400">{blockedReason}</p>
          )}
        </div>
      </div>
    </EditorSection>
  );
};
