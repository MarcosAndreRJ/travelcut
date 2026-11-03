import React from 'react';
import type { EditorApplyResult } from '../../types/ipc';
import { formatUtcDateTime, resultStatusBadge, revisionLabel, type EditorErrorInfo } from './editorModel';
import {
  EditorBadge,
  EditorErrorNotice,
  EditorSection,
  secondaryButtonClass,
  tableCellClass,
  tableHeadClass
} from './EditorPrimitives';
import type { HistoryStatus } from './useEditorApply';

interface ApplicationHistoryProps {
  history: EditorApplyResult[];
  status: HistoryStatus;
  error: EditorErrorInfo | null;
  onReload: () => void;
}

const modeText = (adapterMode: string): string => (adapterMode === 'mock' ? 'Simulador' : 'DaVinci real');

/** Histórico de envios do projeto (editor.applications), mais recente primeiro. */
export const ApplicationHistory: React.FC<ApplicationHistoryProps> = ({ history, status, error, onReload }) => (
  <EditorSection
    title="Histórico de envios"
    description="Registros gravados em 3-Trabalho/editor do projeto. Mais recente primeiro."
    actions={
      <button type="button" onClick={onReload} disabled={status === 'loading'} className={secondaryButtonClass}>
        {status === 'loading' ? 'Carregando…' : 'Atualizar histórico'}
      </button>
    }
    testId="editor-history"
  >
    {status === 'error' && error && <EditorErrorNotice error={error} title="Não foi possível ler o histórico" />}

    {status === 'loading' && history.length === 0 && (
      <p role="status" className="text-xs text-slate-400">Carregando histórico de envios…</p>
    )}

    {status === 'ready' && history.length === 0 && (
      <p className="text-xs text-slate-400">Nenhum envio registrado neste projeto.</p>
    )}

    {history.length > 0 && (
      <div className="overflow-x-auto rounded-lg border border-slate-800">
        <table className="w-full min-w-[36rem] border-collapse">
          <caption className="sr-only">Envios anteriores do projeto</caption>
          <thead>
            <tr>
              <th scope="col" className={tableHeadClass}>Data</th>
              <th scope="col" className={tableHeadClass}>Revisão</th>
              <th scope="col" className={tableHeadClass}>Status</th>
              <th scope="col" className={tableHeadClass}>Modo</th>
            </tr>
          </thead>
          <tbody>
            {history.map((app) => (
              <tr key={app.applicationId} data-testid="history-row">
                <td className={`${tableCellClass} font-mono`}>{formatUtcDateTime(app.startedAtUtc)}</td>
                <td className={`${tableCellClass} font-mono`}>{revisionLabel(app.revision)}</td>
                <td className={tableCellClass}>
                  <EditorBadge badge={resultStatusBadge(app.status)} />
                  {app.reusedPrevious && <span className="ml-1 text-[10px] text-cyan-300">reaproveitado</span>}
                </td>
                <td className={`${tableCellClass}`}>{modeText(app.adapterMode)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    )}
  </EditorSection>
);
