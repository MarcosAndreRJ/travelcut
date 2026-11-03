import React from 'react';
import { RefreshCw } from 'lucide-react';
import type { EditorCapabilities } from '../../types/ipc';
import {
  ENVIRONMENT_STATE_LABEL,
  editorModeLabel,
  editorStatusBadge,
  editorVersionLabel,
  formatUtcDateTime,
  type EditorErrorInfo
} from './editorModel';
import { EditorBadge, EditorErrorNotice, EditorSection, secondaryButtonClass } from './EditorPrimitives';

interface EnvironmentCardProps {
  caps: EditorCapabilities | null;
  refreshing: boolean;
  refreshError: EditorErrorInfo | null;
  onRefresh: () => void;
}

/** Estado real do ambiente vindo de editor.capabilities. Simulador aparece como simulador, nunca como conectado. */
export const EnvironmentCard: React.FC<EnvironmentCardProps> = ({ caps, refreshing, refreshError, onRefresh }) => {
  const refreshButton = (
    <button
      type="button"
      onClick={onRefresh}
      disabled={refreshing}
      className={`${secondaryButtonClass} flex items-center gap-1.5`}
    >
      <RefreshCw className={`w-3.5 h-3.5 ${refreshing ? 'animate-spin' : ''}`} aria-hidden="true" />
      <span>{refreshing ? 'Verificando…' : 'Verificar novamente'}</span>
    </button>
  );

  return (
    <EditorSection title="Ambiente do DaVinci Resolve" actions={refreshButton} testId="editor-environment">
      {refreshError && <EditorErrorNotice error={refreshError} title="Falha ao verificar o ambiente" />}

      {!caps ? (
        <p className="text-xs text-slate-400">
          Ambiente ainda não verificado. Use “Verificar novamente”; se o erro persistir, o host não respondeu.
        </p>
      ) : (
        <dl className="grid grid-cols-1 sm:grid-cols-2 gap-x-6 gap-y-2 text-xs">
          <div className="space-y-1">
            <dt className="text-[10px] uppercase tracking-wider text-slate-500">Estado</dt>
            <dd className="flex flex-wrap items-center gap-2">
              <EditorBadge badge={editorStatusBadge(caps)} />
              <span className="text-slate-300">{ENVIRONMENT_STATE_LABEL[caps.environmentState]}</span>
            </dd>
          </div>

          <div className="space-y-1">
            <dt className="text-[10px] uppercase tracking-wider text-slate-500">Modo do adaptador</dt>
            <dd className="text-slate-200 font-mono">{editorModeLabel(caps)}</dd>
          </div>

          <div className="space-y-1">
            <dt className="text-[10px] uppercase tracking-wider text-slate-500">Versão</dt>
            <dd className="text-slate-200 font-mono break-words">
              {caps.productName ? `${caps.productName} · ` : ''}
              {editorVersionLabel(caps)}
            </dd>
          </div>

          <div className="space-y-1">
            <dt className="text-[10px] uppercase tracking-wider text-slate-500">Verificado em</dt>
            <dd className="text-slate-200 font-mono">{formatUtcDateTime(caps.checkedAtUtc)}</dd>
          </div>

          <div className="space-y-1 sm:col-span-2">
            <dt className="text-[10px] uppercase tracking-wider text-slate-500">Mensagem do host</dt>
            <dd className="text-slate-300 break-words">{caps.statusMessage}</dd>
          </div>

          {caps.reasonCode && (
            <div className="space-y-1 sm:col-span-2">
              <dt className="text-[10px] uppercase tracking-wider text-slate-500">Código de motivo</dt>
              <dd className="font-mono text-amber-300">{caps.reasonCode}</dd>
            </div>
          )}
        </dl>
      )}
    </EditorSection>
  );
};
