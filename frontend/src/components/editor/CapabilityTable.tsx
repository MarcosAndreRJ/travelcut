import React from 'react';
import type { EditorOperationCapability } from '../../types/ipc';
import { operationLabel, verificationBadge } from './editorModel';
import { EditorBadge, EditorSection, tableCellClass, tableHeadClass } from './EditorPrimitives';

interface CapabilityTableProps {
  operations: EditorOperationCapability[];
}

/** Capacidades por operação com o nível de verificação. "Disponível" só quando o host afirma supported. */
export const CapabilityTable: React.FC<CapabilityTableProps> = ({ operations }) => (
  <EditorSection
    title="Capacidades por operação"
    description="O selo indica o nível de verificação. Só simulador não prova integração com o DaVinci real."
    testId="editor-capabilities"
  >
    {operations.length === 0 ? (
      <p className="text-xs text-slate-400">O host não informou nenhuma operação.</p>
    ) : (
      <div className="overflow-x-auto">
        <table className="w-full min-w-[32rem] border-collapse">
          <caption className="sr-only">Capacidades do adaptador do editor por operação</caption>
          <thead>
            <tr>
              <th scope="col" className={tableHeadClass}>Operação</th>
              <th scope="col" className={tableHeadClass}>Disponibilidade</th>
              <th scope="col" className={tableHeadClass}>Verificação</th>
              <th scope="col" className={tableHeadClass}>Nota</th>
            </tr>
          </thead>
          <tbody>
            {operations.map((op) => (
              <tr key={op.operation} data-testid={`capability-${op.operation}`}>
                <td className={tableCellClass}>
                  <span className="font-semibold">{operationLabel(op.operation)}</span>
                  <span className="block font-mono text-[10px] text-slate-500">{op.operation}</span>
                </td>
                <td className={tableCellClass}>
                  {op.supported ? (
                    <span className="text-emerald-300 font-semibold">Disponível</span>
                  ) : (
                    <span className="text-slate-400 font-semibold">Indisponível</span>
                  )}
                </td>
                <td className={tableCellClass}>
                  <EditorBadge badge={verificationBadge(op.verification)} />
                </td>
                <td className={`${tableCellClass} text-slate-400`}>{op.note ?? '—'}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    )}
  </EditorSection>
);
