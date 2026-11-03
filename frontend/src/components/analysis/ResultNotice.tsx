import React from 'react';
import type { AnalysisResultStatus } from '../../types/ipc';
import { ANALYSIS_STATUS_LABEL } from './analysisFormat';

interface ResultNoticeProps {
  label: string;
  status: AnalysisResultStatus;
  reason: string | null;
}

/** Estado de um resultado que não é 'completed'. Motivos vêm do host; sem texto de substituição. */
export const ResultNotice: React.FC<ResultNoticeProps> = ({ label, status, reason }) => {
  if (status === 'completed') return null;

  const tone =
    status === 'failed'
      ? 'text-rose-300 border-rose-500/30 bg-rose-500/5'
      : 'text-amber-200 border-amber-500/25 bg-amber-500/5';

  let detail: string;
  if (status === 'not_run') {
    detail = 'Execute a análise deste clipe para ver os resultados.';
  } else if (status === 'failed') {
    detail = reason ?? 'O host não informou o motivo da falha.';
  } else {
    detail = reason ?? 'O host não informou o motivo da indisponibilidade.';
  }

  return (
    <div role="status" className={`rounded-xl border px-3 py-2 text-[11px] ${tone}`}>
      <p className="font-semibold">
        {label}: {ANALYSIS_STATUS_LABEL[status]}
      </p>
      <p className="text-slate-400 mt-0.5">{detail}</p>
    </div>
  );
};
