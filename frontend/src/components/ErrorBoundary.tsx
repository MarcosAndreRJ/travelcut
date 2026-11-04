import { Component, type ErrorInfo, type ReactNode } from 'react';
import { AlertTriangle, RefreshCw, Home, Copy, Check, ChevronDown, ChevronUp } from 'lucide-react';
import { ipc } from '../services/ipcBridge';

interface Props {
  children: ReactNode;
  fallbackTitle?: string;
  onReset?: () => void;
}

interface State {
  hasError: boolean;
  error: Error | null;
  errorInfo: ErrorInfo | null;
  copied: boolean;
  showDetails: boolean;
}

export class ErrorBoundary extends Component<Props, State> {
  public state: State = {
    hasError: false,
    error: null,
    errorInfo: null,
    copied: false,
    showDetails: false
  };

  public static getDerivedStateFromError(error: Error): Partial<State> {
    return { hasError: true, error };
  }

  public componentDidCatch(error: Error, errorInfo: ErrorInfo): void {
    if ((this as any).updater?.isMounted?.(this)) {
      this.setState({ errorInfo });
    } else {
      this.state = { ...this.state, errorInfo };
    }
    console.error('[ErrorBoundary] React render error caught:', error, errorInfo);

    // Encaminha o erro para o host desktop registrar nos logs locais (se WebView2 estiver ativo)
    try {
      if (typeof window !== 'undefined' && (window as any).chrome?.webview) {
        (window as any).chrome.webview.postMessage(
          JSON.stringify({
            type: 'system.logClientError',
            payload: {
              message: error.message,
              stack: error.stack,
              componentStack: errorInfo.componentStack
            }
          })
        );
      } else {
        void ipc.send('system.logClientError', {
          message: error.message,
          stack: error.stack,
          componentStack: errorInfo.componentStack
        }).catch(() => {
          // Ignora se o IPC mock ou desconectado não suportar
        });
      }
    } catch (_) {
      // Ignora falhas no encaminhamento secundário de telemetria
    }
  }

  private handleReset = () => {
    this.setState({
      hasError: false,
      error: null,
      errorInfo: null,
      copied: false,
      showDetails: false
    });
    if (this.props.onReset) {
      this.props.onReset();
    }
  };

  private handleReload = () => {
    window.location.reload();
  };

  private handleCopy = async () => {
    const { error, errorInfo } = this.state;
    const text = [
      `Error: ${error?.name ?? 'Error'}: ${error?.message ?? 'Unknown error'}`,
      '',
      'Stack trace:',
      error?.stack ?? '(No stack trace)',
      '',
      'Component stack:',
      errorInfo?.componentStack ?? '(No component stack)'
    ].join('\n');

    try {
      await navigator.clipboard.writeText(text);
      this.setState({ copied: true });
      setTimeout(() => this.setState({ copied: false }), 2000);
    } catch (err) {
      console.warn('Falha ao copiar para a área de transferência:', err);
    }
  };

  public render(): ReactNode {
    if (this.state.hasError) {
      const { error, errorInfo, copied, showDetails } = this.state;
      const title = this.props.fallbackTitle ?? 'Ocorreu um erro inesperado na interface';

      return (
        <div
          role="alert"
          aria-live="assertive"
          className="flex-1 w-full h-full min-h-[360px] flex items-center justify-center p-6 bg-[#070d18] text-slate-200 select-none"
        >
          <div className="max-w-xl w-full bg-[#0a1222] border border-red-900/50 rounded-2xl p-6 shadow-2xl shadow-red-950/20 flex flex-col gap-5">
            <div className="flex items-start gap-4">
              <div className="w-12 h-12 rounded-xl bg-red-950/60 border border-red-800/60 flex items-center justify-center text-red-400 shrink-0">
                <AlertTriangle className="w-6 h-6" />
              </div>
              <div className="flex-1 min-w-0">
                <h2 className="text-base font-bold text-white tracking-tight">{title}</h2>
                <p className="text-xs text-slate-400 mt-1 leading-relaxed">
                  A visualização encontrou uma falha de renderização. Os seus arquivos e projeto permanecem seguros.
                </p>
                {error?.message && (
                  <p className="text-xs font-mono text-red-300/90 mt-2 p-2 bg-red-950/30 border border-red-900/30 rounded-lg break-words select-text">
                    {error.message}
                  </p>
                )}
              </div>
            </div>

            <div className="flex flex-wrap items-center gap-2 pt-2 border-t border-slate-800/80">
              <button
                type="button"
                onClick={this.handleReset}
                className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-semibold bg-cyan-600 hover:bg-cyan-500 text-white transition-colors"
              >
                <RefreshCw className="w-3.5 h-3.5" />
                Tentar novamente
              </button>

              <button
                type="button"
                onClick={this.handleReload}
                className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-semibold bg-slate-800 hover:bg-slate-700 text-slate-200 transition-colors"
              >
                <Home className="w-3.5 h-3.5" />
                Recarregar tela
              </button>

              <button
                type="button"
                onClick={this.handleCopy}
                className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-semibold bg-slate-800 hover:bg-slate-700 text-slate-300 ml-auto transition-colors"
                title="Copiar relatório de erro"
              >
                {copied ? <Check className="w-3.5 h-3.5 text-emerald-400" /> : <Copy className="w-3.5 h-3.5" />}
                {copied ? 'Copiado' : 'Copiar erro'}
              </button>

              <button
                type="button"
                onClick={() => this.setState({ showDetails: !showDetails })}
                className="inline-flex items-center gap-1 px-2 py-1.5 rounded-lg text-xs font-medium text-slate-400 hover:text-slate-200 transition-colors"
              >
                {showDetails ? (
                  <>
                    <ChevronUp className="w-3.5 h-3.5" />
                    Ocultar detalhes
                  </>
                ) : (
                  <>
                    <ChevronDown className="w-3.5 h-3.5" />
                    Ver detalhes
                  </>
                )}
              </button>
            </div>

            {showDetails && (
              <div className="mt-1 p-3 bg-[#040810] border border-slate-800 rounded-lg max-h-56 overflow-y-auto select-text font-mono text-[11px] text-slate-400 space-y-2">
                <div>
                  <span className="text-red-400 font-semibold">Stack Trace:</span>
                  <pre className="whitespace-pre-wrap mt-0.5 text-slate-400">{error?.stack ?? 'Indisponível'}</pre>
                </div>
                {errorInfo?.componentStack && (
                  <div className="pt-2 border-t border-slate-800">
                    <span className="text-amber-400 font-semibold">Component Stack:</span>
                    <pre className="whitespace-pre-wrap mt-0.5 text-slate-500">{errorInfo.componentStack}</pre>
                  </div>
                )}
              </div>
            )}
          </div>
        </div>
      );
    }

    return this.props.children;
  }
}
