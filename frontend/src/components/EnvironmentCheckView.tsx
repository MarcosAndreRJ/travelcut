import React, { useCallback, useEffect, useRef, useState } from 'react';
import {
  CheckCircle2,
  AlertCircle,
  RefreshCw,
  ArrowRight,
  Server,
  Layers,
  Wrench,
  HardDrive,
  Cpu,
  Shield,
  HelpCircle
} from 'lucide-react';
import type {
  EditorCapabilities,
  EnvironmentAssistantDiagnosticDto,
  EnvironmentInstallPlanDto,
  ToolInstallProgressDto,
  TestToolResponse
} from '../types/ipc';
import { ipc } from '../services/ipcBridge';
import { EditorBadge } from './editor/EditorPrimitives';
import { isEditorSimulated } from './editor/editorModel';
import {
  useIntegrationStatus
} from './environment/useIntegrationStatus';
import {
  buildIntegrationRows,
  formatCounts,
  overallHeadline
} from './environment/integrationStatus';
import { InstallPlanModal } from './environment/InstallPlanModal';
import { ModelDownloadModal } from './environment/ModelDownloadModal';
import { ActiveJobBanner } from './environment/ActiveJobBanner';
import { ToolActionPanel } from './environment/ToolActionPanel';

interface EnvironmentCheckViewProps {
  editorCaps: EditorCapabilities | null;
  onRefresh: () => void | Promise<void>;
  onContinue: () => void;
}

// Mapeamento dos IDs de linha de integração para toolKeys suportadas pelo backend
const TOOL_KEY_MAP: Record<string, string> = {
  ffmpeg: 'ffmpeg',
  python: 'python',
  whisper: 'whisper',
  hyperframes: 'hyperframes',
  davinci: 'davinci'
};

export const EnvironmentCheckView: React.FC<EnvironmentCheckViewProps> = ({
  editorCaps,
  onRefresh,
  onContinue
}) => {
  const status = useIntegrationStatus();
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [showReasons, setShowReasons] = useState<boolean>(false);
  const [isRefreshing, setIsRefreshing] = useState(false);

  // Diagnóstico completo do assistente de ambiente (F12)
  const [assistantDiag, setAssistantDiag] = useState<EnvironmentAssistantDiagnosticDto | null>(null);
  const [assistantError, setAssistantError] = useState<string | null>(null);

  // Estados de ações de ferramentas (testes, localização e configuração de caminho)
  const [toolActionState, setToolActionState] = useState<
    Record<
      string,
      {
        locating?: boolean;
        testing?: boolean;
        candidates?: string[];
        testResult?: TestToolResponse | null;
        message?: string | null;
        error?: string | null;
      }
    >
  >({});

  // Modal de Plano de Instalação
  const [installPlanModal, setInstallPlanModal] = useState<{
    toolKey: string;
    plan: EnvironmentInstallPlanDto | null;
    loading: boolean;
    error: string | null;
  } | null>(null);

  // Job de Instalação Ativo
  const [activeInstallJob, setActiveInstallJob] = useState<{
    toolKey: string;
    jobId: string;
    progress: ToolInstallProgressDto;
    isCanceling: boolean;
  } | null>(null);

  // Modal de Download do Modelo Whisper (large-v3)
  const [isModelDownloadModalOpen, setIsModelDownloadModalOpen] = useState(false);

  // Job de Download de Modelo Ativo
  const [activeModelJob, setActiveModelJob] = useState<{
    jobId: string;
    progress: ToolInstallProgressDto;
    isCanceling: boolean;
  } | null>(null);

  // Timers de polling para jobs ativos
  const installPollRef = useRef<ReturnType<typeof setInterval> | null>(null);
  const modelPollRef = useRef<ReturnType<typeof setInterval> | null>(null);

  // Limpa timers ao desmontar
  useEffect(() => {
    return () => {
      if (installPollRef.current) clearInterval(installPollRef.current);
      if (modelPollRef.current) clearInterval(modelPollRef.current);
    };
  }, []);

  // Diagnóstico do assistente na montagem
  const fetchAssistantDiag = useCallback(async () => {
    try {
      const diag = await ipc.diagnoseEnvironment();
      setAssistantDiag(diag);
      setAssistantError(null);
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : String(err);
      setAssistantError(`Não foi possível diagnosticar o ambiente: ${msg}`);
    }
  }, []);

  useEffect(() => {
    void fetchAssistantDiag();
  }, [fetchAssistantDiag]);

  // Atualização combinada (integrações existentes + diagnóstico F12)
  const handleRefresh = async () => {
    setIsRefreshing(true);
    try {
      await Promise.all([
        status.refresh(),
        fetchAssistantDiag(),
        Promise.resolve(onRefresh())
      ]);
    } finally {
      setIsRefreshing(false);
    }
  };

  // --- Operações de Ferramenta (A3) ---

  const handleLocateTool = async (toolKey: string) => {
    setToolActionState((prev) => ({
      ...prev,
      [toolKey]: {
        ...prev[toolKey],
        locating: true,
        error: null,
        message: 'Buscando instalações existentes no sistema...'
      }
    }));

    try {
      const res = await ipc.autoLocateTool(toolKey);
      setToolActionState((prev) => ({
        ...prev,
        [toolKey]: {
          ...prev[toolKey],
          locating: false,
          candidates: res.candidates,
          message:
            res.candidates.length > 0
              ? `Encontrada(s) ${res.candidates.length} instalação(ões) candidata(s).`
              : 'Nenhuma instalação existente encontrada nos locais padrão.'
        }
      }));
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : String(err);
      setToolActionState((prev) => ({
        ...prev,
        [toolKey]: {
          ...prev[toolKey],
          locating: false,
          error: `Falha ao localizar ferramenta: ${msg}`
        }
      }));
    }
  };

  const handleConfigurePath = async (toolKey: string, customPath: string) => {
    setToolActionState((prev) => ({
      ...prev,
      [toolKey]: {
        ...prev[toolKey],
        error: null,
        message: 'Gravando caminho configurado...'
      }
    }));

    try {
      await ipc.configureToolPath(toolKey, customPath);
      setToolActionState((prev) => ({
        ...prev,
        [toolKey]: {
          ...prev[toolKey],
          message: 'Caminho configurado com sucesso! Atualizando diagnóstico...',
          candidates: []
        }
      }));
      await handleRefresh();
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : String(err);
      setToolActionState((prev) => ({
        ...prev,
        [toolKey]: {
          ...prev[toolKey],
          error: `Falha ao salvar caminho: ${msg}`
        }
      }));
    }
  };

  const handleTestTool = async (toolKey: string) => {
    setToolActionState((prev) => ({
      ...prev,
      [toolKey]: {
        ...prev[toolKey],
        testing: true,
        error: null,
        testResult: null,
        message: 'Executando teste funcional da ferramenta...'
      }
    }));

    try {
      const res = await ipc.testTool(toolKey);
      setToolActionState((prev) => ({
        ...prev,
        [toolKey]: {
          ...prev[toolKey],
          testing: false,
          testResult: res,
          message: res.success ? 'Teste concluído com sucesso.' : 'O teste da ferramenta falhou.'
        }
      }));
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : String(err);
      setToolActionState((prev) => ({
        ...prev,
        [toolKey]: {
          ...prev[toolKey],
          testing: false,
          error: `Erro ao testar ferramenta: ${msg}`
        }
      }));
    }
  };

  // --- Fluxo de Instalação Assistida ---

  const handleOpenInstallPlan = async (toolKey: string) => {
    setInstallPlanModal({
      toolKey,
      plan: null,
      loading: true,
      error: null
    });

    try {
      const plan = await ipc.getEnvironmentInstallPlan(toolKey);
      setInstallPlanModal({
        toolKey,
        plan,
        loading: false,
        error: null
      });
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : String(err);
      setInstallPlanModal({
        toolKey,
        plan: null,
        loading: false,
        error: `Não foi possível carregar o plano de instalação: ${msg}`
      });
    }
  };

  const handleConfirmInstall = async (
    toolKey: string,
    customInstallPath?: string,
    preferCuda?: boolean,
    approvalToken?: string
  ) => {
    setInstallPlanModal(null);

    try {
      const { jobId } = await ipc.startToolInstall({
        toolKey,
        confirmed: true,
        customInstallPath,
        preferCuda,
        approvalToken
      });

      const initialProgress: ToolInstallProgressDto = {
        jobId,
        toolKey,
        stage: 'Iniciando',
        progressPercent: 0,
        currentMessage: 'Iniciando preparação da instalação...',
        outputLogs: [],
        canCancel: true,
        isCompleted: false,
        isSuccess: false,
        isFailed: false,
        isCanceled: false
      };

      setActiveInstallJob({
        toolKey,
        jobId,
        progress: initialProgress,
        isCanceling: false
      });

      // Inicia polling a cada 800ms
      if (installPollRef.current) clearInterval(installPollRef.current);
      installPollRef.current = setInterval(async () => {
        try {
          const prog = await ipc.getToolInstallProgress(initialProgress.jobId);
          if (prog) {
            setActiveInstallJob(current => (current ? { ...current, progress: prog } : null));
            if (prog.isCompleted) {
              if (installPollRef.current) {
                clearInterval(installPollRef.current);
                installPollRef.current = null;
              }
              if (prog.isSuccess) {
                void handleRefresh();
              }
            }
          }
        } catch (_) {
          // Erro de rede ou desconexão temporária
        }
      }, 800);
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : String(err);
      setAssistantError(`Falha ao iniciar instalação: ${msg}`);
    }
  };

  const handleCancelInstall = async () => {
    if (!activeInstallJob || activeInstallJob.isCanceling) return;

    // Distingue solicitação de cancelamento de cancelamento concluído
    setActiveInstallJob((prev) => (prev ? { ...prev, isCanceling: true } : null));

    try {
      await ipc.cancelToolInstall(activeInstallJob.jobId);
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : String(err);
      setAssistantError(`Falha ao solicitar cancelamento da instalação: ${msg}`);
    }
  };

  // --- Fluxo de Download do Modelo Whisper ---

  const handleConfirmModelDownload = async (modelName: string) => {
    setIsModelDownloadModalOpen(false);

    try {
      const { jobId } = await ipc.startModelDownload({
        modelName,
        confirmed: true
      });

      const initialProgress: ToolInstallProgressDto = {
        jobId,
        toolKey: 'whisper',
        stage: 'Iniciando download',
        progressPercent: 0,
        currentMessage: 'Conectando ao repositório do modelo...',
        outputLogs: [],
        canCancel: true,
        isCompleted: false,
        isSuccess: false,
        isFailed: false,
        isCanceled: false
      };

      setActiveModelJob({
        jobId,
        progress: initialProgress,
        isCanceling: false
      });

      if (modelPollRef.current) clearInterval(modelPollRef.current);
      modelPollRef.current = setInterval(async () => {
        try {
          const prog = await ipc.getModelDownloadProgress(initialProgress.jobId);
          if (prog) {
            setActiveModelJob(current => (current ? { ...current, progress: prog } : null));
            if (prog.isCompleted) {
              if (modelPollRef.current) {
                clearInterval(modelPollRef.current);
                modelPollRef.current = null;
              }
              if (prog.isSuccess) {
                void handleRefresh();
              }
            }
          }
        } catch (_) {}
      }, 800);
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : String(err);
      setAssistantError(`Falha ao iniciar download do modelo: ${msg}`);
    }
  };

  const handleCancelModelDownload = async () => {
    if (!activeModelJob || activeModelJob.isCanceling) return;

    // Distingue solicitação de cancelamento de cancelamento concluído
    setActiveModelJob((prev) => (prev ? { ...prev, isCanceling: true } : null));

    try {
      await ipc.cancelModelDownload(activeModelJob.jobId);
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : String(err);
      setAssistantError(`Falha ao cancelar download do modelo: ${msg}`);
    }
  };

  // Linhas de integrações para a visualização
  const rows = buildIntegrationRows({
    isMock: false,
    analysisEnv: status.analysisEnv,
    analysisError: status.analysisError,
    editorCaps,
    motionCaps: status.motionCaps,
    motionError: status.motionError
  });

  const headline = overallHeadline(rows);


  return (
    <div className="flex-1 overflow-y-auto bg-[#070d18] text-slate-100 p-8 space-y-8 font-sans">
      {/* Top Header & Breadcrumb */}
      <div className="flex flex-col md:flex-row md:items-center justify-between gap-4">
        <div>
          <div className="flex items-center gap-2 text-xs text-slate-400 font-mono tracking-wider uppercase">
            <span>TravelCut Studio</span>
            <span>/</span>
            <span className="text-cyan-400">Verificação do Ambiente & Instalações</span>
          </div>
          <h1 className="text-2xl font-bold text-white mt-1">
            Diagnóstico e Instalação Assistida
          </h1>
          <p className="text-xs text-slate-400 mt-1">
            Verifique dependências, consulte planos transparentes, instale ferramentas ou configure caminhos locais.
          </p>
        </div>

        <div className="flex items-center gap-3">
          <button
            onClick={() => setShowReasons(!showReasons)}
            className={`px-3 py-2 rounded-lg text-xs font-medium border transition-colors flex items-center gap-2 ${
              showReasons
                ? 'bg-slate-800 border-slate-700 text-cyan-300'
                : 'bg-slate-900 border-slate-800 text-slate-400 hover:text-slate-200'
            }`}
          >
            <HelpCircle className="w-3.5 h-3.5" />
            {showReasons ? 'Ocultar justificativas' : 'Por que precisamos?'}
          </button>
          <button
            onClick={handleRefresh}
            disabled={isRefreshing || status.loading}
            className="px-4 py-2 rounded-lg text-xs font-semibold bg-slate-900 hover:bg-slate-800 border border-slate-700 text-slate-200 transition-colors flex items-center gap-2"
          >
            <RefreshCw
              className={`w-3.5 h-3.5 ${
                isRefreshing || status.loading ? 'animate-spin text-cyan-400' : ''
              }`}
            />
            Atualizar verificações
          </button>
        </div>
      </div>

      {/* Alerta de erro geral */}
      {assistantError && (
        <div className="rounded-xl border border-rose-500/30 bg-rose-950/30 p-4 text-xs text-rose-300 flex items-center justify-between">
          <div className="flex items-center gap-2">
            <AlertCircle className="w-4 h-4 text-rose-400 shrink-0" />
            <span>{assistantError}</span>
          </div>
          <button
            onClick={() => setAssistantError(null)}
            className="text-rose-400 hover:text-rose-200 underline font-mono text-[11px]"
          >
            fechar
          </button>
        </div>
      )}

      {/* Banners de Jobs Ativos (Instalação e Download de Modelo) */}
      {activeInstallJob && (
        <ActiveJobBanner
          title={`Instalação de ${activeInstallJob.toolKey.toUpperCase()}`}
          progress={activeInstallJob.progress}
          isCanceling={activeInstallJob.isCanceling}
          onCancel={handleCancelInstall}
          onDismiss={
            activeInstallJob.progress.isCompleted
              ? () => setActiveInstallJob(null)
              : undefined
          }
        />
      )}

      {activeModelJob && (
        <ActiveJobBanner
          title="Download do Modelo Whisper (large-v3)"
          progress={activeModelJob.progress}
          isCanceling={activeModelJob.isCanceling}
          onCancel={handleCancelModelDownload}
          onDismiss={
            activeModelJob.progress.isCompleted
              ? () => setActiveModelJob(null)
              : undefined
          }
        />
      )}

      {/* Banner Resumo de Estado Geral */}
      <div className="rounded-2xl border border-cyan-500/20 bg-gradient-to-r from-[#0a1528] to-[#0d1c33] p-6 shadow-xl flex flex-col md:flex-row items-start md:items-center justify-between gap-6">
        <div className="flex items-center gap-4">
          <div className="w-12 h-12 rounded-xl bg-cyan-500/10 border border-cyan-500/30 flex items-center justify-center shrink-0">
            {headline.tone === 'emerald' ? (
              <CheckCircle2 className="w-6 h-6 text-emerald-400" />
            ) : (
              <AlertCircle className="w-6 h-6 text-amber-300" />
            )}
          </div>
          <div>
            <h2 className="text-lg font-bold text-white" role="status">
              {headline.title}
            </h2>
            <p className="text-xs text-slate-300 mt-0.5">{formatCounts(rows)}</p>
          </div>
        </div>

        {/* Informações do Sistema do Host */}
        {assistantDiag?.systemInfo && (
          <div className="flex flex-wrap items-center gap-4 text-xs font-mono text-slate-400 border-t md:border-t-0 md:border-l border-slate-800 pt-4 md:pt-0 md:pl-6">
            <div className="flex items-center gap-1.5" title="Sistema Operacional">
              <Cpu className="w-3.5 h-3.5 text-cyan-400" />
              <span>{assistantDiag.systemInfo.osDescription}</span>
            </div>
            <div className="flex items-center gap-1.5" title="Espaço livre no disco de cache">
              <HardDrive className="w-3.5 h-3.5 text-cyan-400" />
              <span>
                {(assistantDiag.systemInfo.freeDiskSpaceMb / 1024).toFixed(1)} GB livres
              </span>
            </div>
            <div className="flex items-center gap-1.5" title="Gerenciador WinGet">
              <Shield className="w-3.5 h-3.5 text-cyan-400" />
              <span>
                WinGet: {assistantDiag.systemInfo.hasWinget ? 'Disponível' : 'Ausente'}
              </span>
            </div>
          </div>
        )}
      </div>

      {/* Grid Principal: 2 Colunas */}
      <div className="grid grid-cols-1 lg:grid-cols-3 gap-8 items-start">
        {/* Coluna Esquerda: Lista de Ferramentas e Ações Assistidas */}
        <div className="lg:col-span-2 space-y-4">
          <div className="flex items-center justify-between pb-2 border-b border-slate-800">
            <h3 className="text-sm font-bold uppercase tracking-wider text-slate-300 flex items-center gap-2">
              <Layers className="w-4 h-4 text-cyan-400" />
              Componentes & Ferramentas do Ambiente
            </h3>
            <span className="text-xs text-slate-500 font-mono">
              Total: {rows.length} itens auditados
            </span>
          </div>

          <div className="space-y-3">
            {rows.map((row) => {
              const isSelected = selectedId === row.id;
              const toolKey = TOOL_KEY_MAP[row.id];
              const actionState = toolKey ? toolActionState[toolKey] : undefined;

              return (
                <div
                  key={row.id}
                  className={`rounded-xl border transition-all ${
                    isSelected
                      ? 'bg-[#0c182c] border-cyan-500/40 shadow-lg'
                      : 'bg-[#09101d] border-slate-800/80 hover:border-slate-700'
                  }`}
                >
                  <div className="p-4 flex flex-col sm:flex-row sm:items-center justify-between gap-3">
                    <div className="flex items-start sm:items-center gap-3">
                      <div className="mt-0.5 sm:mt-0"><EditorBadge badge={{ tone: row.tone, label: row.label }} /></div>
                      <div>
                        <div className="flex items-center gap-2">
                          <span className="font-semibold text-slate-200 text-sm">
                            {row.name}
                          </span>
                          {row.version && (
                            <span className="text-[11px] font-mono text-slate-400 bg-slate-800 px-1.5 py-0.5 rounded">
                              {row.version}
                            </span>
                          )}
                        </div>
                        <div className="text-xs text-slate-400 mt-0.5 flex flex-wrap items-center gap-2">
                          <span>{row.detail}</span>
                          <span className="text-slate-600">•</span>
                          <span className="text-slate-500 text-[11px]">Fonte: {row.source}</span>
                        </div>
                      </div>
                    </div>

                    <div className="flex items-center gap-2 shrink-0 self-end sm:self-auto">
                      {toolKey && (
                        <button
                          onClick={() => handleTestTool(toolKey)}
                          disabled={actionState?.testing}
                          className="px-2.5 py-1 rounded text-xs font-mono font-medium border border-slate-700 hover:border-slate-600 bg-slate-800/80 text-slate-300 hover:text-white transition-colors"
                          title="Executar verificação funcional do comando"
                        >
                          {actionState?.testing ? 'Testando...' : 'Testar'}
                        </button>
                      )}
                      <button
                        onClick={() => setSelectedId(isSelected ? null : row.id)}
                        className="px-3 py-1 rounded text-xs font-semibold bg-cyan-950/40 hover:bg-cyan-900/50 border border-cyan-800/40 text-cyan-300 transition-colors"
                      >
                        {isSelected ? 'Ocultar' : 'Ações / Detalhes'}
                      </button>
                    </div>
                  </div>

                  {/* Painel expandido com Ações Assistidas e Detalhes */}
                  {isSelected && (
                    <div className="border-t border-slate-800/80 bg-[#070c16] p-4 rounded-b-xl space-y-4">
                      {/* Explicação da ferramenta */}
                      {showReasons && (
                        <div className="rounded-lg bg-cyan-950/20 border border-cyan-500/20 p-3 text-xs text-cyan-200">
                          <span className="font-semibold text-cyan-400">Por que é necessário? </span>
                          {row.detail}
                        </div>
                      )}

                      {/* Painel de Ações de Instalação e Configuração */}
                      {toolKey ? (
                        <ToolActionPanel
                          toolKey={toolKey}
                          toolName={row.name}
                          isReady={row.tone === 'emerald'}
                          canInstall={row.tone !== 'emerald'}
                          canDownloadModel={toolKey === 'whisper'}
                          onLocate={handleLocateTool}
                          onConfigurePath={handleConfigurePath}
                          onOpenInstallPlan={handleOpenInstallPlan}
                          onTestTool={handleTestTool}
                          onOpenModelDownload={
                            toolKey === 'whisper'
                              ? () => setIsModelDownloadModalOpen(true)
                              : undefined
                          }
                          locating={actionState?.locating}
                          testing={actionState?.testing}
                          candidates={actionState?.candidates}
                          testResult={actionState?.testResult}
                          message={actionState?.message || undefined}
                          error={actionState?.error}
                        />
                      ) : (
                        <div className="text-xs text-slate-400 p-2">
                          {row.detail}
                        </div>
                      )}
                    </div>
                  )}
                </div>
              );
            })}
          </div>
        </div>

        {/* Coluna Direita: Modo de Execução, Guia de Host e Próximos Passos */}
        <div className="space-y-6">
          {/* Card: Modo de Execução DaVinci */}
          <div className="rounded-2xl bg-[#0a1222] border border-slate-800 p-5 space-y-4 shadow-md">
            <h4 className="text-xs font-bold uppercase tracking-wider text-slate-400 flex items-center gap-2">
              <Server className="w-4 h-4 text-cyan-400" />
              Modo de Execução DaVinci
            </h4>

            <div className="p-4 rounded-xl bg-slate-900/60 border border-slate-800 space-y-2">
              <div className="flex items-center justify-between">
                <span className="text-xs text-slate-400">Adaptador Atual</span>
                <span className="font-mono text-xs font-semibold text-slate-200">
                  {editorCaps?.adapterMode === 'davinci'
                    ? 'DaVinci Resolve Studio (Real)'
                    : 'Mock / Simulado'}
                </span>
              </div>
              <div className="flex items-center justify-between">
                <span className="text-xs text-slate-400">Estado</span>
                <EditorBadge
                  badge={{
                    tone: isEditorSimulated(editorCaps) ? 'amber' : 'emerald',
                    label: editorCaps?.environmentState ?? 'SIMULATED'
                  }}
                />
              </div>
            </div>

            <p className="text-xs text-slate-400 leading-relaxed">
              O TravelCut opera com integração direta ao DaVinci Studio ou em modo simulado para desenvolvimento e validação.
            </p>
          </div>

          {/* Card: Próximos Passos */}
          <div className="rounded-2xl bg-gradient-to-br from-cyan-950/20 to-slate-900/40 border border-cyan-900/30 p-5 space-y-4">
            <h4 className="text-xs font-bold uppercase tracking-wider text-cyan-400 flex items-center gap-2">
              <Wrench className="w-4 h-4" />
              Próxima Etapa
            </h4>
            <p className="text-xs text-slate-300 leading-relaxed">
              Após confirmar as dependências e ferramentas necessárias, avance para preparar e organizar o projeto.
            </p>

            <button
              onClick={onContinue}
              className="w-full py-3 px-4 rounded-xl font-semibold text-xs bg-cyan-600 hover:bg-cyan-500 text-slate-950 transition-all flex items-center justify-center gap-2 shadow-lg shadow-cyan-600/20"
            >
              Iniciar preparação do projeto <ArrowRight className="w-4 h-4" />
            </button>
          </div>
        </div>
      </div>

      {/* Modal de Plano de Instalação */}
      {installPlanModal && (
        <InstallPlanModal
          plan={installPlanModal.plan}
          loading={installPlanModal.loading}
          error={installPlanModal.error}
          onConfirm={handleConfirmInstall}
          onClose={() => setInstallPlanModal(null)}
        />
      )}

      {/* Modal de Confirmação de Download de Modelo Whisper */}
      <ModelDownloadModal
        isOpen={isModelDownloadModalOpen}
        onConfirm={handleConfirmModelDownload}
        onClose={() => setIsModelDownloadModalOpen(false)}
      />
    </div>
  );
};
