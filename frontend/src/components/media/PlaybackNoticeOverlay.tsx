import React from 'react';
import { AlertCircle, Ban, Loader2, RefreshCw, Wrench } from 'lucide-react';
import type { ClipPlayerNotice } from './mediaClipModel';

export interface ProxyControl {
  /** Proxy em geração por esta tela ou por um job anterior. */
  generating: boolean;
  onStart: () => void;
  message: string | null;
}

export const ProxyButton: React.FC<{ proxy: ProxyControl }> = ({ proxy }) => (
  <button
    type="button"
    onClick={proxy.onStart}
    disabled={proxy.generating}
    className="px-3 py-1.5 rounded-lg bg-cyan-600 hover:bg-cyan-500 disabled:opacity-50 disabled:cursor-not-allowed text-[11px] font-bold text-white flex items-center gap-1.5"
  >
    {proxy.generating ? <Loader2 className="w-3.5 h-3.5 animate-spin" aria-hidden="true" /> : <Wrench className="w-3.5 h-3.5" aria-hidden="true" />}
    {proxy.generating ? 'Gerando proxy…' : 'Gerar proxy compatível'}
  </button>
);

/**
 * Cobertura da área de vídeo para fonte indisponível, erro do elemento ou vídeo sem imagem. Compartilhada pelo
 * player de clipe e pelo de intervalo, para os dois darem o mesmo diagnóstico de reprodução.
 */
export const PlaybackNoticeOverlay: React.FC<{
  notice: ClipPlayerNotice;
  proxy: ProxyControl;
  onReload: () => void;
}> = ({ notice, proxy, onReload }) => {
  switch (notice.kind) {
    case 'none':
      return null;
    case 'loading':
      return (
        <div role="status" className="absolute inset-0 flex flex-col items-center justify-center gap-2 text-center p-4 text-slate-300">
          <Loader2 className="w-6 h-6 text-cyan-400 animate-spin" aria-hidden="true" />
          <p className="text-xs">Carregando fonte de reprodução…</p>
        </div>
      );
    case 'source_error':
      return (
        <div role="alert" className="absolute inset-0 flex flex-col items-center justify-center gap-2 text-center p-4">
          <AlertCircle className="w-6 h-6 text-rose-400" aria-hidden="true" />
          <p className="text-xs text-rose-200 max-w-md">Não foi possível obter a fonte de reprodução: {notice.message}</p>
          <button
            type="button"
            onClick={onReload}
            className="px-3 py-1.5 rounded-lg bg-slate-800 hover:bg-slate-700 text-[11px] font-semibold text-slate-200 flex items-center gap-1.5"
          >
            <RefreshCw className="w-3.5 h-3.5" aria-hidden="true" /> Tentar novamente
          </button>
        </div>
      );
    default: {
      const Icon = notice.kind === 'unavailable' ? Ban : AlertCircle;
      return (
        <div
          role="alert"
          data-testid={`player-notice-${notice.kind}`}
          className="absolute inset-0 bg-black/85 flex flex-col items-center justify-center gap-2 text-center p-4"
        >
          <Icon className="w-6 h-6 text-amber-400" aria-hidden="true" />
          <p className="text-xs text-slate-200 max-w-md">{notice.message}</p>
          {notice.offerProxy && <ProxyButton proxy={proxy} />}
          {notice.offerProxy && <p className="text-[10px] text-slate-400">O proxy é uma cópia em 3-Trabalho; o arquivo original não é alterado.</p>}
          {proxy.message && <p role="status" className="text-[11px] text-slate-300">{proxy.message}</p>}
        </div>
      );
    }
  }
};
