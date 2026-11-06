import React, { useState } from 'react';
import {
  AlertTriangle,
  Clock,
  Download,
  HardDrive,
  Shield,
  Terminal,
  X,
  CheckCircle2,
  Loader2
} from 'lucide-react';
import type { EnvironmentInstallPlanDto } from '../../types/ipc';

interface InstallPlanModalProps {
  plan: EnvironmentInstallPlanDto | null;
  loading: boolean;
  error: string | null;
  onConfirm: (toolKey: string, customPath?: string, preferCuda?: boolean, approvalToken?: string) => void;
  onClose: () => void;
}

export const InstallPlanModal: React.FC<InstallPlanModalProps> = ({
  plan,
  loading,
  error,
  onConfirm,
  onClose
}) => {
  const [preferCuda, setPreferCuda] = useState(false);
  const [customPath, setCustomPath] = useState('');

  if (!plan && !loading && !error) return null;

  return (
    <div
      role="dialog"
      aria-modal="true"
      aria-labelledby="install-plan-title"
      className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/80 backdrop-blur-sm"
    >
      <div className="bg-[#0b1326] border border-slate-700/80 rounded-2xl max-w-2xl w-full max-h-[90vh] flex flex-col shadow-2xl overflow-hidden animate-in fade-in zoom-in-95 duration-150">
        {/* Header */}
        <div className="flex items-center justify-between p-6 border-b border-slate-800 bg-[#070d18]">
          <div className="flex items-center gap-3">
            <div className="w-10 h-10 rounded-xl bg-cyan-500/10 border border-cyan-500/30 flex items-center justify-center text-cyan-400">
              <Download className="w-5 h-5" />
            </div>
            <div>
              <h3 id="install-plan-title" className="text-base font-bold text-white">
                {loading ? 'Carregando plano de instalação...' : (plan?.title ?? 'Plano de Instalação')}
              </h3>
              <p className="text-xs text-slate-400">
                {plan?.toolKey ? `Ferramenta: ${plan.toolKey}` : 'Consulte os detalhes antes de prosseguir'}
              </p>
            </div>
          </div>
          <button
            onClick={onClose}
            className="p-2 text-slate-400 hover:text-white rounded-lg hover:bg-slate-800 transition"
            aria-label="Fechar modal"
          >
            <X className="w-5 h-5" />
          </button>
        </div>

        {/* Content */}
        <div className="p-6 overflow-y-auto space-y-6 flex-1 text-sm text-slate-300">
          {loading && (
            <div className="flex flex-col items-center justify-center py-12 space-y-3">
              <Loader2 className="w-8 h-8 text-cyan-400 animate-spin" />
              <p className="text-slate-400 text-xs">Consultando plano com o host Windows...</p>
            </div>
          )}

          {error && (
            <div className="p-4 rounded-xl bg-red-950/40 border border-red-500/30 text-red-300 flex items-start gap-3">
              <AlertTriangle className="w-5 h-5 text-red-400 shrink-0 mt-0.5" />
              <div>
                <p className="font-semibold text-xs">Erro ao consultar plano de instalação</p>
                <p className="text-xs text-red-300/80 mt-1">{error}</p>
              </div>
            </div>
          )}

          {plan && !loading && (
            <>
              {/* Description */}
              <p className="text-xs text-slate-300 leading-relaxed bg-[#0e172a] p-3.5 rounded-xl border border-slate-800/80">
                {plan.description}
              </p>

              {/* Warning Notice if elevation or special caution required */}
              {plan.requiresElevation && (
                <div className="p-3.5 rounded-xl bg-amber-950/40 border border-amber-500/30 text-amber-200 flex items-start gap-2.5">
                  <Shield className="w-5 h-5 text-amber-400 shrink-0 mt-0.5" />
                  <div className="text-xs space-y-0.5">
                    <span className="font-bold">Requer Privilégios de Administrador</span>
                    <p className="text-amber-200/80">
                      Esta instalação pode solicitar confirmação UAC do Windows para registrar binários ou serviços no sistema.
                    </p>
                  </div>
                </div>
              )}

              {plan.warningNotice && (
                <div className="p-3 rounded-xl bg-slate-900 border border-slate-700 text-xs text-slate-300 flex items-start gap-2">
                  <AlertTriangle className="w-4 h-4 text-cyan-400 shrink-0 mt-0.5" />
                  <span>{plan.warningNotice}</span>
                </div>
              )}

              {/* Metrics Grid */}
              <div className="grid grid-cols-3 gap-3">
                <div className="p-3 rounded-xl bg-[#0a1222] border border-slate-800">
                  <div className="flex items-center gap-1.5 text-slate-400 text-[11px]">
                    <Clock className="w-3.5 h-3.5 text-cyan-400" />
                    <span>Tempo estimado</span>
                  </div>
                  <div className="text-base font-bold text-white mt-1">
                    ~{plan.estimatedTime || (plan.estimatedMinutes ? `${plan.estimatedMinutes} min` : "2-5 min")}
                  </div>
                </div>

                <div className="p-3 rounded-xl bg-[#0a1222] border border-slate-800">
                  <div className="flex items-center gap-1.5 text-slate-400 text-[11px]">
                    <Download className="w-3.5 h-3.5 text-cyan-400" />
                    <span>Download aprox.</span>
                  </div>
                  <div className="text-base font-bold text-white mt-1">
                    {(plan.downloadSizeMb ?? 0) > 0 ? `~${plan.estimatedDownloadSize ?? `${plan.downloadSizeMb ?? 100} MB`}` : 'Incluso/Sistema'}
                  </div>
                </div>

                <div className="p-3 rounded-xl bg-[#0a1222] border border-slate-800">
                  <div className="flex items-center gap-1.5 text-slate-400 text-[11px]">
                    <HardDrive className="w-3.5 h-3.5 text-cyan-400" />
                    <span>Elevação</span>
                  </div>
                  <div className="text-base font-bold text-white mt-1">
                    {plan.requiresElevation ? 'UAC Sim' : 'Usuário padrão'}
                  </div>
                </div>
              </div>

              {/* Dependencies if any */}
              {plan.dependencies && plan.dependencies.length > 0 && (
                <div className="space-y-1.5">
                  <span className="text-[11px] font-bold uppercase tracking-wider text-slate-400">
                    Dependências prévias
                  </span>
                  <div className="flex flex-wrap gap-1.5">
                    {plan.dependencies.map((dep) => (
                      <span
                        key={dep}
                        className="px-2.5 py-1 text-xs rounded-lg bg-slate-800 border border-slate-700 text-slate-200"
                      >
                        {dep}
                      </span>
                    ))}
                  </div>
                </div>
              )}

              {/* Steps Sequence */}
              <div className="space-y-2.5">
                <span className="text-[11px] font-bold uppercase tracking-wider text-slate-400">
                  Passos de Execução ({plan.steps.length})
                </span>
                <div className="space-y-2">
                  {plan.steps.map((st) => (
                    <div
                      key={st.stepNumber}
                      className="p-3 rounded-xl bg-[#090f1d] border border-slate-800/80 space-y-1.5"
                    >
                      <div className="flex items-center gap-2">
                        <span className="w-5 h-5 rounded-full bg-cyan-500/20 text-cyan-400 text-xs font-bold flex items-center justify-center shrink-0">
                          {st.stepNumber}
                        </span>
                        <span className="font-semibold text-xs text-white">{st.title}</span>
                      </div>
                      <p className="text-xs text-slate-400 pl-7">{st.description}</p>
                      {st.command && (
                        <div className="ml-7 p-2 rounded-lg bg-black/60 border border-slate-800 text-[11px] font-mono text-cyan-300 flex items-center gap-1.5">
                          <Terminal className="w-3.5 h-3.5 text-slate-500 shrink-0" />
                          <span className="truncate">{st.command}</span>
                        </div>
                      )}
                    </div>
                  ))}
                </div>
              </div>

              {/* Tool specific options */}
              {plan.toolKey === 'whisper' && (
                <label className="flex items-center gap-2.5 p-3 rounded-xl bg-[#090f1d] border border-slate-800 cursor-pointer text-xs">
                  <input
                    type="checkbox"
                    checked={preferCuda}
                    onChange={(e) => setPreferCuda(e.target.checked)}
                    className="w-4 h-4 rounded border-slate-700 text-cyan-500 focus:ring-cyan-500/30"
                  />
                  <span>Habilitar suporte com aceleração CUDA (NVIDIA GPU) caso detectado</span>
                </label>
              )}

              {/* Custom destination if allowed */}
              <div className="space-y-1">
                <label className="text-[11px] text-slate-400 font-semibold">
                  Caminho de destino personalizado (opcional)
                </label>
                <input
                  type="text"
                  placeholder={plan.targetDirectory ?? 'Padrão do instalador / WinGet'}
                  value={customPath}
                  onChange={(e) => setCustomPath(e.target.value)}
                  className="w-full px-3 py-2 text-xs bg-[#070d18] border border-slate-800 rounded-lg text-slate-200 placeholder-slate-600 focus:outline-none focus:border-cyan-500"
                />
              </div>
            </>
          )}
        </div>

        {/* Footer actions */}
        <div className="p-4 border-t border-slate-800 bg-[#070d18] flex items-center justify-end gap-3">
          <button
            onClick={onClose}
            className="px-4 py-2 text-xs font-semibold rounded-xl text-slate-300 hover:text-white hover:bg-slate-800 transition"
          >
            Cancelar
          </button>
          <button
            disabled={loading || !plan}
            onClick={() => {
              if (plan) {
                onConfirm(plan.toolKey, customPath.trim() || undefined, preferCuda, plan.approvalToken || undefined);
              }
            }}
            className="px-5 py-2 text-xs font-bold rounded-xl bg-cyan-600 hover:bg-cyan-500 text-white transition disabled:opacity-50 disabled:cursor-not-allowed flex items-center gap-2 shadow-lg shadow-cyan-600/20"
          >
            <CheckCircle2 className="w-4 h-4" />
            Confirmar e Iniciar Instalação
          </button>
        </div>
      </div>
    </div>
  );
};
