import React, { useState } from 'react';
import { AlertCircle, Ban, ChevronDown, ChevronUp, Copy, FolderOpen, Loader2, RefreshCw, Wrench } from 'lucide-react';
import type { ClipPlayerNotice } from './mediaClipModel';
import type { PlayerDiagnosticData } from './usePlayerDiagnostics';

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
  diagnostic?: PlayerDiagnosticData | null;
  onCopyDiagnostic?: () => Promise<boolean> | void;
  onOpenLogsFolder?: () => void;
}> = ({ notice, proxy, onReload, diagnostic, onCopyDiagnostic, onOpenLogsFolder }) => {
  const [showDetails, setShowDetails] = useState(false);
  const [copied, setCopied] = useState(false);

  const handleCopy = async () => {
    if (onCopyDiagnostic) {
      const res = await onCopyDiagnostic();
      if (res !== false) {
        setCopied(true);
        setTimeout(() => setCopied(false), 2000);
      }
    }
  };

  if (notice.kind === 'none') {
    return null;
  }

  if (notice.kind === 'loading') {
    return (
      <div role="status" className="absolute inset-0 flex flex-col items-center justify-center gap-2 text-center p-4 text-slate-300">
        <Loader2 className="w-6 h-6 text-cyan-400 animate-spin" aria-hidden="true" />
        <p className="text-xs">Carregando fonte de reprodução…</p>
      </div>
    );
  }

  const isError = notice.kind === 'element_error' || notice.kind === 'source_error' || notice.kind === 'unavailable';
  const Icon = notice.kind === 'unavailable' ? Ban : AlertCircle;

  return (
    <div
      role="alert"
      data-testid={`player-notice-${notice.kind}`}
      className="absolute inset-0 bg-black/90 flex flex-col items-center justify-center gap-2 text-center p-4 overflow-y-auto"
    >
      <Icon className={`w-6 h-6 ${notice.kind === 'unavailable' ? 'text-amber-400' : 'text-rose-400'}`} aria-hidden="true" />
      <p className="text-xs text-slate-200 max-w-md font-medium">{notice.message}</p>

      {notice.kind === 'source_error' && (
        <button
          type="button"
          onClick={onReload}
          className="px-3 py-1.5 rounded-lg bg-slate-800 hover:bg-slate-700 text-[11px] font-semibold text-slate-200 flex items-center gap-1.5 mt-1"
        >
          <RefreshCw className="w-3.5 h-3.5" aria-hidden="true" /> Tentar novamente
        </button>
      )}

      {notice.offerProxy && <ProxyButton proxy={proxy} />}
      {notice.offerProxy && <p className="text-[10px] text-slate-400">O proxy é uma cópia em 3-Trabalho; o arquivo original não é alterado.</p>}
      {proxy.message && <p role="status" className="text-[11px] text-slate-300">{proxy.message}</p>}

      {isError && (
        <div className="flex flex-col items-center gap-2 mt-2 w-full max-w-md">
          <div className="flex flex-wrap items-center justify-center gap-2">
            <button
              type="button"
              onClick={() => setShowDetails(prev => !prev)}
              className="px-2.5 py-1 rounded bg-slate-800 hover:bg-slate-700 text-[10px] text-slate-300 flex items-center gap-1 border border-slate-700 transition-colors"
            >
              {showDetails ? <ChevronUp className="w-3 h-3" /> : <ChevronDown className="w-3 h-3" />}
              {showDetails ? 'Ocultar detalhes técnicos' : 'Ver detalhes técnicos'}
            </button>

            {onCopyDiagnostic && (
              <button
                type="button"
                onClick={handleCopy}
                className="px-2.5 py-1 rounded bg-slate-800 hover:bg-slate-700 text-[10px] text-slate-300 flex items-center gap-1 border border-slate-700 transition-colors"
              >
                <Copy className="w-3 h-3" />
                {copied ? 'Copiado!' : 'Copiar diagnóstico'}
              </button>
            )}

            {onOpenLogsFolder && (
              <button
                type="button"
                onClick={onOpenLogsFolder}
                className="px-2.5 py-1 rounded bg-slate-800 hover:bg-slate-700 text-[10px] text-slate-300 flex items-center gap-1 border border-slate-700 transition-colors"
              >
                <FolderOpen className="w-3 h-3" />
                Abrir pasta de logs
              </button>
            )}
          </div>

          {showDetails && diagnostic && (
            <div className="w-full text-left bg-slate-950 border border-slate-800 rounded p-2.5 text-[10px] font-mono text-slate-300 space-y-1 overflow-x-auto max-h-48">
              <div><strong className="text-slate-400">Status:</strong> {diagnostic.status ?? 'N/A'} (proxy: {diagnostic.proxyStatus ?? 'N/A'})</div>
              <div><strong className="text-slate-400">Codec / MIME:</strong> {diagnostic.codec ?? 'N/A'} / {diagnostic.mimeType ?? 'N/A'}</div>
              <div><strong className="text-slate-400">Código de erro:</strong> {diagnostic.errorCode ?? 'N/A'} ({diagnostic.errorMessage ?? 'N/A'})</div>
              <div><strong className="text-slate-400">URL:</strong> <span className="break-all">{diagnostic.url ?? 'N/A'}</span></div>
              <div><strong className="text-slate-400">ReadyState / NetworkState:</strong> {diagnostic.readyState} / {diagnostic.networkState}</div>
              <div><strong className="text-slate-400">Dimensões do vídeo:</strong> {diagnostic.videoWidth}x{diagnostic.videoHeight}</div>
              {diagnostic.timeToFailureMs !== null && (
                <div><strong className="text-slate-400">Tempo até a falha:</strong> {diagnostic.timeToFailureMs}ms</div>
              )}
              {diagnostic.recentEvents.length > 0 && (
                <div className="pt-1">
                  <strong className="text-slate-400">Eventos recentes:</strong>
                  <div className="text-[9px] text-slate-400 pl-1">
                    {diagnostic.recentEvents.map((e, idx) => (
                      <span key={idx} className="mr-2">[{e.timeMs}ms {e.event}]</span>
                    ))}
                  </div>
                </div>
              )}
            </div>
          )}
        </div>
      )}
    </div>
  );
};
