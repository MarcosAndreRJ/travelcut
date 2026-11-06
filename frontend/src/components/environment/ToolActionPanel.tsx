import React, { useState } from 'react';
import {
  Search,
  Settings2,
  Terminal,
  Play,
  Download,
  CheckCircle2,
  AlertCircle,
  Loader2,
  Check,
  X
} from 'lucide-react';
import type { TestToolResponse } from '../../types/ipc';

interface ToolActionPanelProps {
  toolKey: string;
  message?: string;
  error?: string | null;
  toolName: string;
  isReady: boolean;
  canInstall: boolean;
  canDownloadModel?: boolean;
  onLocate: (toolKey: string) => void;
  onConfigurePath: (toolKey: string, path: string) => void;
  onOpenInstallPlan: (toolKey: string) => void;
  onOpenModelDownload?: () => void;
  onTestTool: (toolKey: string) => void;
  locating?: boolean;
  testing?: boolean;
  candidates?: string[];
  testResult?: TestToolResponse | null;
}

export const ToolActionPanel: React.FC<ToolActionPanelProps> = ({
  toolKey,
  toolName,
  isReady: _isReady,
  canInstall,
  canDownloadModel,
  onLocate,
  onConfigurePath,
  onOpenInstallPlan,
  onOpenModelDownload,
  onTestTool,
  locating = false,
  testing = false,
  candidates,
  testResult
}) => {
  const [showConfig, setShowConfig] = useState(false);
  const [customPath, setCustomPath] = useState('');
  const [showLogs, setShowLogs] = useState(false);

  const handleSavePath = () => {
    if (customPath.trim()) {
      onConfigurePath(toolKey, customPath.trim());
      setShowConfig(false);
      setCustomPath('');
    }
  };

  return (
    <div className="mt-3 pt-3 border-t border-slate-800/60 space-y-3">
      {/* Action Buttons Row */}
      <div className="flex flex-wrap items-center gap-2">
        {/* Test Button */}
        <button
          onClick={() => onTestTool(toolKey)}
          disabled={testing}
          className="px-2.5 py-1 text-xs font-semibold rounded-lg bg-slate-800/80 hover:bg-slate-700 text-slate-200 hover:text-white border border-slate-700/60 transition flex items-center gap-1.5 disabled:opacity-50"
          title={`Executar autoteste do ${toolName}`}
        >
          {testing ? (
            <Loader2 className="w-3.5 h-3.5 animate-spin text-cyan-400" />
          ) : (
            <Play className="w-3.5 h-3.5 text-cyan-400" />
          )}
          <span>{testing ? 'Testando...' : 'Testar'}</span>
        </button>

        {/* Locate Button */}
        <button
          onClick={() => onLocate(toolKey)}
          disabled={locating}
          className="px-2.5 py-1 text-xs font-semibold rounded-lg bg-slate-800/80 hover:bg-slate-700 text-slate-200 hover:text-white border border-slate-700/60 transition flex items-center gap-1.5 disabled:opacity-50"
          title={`Procurar executáveis do ${toolName} nos diretórios padrão do Windows`}
        >
          {locating ? (
            <Loader2 className="w-3.5 h-3.5 animate-spin text-cyan-400" />
          ) : (
            <Search className="w-3.5 h-3.5 text-cyan-400" />
          )}
          <span>{locating ? 'Procurando...' : 'Localizar'}</span>
        </button>

        {/* Configure Path Toggle */}
        <button
          onClick={() => setShowConfig(!showConfig)}
          className={`px-2.5 py-1 text-xs font-semibold rounded-lg border transition flex items-center gap-1.5 ${
            showConfig
              ? 'bg-cyan-950/40 border-cyan-500/50 text-cyan-300'
              : 'bg-slate-800/80 hover:bg-slate-700 text-slate-200 hover:text-white border-slate-700/60'
          }`}
          title="Configurar manualmente o caminho do executável"
        >
          <Settings2 className="w-3.5 h-3.5 text-cyan-400" />
          <span>Caminho</span>
        </button>

        {/* Install / Plan Button */}
        {canInstall && (
          <button
            onClick={() => onOpenInstallPlan(toolKey)}
            className="px-2.5 py-1 text-xs font-semibold rounded-lg bg-cyan-600/20 hover:bg-cyan-600/30 text-cyan-300 hover:text-cyan-200 border border-cyan-500/40 transition flex items-center gap-1.5"
            title={`Ver plano de instalação assistida do ${toolName}`}
          >
            <Terminal className="w-3.5 h-3.5 text-cyan-400" />
            <span>Plano de Instalação</span>
          </button>
        )}

        {/* Whisper Model Download Button */}
        {canDownloadModel && onOpenModelDownload && (
          <button
            onClick={onOpenModelDownload}
            className="px-2.5 py-1 text-xs font-semibold rounded-lg bg-indigo-600/20 hover:bg-indigo-600/30 text-indigo-300 hover:text-indigo-200 border border-indigo-500/40 transition flex items-center gap-1.5"
            title="Baixar modelo Whisper large-v3 para transcrição de alta precisão"
          >
            <Download className="w-3.5 h-3.5 text-indigo-400" />
            <span>Baixar large-v3</span>
          </button>
        )}
      </div>

      {/* Path Input Row */}
      {showConfig && (
        <div className="flex items-center gap-2 bg-slate-900/90 p-2.5 rounded-xl border border-slate-800 animate-in fade-in duration-150">
          <input
            type="text"
            placeholder="Ex: C:\Program Files\...\bin\executable.exe"
            value={customPath}
            onChange={(e) => setCustomPath(e.target.value)}
            className="flex-1 bg-black/60 border border-slate-700 rounded-lg px-3 py-1.5 text-xs text-white placeholder-slate-500 focus:outline-none focus:border-cyan-500 font-mono"
          />
          <button
            onClick={handleSavePath}
            disabled={!customPath.trim()}
            className="px-3 py-1.5 text-xs font-bold rounded-lg bg-cyan-600 hover:bg-cyan-500 text-white transition disabled:opacity-40 disabled:cursor-not-allowed flex items-center gap-1 shrink-0"
          >
            <Check className="w-3.5 h-3.5" />
            Salvar
          </button>
          <button
            onClick={() => setShowConfig(false)}
            className="p-1.5 text-slate-400 hover:text-slate-200 transition"
          >
            <X className="w-3.5 h-3.5" />
          </button>
        </div>
      )}

      {/* Candidates List from AutoLocate */}
      {candidates && candidates.length > 0 && (
        <div className="bg-slate-900/80 p-2.5 rounded-xl border border-cyan-500/30 space-y-1.5">
          <div className="text-[11px] font-semibold text-cyan-300 flex items-center gap-1.5">
            <CheckCircle2 className="w-3.5 h-3.5 text-cyan-400" />
            <span>Candidatos detectados no sistema ({candidates.length}):</span>
          </div>
          <div className="space-y-1">
            {candidates.map((cand, idx) => (
              <div
                key={idx}
                className="flex items-center justify-between gap-2 p-1.5 rounded-lg bg-black/40 hover:bg-black/60 border border-slate-800 text-xs"
              >
                <span className="font-mono text-slate-300 text-[11px] truncate" title={cand}>
                  {cand}
                </span>
                <button
                  onClick={() => onConfigurePath(toolKey, cand)}
                  className="px-2 py-0.5 text-[11px] font-bold rounded bg-cyan-600/30 hover:bg-cyan-600/50 text-cyan-300 border border-cyan-500/40 transition shrink-0"
                >
                  Usar este
                </button>
              </div>
            ))}
          </div>
        </div>
      )}

      {/* Test Result Display */}
      {testResult && (
        <div
          className={`p-2.5 rounded-xl border space-y-1.5 text-xs ${
            testResult.success
              ? 'bg-emerald-950/20 border-emerald-500/40'
              : 'bg-red-950/20 border-red-500/40'
          }`}
        >
          <div className="flex items-start justify-between gap-2">
            <div className="flex items-center gap-2">
              {testResult.success ? (
                <CheckCircle2 className="w-4 h-4 text-emerald-400 shrink-0" />
              ) : (
                <AlertCircle className="w-4 h-4 text-red-400 shrink-0" />
              )}
              <span
                className={`font-semibold ${
                  testResult.success ? 'text-emerald-300' : 'text-red-300'
                }`}
              >
                {testResult.message}
              </span>
            </div>

            {testResult.outputLogs && testResult.outputLogs.length > 0 && (
              <button
                onClick={() => setShowLogs(!showLogs)}
                className="text-[11px] text-slate-400 hover:text-slate-200 underline shrink-0"
              >
                {showLogs ? 'Ocultar logs' : 'Ver logs'}
              </button>
            )}
          </div>

          {testResult.details && (
            <p className="text-[11px] text-slate-300 pl-6 leading-relaxed">
              {testResult.details}
            </p>
          )}

          {showLogs && testResult.outputLogs && testResult.outputLogs.length > 0 && (
            <div className="mt-2 p-2 rounded-lg bg-black/70 border border-slate-800 font-mono text-[11px] text-slate-300 max-h-36 overflow-y-auto space-y-0.5">
              {testResult.outputLogs.map((log, idx) => (
                <div key={idx} className="whitespace-pre-wrap break-all leading-snug">
                  {log}
                </div>
              ))}
            </div>
          )}
        </div>
      )}
    </div>
  );
};
