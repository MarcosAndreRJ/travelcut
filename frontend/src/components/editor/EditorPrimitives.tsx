import React from 'react';
import { AlertCircle, AlertTriangle } from 'lucide-react';
import type { EditorBadgeInfo, EditorErrorInfo, EditorTone } from './editorModel';

const TONE_CLASS: Record<EditorTone, string> = {
  emerald: 'bg-emerald-950/70 text-emerald-300 border-emerald-500/30',
  amber: 'bg-amber-950/70 text-amber-300 border-amber-500/40',
  rose: 'bg-rose-950/70 text-rose-300 border-rose-500/40',
  cyan: 'bg-cyan-950/70 text-cyan-300 border-cyan-500/30',
  slate: 'bg-slate-800 text-slate-300 border-slate-700'
};

/** Cores de texto e de ponto por tom, para cartões compactos (painel inicial, barras laterais). */
export const TONE_TEXT: Record<EditorTone, string> = {
  emerald: 'text-emerald-400',
  amber: 'text-amber-300',
  rose: 'text-rose-400',
  cyan: 'text-cyan-300',
  slate: 'text-slate-400'
};

export const TONE_DOT: Record<EditorTone, string> = {
  emerald: 'bg-emerald-400',
  amber: 'bg-amber-400',
  rose: 'bg-rose-500',
  cyan: 'bg-cyan-400',
  slate: 'bg-slate-500'
};

export const EditorBadge: React.FC<{ badge: EditorBadgeInfo; title?: string; className?: string }> = ({
  badge,
  title,
  className = ''
}) => (
  <span
    title={title}
    className={`inline-flex items-center px-1.5 py-0.5 rounded border text-[10px] font-semibold whitespace-nowrap ${TONE_CLASS[badge.tone]} ${className}`}
  >
    {badge.label}
  </span>
);

/** Bloco com título. Cada tela monta suas seções com este cartão padrão. */
export const EditorSection: React.FC<{
  title: string;
  description?: React.ReactNode;
  actions?: React.ReactNode;
  children: React.ReactNode;
  testId?: string;
}> = ({ title, description, actions, children, testId }) => (
  <section
    aria-label={title}
    data-testid={testId}
    className="rounded-xl border border-slate-800 bg-[#0a1222] p-4 space-y-3 min-w-0"
  >
    <div className="flex flex-wrap items-start justify-between gap-3">
      <div className="min-w-0">
        <h3 className="text-sm font-bold text-white">{title}</h3>
        {description && <div className="text-[11px] text-slate-400 mt-0.5 leading-relaxed">{description}</div>}
      </div>
      {actions && <div className="flex flex-wrap items-center gap-2">{actions}</div>}
    </div>
    {children}
  </section>
);

/** Erro sempre visível, com código estável quando o host envia um. Nunca engole a falha. */
export const EditorErrorNotice: React.FC<{ error: EditorErrorInfo; title?: string }> = ({ error, title }) => (
  <div
    role="alert"
    className="flex items-start gap-2 px-3 py-2 rounded-xl bg-rose-950/50 border border-rose-500/30 text-xs text-rose-200"
  >
    <AlertCircle className="w-4 h-4 shrink-0 mt-0.5" aria-hidden="true" />
    <div className="min-w-0 space-y-0.5">
      <p className="font-semibold">{title ?? 'Não foi possível concluir'}</p>
      <p className="break-words">{error.message}</p>
      {error.code && <p className="font-mono text-[10px] text-rose-300/80">código: {error.code}</p>}
    </div>
  </div>
);

/** Aviso âmbar fixo: deixa explícito que o resultado é simulação. */
export const SimulatedBanner: React.FC<{ message: string }> = ({ message }) => (
  <div
    role="status"
    data-testid="simulated-banner"
    className="flex items-start gap-2 px-3 py-2 rounded-xl bg-amber-950/50 border border-amber-500/40 text-xs text-amber-200 font-semibold"
  >
    <AlertTriangle className="w-4 h-4 shrink-0 mt-0.5" aria-hidden="true" />
    <span>{message}</span>
  </div>
);

export const tableHeadClass = 'text-left text-[10px] font-semibold uppercase tracking-wider text-slate-400 px-2 py-1.5 border-b border-slate-800';
export const tableCellClass = 'px-2 py-1.5 border-b border-slate-800/60 text-xs text-slate-200 align-top';
export const primaryButtonClass =
  'px-4 py-2 rounded-xl bg-gradient-to-r from-cyan-500 to-blue-600 text-xs font-bold text-white disabled:opacity-50 disabled:cursor-not-allowed focus:outline-none focus-visible:ring-2 focus-visible:ring-cyan-400';
export const secondaryButtonClass =
  'px-4 py-2 rounded-xl bg-slate-800 hover:bg-slate-700 text-xs font-semibold text-slate-200 disabled:opacity-50 disabled:cursor-not-allowed focus:outline-none focus-visible:ring-2 focus-visible:ring-cyan-400';
