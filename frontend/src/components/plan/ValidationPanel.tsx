import React from 'react';
import { AlertCircle, AlertTriangle, CheckCircle2, Loader2, RefreshCw } from 'lucide-react';
import type { EditPlanDto, PlanValidationDto } from '../../types/ipc';
import { Badge } from './PlanPrimitives';
import { issueTargetClipId } from './planModel';
import { formatDateTime, severityLabel } from './planFormat';

interface ValidationPanelProps {
  plan: EditPlanDto;
  validation: PlanValidationDto;
  onSelectClip: (clipId: string) => void;
  onRevalidate: () => void;
  revalidating: boolean;
  disabled: boolean;
}

export const ValidationPanel: React.FC<ValidationPanelProps> = ({
  plan,
  validation,
  onSelectClip,
  onRevalidate,
  revalidating,
  disabled
}) => {
  const errors = validation.issues.filter((i) => i.severity === 'error').length;
  const warnings = validation.issues.length - errors;

  return (
    <section aria-labelledby="plan-validation-title" className="space-y-2">
      <div className="flex items-center justify-between gap-2">
        <h3 id="plan-validation-title" className="text-xs font-bold text-white">
          Validação
        </h3>
        {validation.isValid ? (
          <Badge tone="emerald" className="text-[11px]">
            <CheckCircle2 className="w-3 h-3" aria-hidden="true" /> válido
          </Badge>
        ) : (
          <Badge tone="rose" className="text-[11px]">
            <AlertCircle className="w-3 h-3" aria-hidden="true" /> inválido
          </Badge>
        )}
      </div>

      <p className="text-[11px] text-slate-500">
        {errors} erro(s) · {warnings} aviso(s) · validado em {formatDateTime(validation.validatedAtUtc)}
      </p>

      {validation.issues.length === 0 ? (
        <p className="text-[11px] text-slate-400">Nenhum problema encontrado nesta revisão.</p>
      ) : (
        <ul className="space-y-1.5 max-h-56 overflow-y-auto pr-1" aria-label="Problemas da validação">
          {validation.issues.map((issue, index) => {
            const target = issueTargetClipId(plan, issue);
            const isError = issue.severity === 'error';
            const body = (
              <>
                <span className="flex items-center gap-1.5 text-[10px] font-bold uppercase">
                  {isError ? (
                    <AlertCircle className="w-3 h-3" aria-hidden="true" />
                  ) : (
                    <AlertTriangle className="w-3 h-3" aria-hidden="true" />
                  )}
                  {severityLabel(issue.severity)} · {issue.code}
                </span>
                <span className="block text-[11px] text-slate-200 mt-0.5">{issue.message}</span>
                {target && <span className="block text-[10px] text-cyan-300 mt-0.5">Ver clipe {target}</span>}
              </>
            );
            const tone = isError
              ? 'bg-rose-950/40 border-rose-500/30 text-rose-300'
              : 'bg-amber-950/40 border-amber-500/30 text-amber-300';
            return (
              <li key={`${issue.code}-${issue.clipId ?? 'plano'}-${index}`}>
                {target ? (
                  <button
                    type="button"
                    onClick={() => onSelectClip(target)}
                    className={`w-full text-left rounded-lg border px-2.5 py-2 ${tone} hover:brightness-110 focus:outline-none focus-visible:ring-2 focus-visible:ring-cyan-400`}
                  >
                    {body}
                  </button>
                ) : (
                  <div className={`rounded-lg border px-2.5 py-2 ${tone}`}>{body}</div>
                )}
              </li>
            );
          })}
        </ul>
      )}

      <button
        type="button"
        onClick={onRevalidate}
        disabled={disabled || revalidating}
        className="w-full py-1.5 rounded-lg border border-slate-700 text-[11px] font-semibold text-slate-200 hover:bg-slate-800 disabled:opacity-50 disabled:cursor-not-allowed flex items-center justify-center gap-1.5 focus:outline-none focus-visible:ring-2 focus-visible:ring-cyan-400"
      >
        {revalidating ? (
          <Loader2 className="w-3.5 h-3.5 animate-spin" aria-hidden="true" />
        ) : (
          <RefreshCw className="w-3.5 h-3.5" aria-hidden="true" />
        )}
        Revalidar R{plan.revision}
      </button>
    </section>
  );
};
