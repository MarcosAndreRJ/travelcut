import React from 'react';
import type { EditorApplyResult } from '../../types/ipc';
import {
  hasRecordNotSaved,
  hasSnapshot,
  mappingBadge,
  operationLabel,
  resultErrorInfo,
  resultStatusBadge,
  revisionLabel,
  stepStatusBadge,
  formatUtcDateTime
} from './editorModel';
import {
  EditorBadge,
  EditorErrorNotice,
  EditorSection,
  SimulatedBanner,
  tableCellClass,
  tableHeadClass
} from './EditorPrimitives';

interface ApplyResultPanelProps {
  result: EditorApplyResult;
}

/** Resposta de editor.apply. Mostra o resultado devolvido pelo host (ou pelo simulador), sem completar lacunas. */
export const ApplyResultPanel: React.FC<ApplyResultPanelProps> = ({ result }) => {
  const error = resultErrorInfo(result);
  const notSaved = hasRecordNotSaved(result.warnings);

  return (
    <EditorSection
      title="Resultado do envio"
      description={`Aplicação ${result.applicationId} · ${revisionLabel(result.revision)} · iniciada em ${formatUtcDateTime(result.startedAtUtc)}`}
      actions={<EditorBadge badge={resultStatusBadge(result.status)} />}
      testId="editor-result"
    >
      {result.isSimulated && <SimulatedBanner message="Resultado simulado: nenhum projeto, timeline ou arquivo foi criado no DaVinci Resolve." />}

      {result.reusedPrevious && (
        <p role="status" className="text-xs text-cyan-200 bg-cyan-950/40 border border-cyan-500/30 rounded-lg px-3 py-2">
          Resultado anterior reaproveitado: esta chave de envio já tinha sido aplicada a esta montagem.
        </p>
      )}

      {error && <EditorErrorNotice error={error} title="O envio terminou com erro" />}

      {notSaved && (
        <p role="alert" className="text-xs text-rose-200 bg-rose-950/50 border border-rose-500/40 rounded-lg px-3 py-2">
          <span className="font-mono text-[10px] mr-1">RECORD_NOT_SAVED</span>
          O resultado no editor não foi registrado em 3-Trabalho/editor. Confira o projeto no DaVinci antes de repetir.
        </p>
      )}

      <dl className="grid grid-cols-1 md:grid-cols-2 gap-x-6 gap-y-2 text-xs">
        <div className="space-y-0.5 min-w-0">
          <dt className="text-[10px] uppercase tracking-wider text-slate-500">Projeto no editor</dt>
          <dd className="font-mono text-slate-200 break-all">{result.editorProjectName ?? '—'}</dd>
        </div>
        <div className="space-y-0.5 min-w-0">
          <dt className="text-[10px] uppercase tracking-wider text-slate-500">Timeline</dt>
          <dd className="font-mono text-slate-200 break-all">{result.timelineName ?? '—'}</dd>
        </div>
        <div className="space-y-0.5 min-w-0">
          <dt className="text-[10px] uppercase tracking-wider text-slate-500">Registro</dt>
          <dd className="font-mono text-slate-400 break-all">{result.recordRelativePath}</dd>
        </div>
        <div className="space-y-0.5">
          <dt className="text-[10px] uppercase tracking-wider text-slate-500">Terminada em</dt>
          <dd className="font-mono text-slate-200">{formatUtcDateTime(result.finishedAtUtc)}</dd>
        </div>
        {hasSnapshot(result) && (
          <div className="space-y-0.5 md:col-span-2 min-w-0">
            <dt className="text-[10px] uppercase tracking-wider text-slate-500">Snapshot .drp</dt>
            <dd className="font-mono text-slate-200 break-all">{result.snapshotRelativePath}</dd>
          </div>
        )}
      </dl>

      <div className="space-y-2">
        <h4 className="text-xs font-semibold text-slate-200">Passos ({result.steps.length})</h4>
        {result.steps.length === 0 ? (
          <p className="text-xs text-slate-400">O host não informou etapas.</p>
        ) : (
          <div className="overflow-x-auto rounded-lg border border-slate-800">
            <table className="w-full min-w-[28rem] border-collapse">
              <caption className="sr-only">Etapas executadas pelo adaptador</caption>
              <thead>
                <tr>
                  <th scope="col" className={tableHeadClass}>Operação</th>
                  <th scope="col" className={tableHeadClass}>Status</th>
                  <th scope="col" className={tableHeadClass}>Mensagem</th>
                </tr>
              </thead>
              <tbody>
                {result.steps.map((step, i) => (
                  <tr key={`${step.operation}-${i}`} data-testid="result-step">
                    <td className={tableCellClass}>{operationLabel(step.operation)}</td>
                    <td className={tableCellClass}>
                      <EditorBadge badge={stepStatusBadge(step.status)} />
                    </td>
                    <td className={`${tableCellClass} text-slate-400`}>{step.message}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      <div className="space-y-2">
        <h4 className="text-xs font-semibold text-slate-200">Mapeamento dos clipes ({result.clipMappings.length})</h4>
        {result.clipMappings.length === 0 ? (
          <p className="text-xs text-slate-400">Nenhum clipe mapeado.</p>
        ) : (
          <div className="overflow-x-auto rounded-lg border border-slate-800">
            <table className="w-full min-w-[32rem] border-collapse">
              <caption className="sr-only">Conferência de clipes enviados contra a timeline observada</caption>
              <thead>
                <tr>
                  <th scope="col" className={tableHeadClass}>Clipe</th>
                  <th scope="col" className={tableHeadClass}>Frame observado</th>
                  <th scope="col" className={tableHeadClass}>Duração observada</th>
                  <th scope="col" className={tableHeadClass}>Conferência</th>
                </tr>
              </thead>
              <tbody>
                {result.clipMappings.map((m) => (
                  <tr key={m.clipId} data-testid="result-mapping">
                    <td className={`${tableCellClass} font-mono break-all max-w-[12rem]`}>{m.clipId}</td>
                    <td className={`${tableCellClass} font-mono`}>{m.observedRecordFrame ?? '—'}</td>
                    <td className={`${tableCellClass} font-mono`}>{m.observedDurationFrames ?? '—'}</td>
                    <td className={tableCellClass}>
                      <EditorBadge badge={mappingBadge(m.matches)} />
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {result.warnings.length > 0 && (
        <div className="rounded-xl border border-amber-500/40 bg-amber-950/30 p-3 space-y-1.5">
          <p className="text-xs font-semibold text-amber-200">Avisos ({result.warnings.length})</p>
          <ul className="space-y-1 text-xs text-amber-200">
            {result.warnings.map((w, i) => (
              <li key={`${w.code}-${i}`} className="flex flex-wrap gap-x-2">
                <span className="font-mono text-[10px] text-amber-300">{w.code}</span>
                <span>{w.message}</span>
              </li>
            ))}
          </ul>
        </div>
      )}

      {result.rejected.length > 0 && (
        <div className="rounded-xl border border-rose-500/40 bg-rose-950/30 p-3 space-y-1.5">
          <p className="text-xs font-semibold text-rose-200">Rejeitados ({result.rejected.length})</p>
          <ul className="space-y-1 text-xs text-rose-200">
            {result.rejected.map((r, i) => (
              <li key={`${r.code}-${i}`} className="flex flex-wrap gap-x-2">
                <span className="font-mono text-[10px] text-rose-300">{r.code}</span>
                <span>{r.message}</span>
              </li>
            ))}
          </ul>
        </div>
      )}
    </EditorSection>
  );
};
