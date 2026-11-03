import React from 'react';
import { AlertCircle, Ban, Loader2, Info, Inbox } from 'lucide-react';
import { ipc } from '../../services/ipcBridge';

/** Selo DEMONSTRAÇÃO: aparece sempre que os dados vêm do mock (fora do WebView2). */
export const DemoBadge: React.FC<{ className?: string }> = ({ className = '' }) => {
  if (!ipc.isMock) return null;
  return (
    <span
      className={`px-2 py-0.5 rounded-full text-[10px] font-bold uppercase tracking-wider bg-amber-500/10 text-amber-300 border border-amber-500/30 ${className}`}
      title="Dados simulados: não são do projeto real"
    >
      DEMONSTRAÇÃO
    </span>
  );
};

export type BadgeTone = 'cyan' | 'amber' | 'rose' | 'emerald' | 'slate';

const BADGE_TONE: Record<BadgeTone, string> = {
  cyan: 'bg-cyan-950/70 text-cyan-300 border-cyan-500/30',
  amber: 'bg-amber-950/70 text-amber-300 border-amber-500/40',
  rose: 'bg-rose-950/70 text-rose-300 border-rose-500/40',
  emerald: 'bg-emerald-950/70 text-emerald-300 border-emerald-500/30',
  slate: 'bg-slate-800 text-slate-300 border-slate-700'
};

export const Badge: React.FC<{ tone?: BadgeTone; title?: string; className?: string; children: React.ReactNode }> = ({
  tone = 'slate',
  title,
  className = '',
  children
}) => (
  <span
    title={title}
    className={`inline-flex items-center gap-1 px-1.5 py-0.5 rounded border text-[10px] font-semibold whitespace-nowrap ${BADGE_TONE[tone]} ${className}`}
  >
    {children}
  </span>
);

/** Selo de hipótese: seção ou estrutura sugerida, sujeita a revisão humana. */
export const HypothesisBadge: React.FC = () => (
  <Badge tone="amber" title="Hipótese: sugerida pelo plano e sujeita a revisão humana">
    hipótese
  </Badge>
);

type StateTone = 'loading' | 'empty' | 'error' | 'blocked';

const STATE_ICON: Record<StateTone, React.ReactNode> = {
  loading: <Loader2 className="w-6 h-6 text-cyan-400 animate-spin" aria-hidden="true" />,
  empty: <Inbox className="w-6 h-6 text-slate-400" aria-hidden="true" />,
  error: <AlertCircle className="w-6 h-6 text-rose-400" aria-hidden="true" />,
  blocked: <Ban className="w-6 h-6 text-amber-400" aria-hidden="true" />
};

/** Estados não-ready: carregando, vazio, erro e bloqueado. Erro e carregamento anunciam-se a leitores de tela. */
export const StateCard: React.FC<{
  tone: StateTone;
  title: string;
  description: React.ReactNode;
  action?: React.ReactNode;
}> = ({ tone, title, description, action }) => (
  <div
    role={tone === 'error' ? 'alert' : 'status'}
    data-testid={`plan-state-${tone}`}
    className="flex-1 flex flex-col items-center justify-center p-8 text-center gap-3"
  >
    <div className="w-14 h-14 rounded-2xl bg-[#0a1222] border border-slate-800 flex items-center justify-center shadow-xl">
      {STATE_ICON[tone]}
    </div>
    <h3 className="text-sm font-bold text-white">{title}</h3>
    <div className="text-xs text-slate-400 max-w-md leading-relaxed">{description}</div>
    {action && <div className="pt-1 flex flex-wrap justify-center gap-2">{action}</div>}
  </div>
);

export const ActionError: React.FC<{ message: string; onDismiss: () => void }> = ({ message, onDismiss }) => (
  <div role="alert" className="mx-4 mt-3 flex items-start gap-2 px-3 py-2 rounded-xl bg-rose-950/50 border border-rose-500/30 text-xs text-rose-200">
    <AlertCircle className="w-4 h-4 shrink-0 mt-0.5" aria-hidden="true" />
    <span className="flex-1">{message}</span>
    <button
      type="button"
      onClick={onDismiss}
      className="text-rose-300 hover:text-white underline underline-offset-2 focus:outline-none focus-visible:ring-2 focus-visible:ring-rose-400 rounded"
    >
      Fechar
    </button>
  </div>
);

/** Indicador de valor do resumo. Sem valor real, mostra '—' em vez de zero inventado. */
export const StatTile: React.FC<{ label: string; value: string; hint?: string; tone?: 'default' | 'cyan' }> = ({
  label,
  value,
  hint,
  tone = 'default'
}) => (
  <div
    className={`min-w-[8.5rem] flex-1 p-3 rounded-xl bg-[#0a1222] border ${
      tone === 'cyan' ? 'border-cyan-500/40' : 'border-slate-800'
    }`}
  >
    <span className="text-[11px] text-slate-400 block truncate" title={label}>
      {label}
    </span>
    <span className={`block text-sm font-mono font-bold mt-0.5 ${tone === 'cyan' ? 'text-cyan-300' : 'text-white'}`}>
      {value}
    </span>
    {hint && <span className="block text-[10px] text-slate-500 mt-0.5 truncate" title={hint}>{hint}</span>}
  </div>
);

/** Área de prévia: não há player real nesta fase, portanto nenhum vídeo é exibido. */
export const PreviewPlaceholder: React.FC = () => (
  <div
    role="region"
    aria-label="Prévia"
    className="h-40 shrink-0 rounded-xl bg-black border border-slate-800 flex flex-col items-center justify-center gap-2 text-center px-6"
  >
    <Info className="w-5 h-5 text-slate-500" aria-hidden="true" />
    <p className="text-sm font-semibold text-slate-300">Prévia indisponível nesta fase</p>
    <p className="text-[11px] text-slate-500 max-w-md">
      Esta tela mostra o planejamento, não um render. Nenhum vídeo é exibido aqui.
    </p>
  </div>
);
