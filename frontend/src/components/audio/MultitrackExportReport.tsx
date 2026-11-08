import React from 'react';
import { AlertTriangle, CheckCircle2 } from 'lucide-react';
import type { ExportCapabilityReport } from './multitrackModel';

export interface MultitrackExportReportProps {
  report: ExportCapabilityReport;
}

export const MultitrackExportReport: React.FC<MultitrackExportReportProps> = ({ report }) => {
  return (
    <div className="p-3 rounded-lg bg-slate-900/60 border border-slate-800 space-y-3 text-xs">
      <div className="flex items-center gap-1.5 font-semibold text-slate-200">
        {report.isFullyExportable ? (
          <CheckCircle2 className="w-4 h-4 text-emerald-400" aria-hidden="true" />
        ) : (
          <AlertTriangle className="w-4 h-4 text-amber-400" aria-hidden="true" />
        )}
        <span>Compatibilidade com Exportação DaVinci</span>
      </div>
      <p className="text-[11px] text-slate-400">
        Status de exportação dos clipes e faixas multitrack para o assembly DaVinci Resolve.
      </p>

      <div className="grid grid-cols-2 gap-2 text-center text-xs">
        <div className="p-2 rounded bg-slate-950 border border-slate-800">
          <div className="text-[10px] text-slate-500 uppercase font-bold">Exportáveis (V1/A1)</div>
          <div className="text-base font-bold text-emerald-400 font-mono mt-0.5">{report.supportedClipsCount}</div>
        </div>
        <div className="p-2 rounded bg-slate-950 border border-slate-800">
          <div className="text-[10px] text-slate-500 uppercase font-bold">Faixas Adicionais</div>
          <div className="text-base font-bold text-amber-400 font-mono mt-0.5">{report.unsupportedClipsCount}</div>
        </div>
      </div>

      {report.rejectionReasons.length > 0 && (
        <div className="space-y-1.5 pt-1">
          <span className="text-[10px] text-amber-400 uppercase font-bold tracking-wider">Limitações Detectadas:</span>
          <div className="space-y-1">
            {report.rejectionReasons.map((reason, idx) => (
              <div key={idx} className="flex items-start gap-1.5 text-[11px] text-amber-300/90 bg-amber-950/30 p-1.5 rounded border border-amber-900/40">
                <span className="font-mono text-amber-500 shrink-0">•</span>
                <span>{reason}</span>
              </div>
            ))}
          </div>
        </div>
      )}

      {report.warnings.length > 0 && (
        <div className="space-y-1 pt-1">
          {report.warnings.map((warning, idx) => (
            <div key={idx} className="text-[11px] text-slate-400 italic">
              {warning}
            </div>
          ))}
        </div>
      )}

      <div className="p-2 rounded bg-slate-950/80 border border-slate-800/80 text-[10px] text-slate-400 space-y-1">
        <p className="font-semibold text-slate-300">Nota arquitetural (F14.3):</p>
        <p>
          Clipes em faixas adicionais (V2 B-roll, A2 Música, A3 Efeitos) permanecem íntegros e persistidos no projeto TravelCut Studio para preview e edição, e serão integrados ao exportador multifaixa na F14.5.
        </p>
      </div>
    </div>
  );
};
