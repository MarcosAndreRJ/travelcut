import React, { useState, useEffect, useCallback, useRef } from 'react';
import { FolderOpen, Loader2, AlertCircle, Plus } from 'lucide-react';
import { SplashScreen } from './components/SplashScreen';
import { Sidebar, NavigationTab } from './components/Sidebar';
import { TopHeader } from './components/TopHeader';
import { DashboardOverviewView } from './components/DashboardOverviewView';
import { EnvironmentCheckView } from './components/EnvironmentCheckView';
import { WelcomeStartView } from './components/WelcomeStartView';
import { ReviewDecisionsView } from './components/ReviewDecisionsView';
import {
  describeDecisionError,
  decisionPlanErrorMessage,
  describePlanSkippedReason,
  type ReviewDecisionAction,
  type ReviewErrorInfo,
  type ReviewNotice
} from './components/review/reviewModel';
import { LibraryTranscriptionView } from './components/LibraryTranscriptionView';
import { TimelinePlanningView } from './components/TimelinePlanningView';
import { SettingsIntegrationsView } from './components/SettingsIntegrationsView';
import { MontageOverviewView } from './components/MontageOverviewView';
import { ResolveHubView } from './components/ResolveHubView';
import { ErrorBoundary } from './components/ErrorBoundary';
import { ModelDownloadBanner } from './components/environment/ModelDownloadBanner';
import { usePipelineCompletionNotifier } from './components/analysis/usePipelineCompletionNotifier';
import { integrationStatusStore } from './services/integrationStatusStore';
import { modelDownloadTracker } from './services/modelDownloadTracker';
import { ipc } from './services/ipcBridge';
import { ProjectSummary, EditSuggestion, EditorCapabilities, DecisionResultDto, SmartZoomParametersDto } from './types/ipc';
import {
  CREATE_MISSING_FOLDERS_QUESTION,
  isMissingFoldersError,
  projectOperationErrorMessage
} from './services/projectOpenFlow';

/** Revisor padrão das decisões (sem identidade de usuário nesta versão). */
const REVIEWER = 'Editor';

export const App: React.FC = () => {
  const [showSplash, setShowSplash] = useState<boolean>(true);
  // Sem projeto aberto o app começa nas boas-vindas; nenhum projeto é aberto ou criado automaticamente.
  const [currentTab, setCurrentTab] = useState<NavigationTab>('onboarding');
  const [project, setProject] = useState<ProjectSummary | null>(null);
  const [recentProjects, setRecentProjects] = useState<ProjectSummary[]>([]);
  const [suggestions, setSuggestions] = useState<EditSuggestion[]>([]);
  const [editorCaps, setEditorCaps] = useState<EditorCapabilities | null>(null);
  // Como a Biblioteca deve abrir após criar/abrir projeto; key força reaplicar o modo.
  const [libraryEntry, setLibraryEntry] = useState<{ mode: 'auto' | 'import' | 'clips'; key: number }>({ mode: 'auto', key: 0 });

  // Modais de Projeto: 'create' | 'open' | null
  const [modalMode, setModalMode] = useState<'create' | 'open' | null>(null);
  const [projectNameInput, setProjectNameInput] = useState<string>('');
  const [createBasePathInput, setCreateBasePathInput] = useState<string>('');
  const [projectPathInput, setProjectPathInput] = useState<string>('');
  const [isProjectLoading, setIsProjectLoading] = useState<boolean>(false);
  const [projectError, setProjectError] = useState<string | null>(null);
  // Criar projeto: cria as pastas canônicas (padrão). Abrir projeto: não cria nada sem confirmação.
  const [createFolders, setCreateFolders] = useState<boolean>(true);
  const [openCreateMissing, setOpenCreateMissing] = useState<boolean>(false);
  // Pedido de confirmação quando o host informa pastas canônicas ausentes (INVALID_FOUR_FOLDERS).
  const [missingFoldersPrompt, setMissingFoldersPrompt] = useState<{ path: string; message: string } | null>(null);

  // Ao trocar de modal, o pedido de confirmação anterior não deve reaparecer.
  useEffect(() => {
    setMissingFoldersPrompt(null);
  }, [modalMode]);

  // Sugestões são do projeto atual: recarregam ao trocar de projeto, ao abrir a Revisão e ao terminar a análise.
  const [suggestionsReloadKey, setSuggestionsReloadKey] = useState<number>(0);
  const [suggestionsLoading, setSuggestionsLoading] = useState<boolean>(false);
  const [suggestionsError, setSuggestionsError] = useState<string | null>(null);
  // Feedback de decisões (erro visível na Revisão e avisos não intrusivos).
  const [reviewError, setReviewError] = useState<ReviewErrorInfo | null>(null);
  const [reviewNotice, setReviewNotice] = useState<ReviewNotice | null>(null);
  const projectId = project?.projectId ?? null;
  const activeProjectIdRef = useRef<string | null>(null);

  useEffect(() => {
    activeProjectIdRef.current = projectId;
    setReviewError(null);
    setReviewNotice(null);
  }, [projectId]);

  const loadRecentProjects = async () => {
    try {
      const res = await ipc.send<{ projects: ProjectSummary[] }>('project.recent');
      setRecentProjects(res?.projects ?? []);
    } catch (err) {
      console.error('Falha ao carregar projetos recentes:', err);
    }
  };

  const handleCloseProject = useCallback(() => {
    activeProjectIdRef.current = null;
    setProject(null);
    setCurrentTab('onboarding');
    setSuggestions([]);
    setSuggestionsLoading(false);
    setSuggestionsError(null);
    setReviewError(null);
    setReviewNotice(null);
    setMissingFoldersPrompt(null);
    setModalMode(null);
    setProjectError(null);
    void loadRecentProjects();
  }, []);

  useEffect(() => {
    if (!projectId) {
      setSuggestions([]);
      setSuggestionsError(null);
      setSuggestionsLoading(false);
      return;
    }
    let cancelled = false;
    setSuggestionsLoading(true);
    setSuggestionsError(null);
    ipc
      .listSuggestions(projectId)
      .then((list) => {
        if (!cancelled && activeProjectIdRef.current === projectId) setSuggestions(list);
      })
      .catch((err: unknown) => {
        console.error('Falha ao carregar sugestões:', err);
        if (!cancelled && activeProjectIdRef.current === projectId) {
          setSuggestions([]);
          setSuggestionsError(describeDecisionError(err).message);
        }
      })
      .finally(() => {
        if (!cancelled && activeProjectIdRef.current === projectId) setSuggestionsLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [projectId, suggestionsReloadKey]);

  // Ao abrir a aba Revisão, a lista é recarregada do host (estado efetivo atual).
  useEffect(() => {
    if (currentTab === 'review') setSuggestionsReloadKey((k) => k + 1);
  }, [currentTab]);

  const handleAnalysisFinished = useCallback(() => {
    setSuggestionsReloadKey((k) => k + 1);
  }, []);

  useEffect(() => {
    const initialize = async () => {
      try {
        const caps = await ipc.send<EditorCapabilities>('editor.capabilities');
        setEditorCaps(caps);
      } catch (err) {
        console.error('Falha ao obter capacidades do editor:', err);
      }
      await loadRecentProjects();
    };

    initialize();
  }, []);

  // Projeto novo → importação; projeto existente → Biblioteca decide (importação se vazia, clipes caso contrário).
  const enterProject = async (summary: ProjectSummary, mode: 'auto' | 'import') => {
    setProject(summary);
    setModalMode(null);
    setLibraryEntry({ mode, key: Date.now() });
    setCurrentTab('library');
    // Recarrega sugestões mesmo quando o projeto já era o atual (o efeito acima só observa projectId).
    setSuggestionsReloadKey((k) => k + 1);
    loadRecentProjects();
  };

  // Recarrega contadores do projeto (ex.: após importação) sem criar pastas.
  const refreshProject = async () => {
    if (!project) return;
    try {
      const res = await ipc.openProject(project.rootPath, false);
      if (res?.summary) setProject(res.summary);
    } catch (err) {
      console.error('Falha ao atualizar resumo do projeto:', err);
    }
  };

  const handleSelectFolder = async (target: 'create' | 'open') => {
    try {
      const res = await ipc.send<{ selectedPath: string; canceled: boolean }>('dialog.selectFolder');
      if (res && !res.canceled && res.selectedPath) {
        if (target === 'create') {
          setCreateBasePathInput(res.selectedPath);
        } else {
          setProjectPathInput(res.selectedPath);
        }
        setProjectError(null);
      }
    } catch (err: any) {
      console.error('Erro ao selecionar pasta:', err);
    }
  };

  const handleCreateProject = async (name: string, basePath: string) => {
    const trimmedName = name.trim();
    const trimmedBase = basePath.trim();
    if (!trimmedName) {
      setProjectError('Por favor, informe o nome do projeto.');
      return;
    }
    if (!trimmedBase) {
      setProjectError('Por favor, selecione ou informe a pasta de destino.');
      return;
    }

    setIsProjectLoading(true);
    setProjectError(null);

    // Se o caminho base já não terminar com o nome do projeto, acrescentar
    let finalPath = trimmedBase;
    if (!finalPath.endsWith('\\' + trimmedName) && !finalPath.endsWith('/' + trimmedName)) {
      finalPath = finalPath.replace(/[\\/]+$/, '') + '\\' + trimmedName;
    }

    try {
      const res = await ipc.createProject(trimmedName, finalPath, createFolders);
      if (res && res.summary) {
        setProjectPathInput(finalPath);
        await enterProject(res.summary, 'import');
      }
    } catch (err: any) {
      console.error('Erro ao criar projeto:', err);
      setProjectError(projectOperationErrorMessage(err, 'create'));
    } finally {
      setIsProjectLoading(false);
    }
  };

  /**
   * Abre projeto existente. Por padrão não cria pastas: se o host responder INVALID_FOUR_FOLDERS,
   * pede confirmação explícita e só reenvia com autoCreateFolders=true depois do aceite do usuário.
   */
  const handleOpenProject = async (path: string, createMissingFolders: boolean = openCreateMissing) => {
    const trimmedPath = (path ?? '').trim();
    if (!trimmedPath) {
      setProjectError('Por favor, informe ou selecione o caminho da pasta raiz do projeto.');
      return;
    }

    setIsProjectLoading(true);
    setProjectError(null);
    setMissingFoldersPrompt(null);

    try {
      const res = await ipc.openProject(trimmedPath, createMissingFolders);
      if (res && res.summary) {
        setProjectPathInput(trimmedPath);
        await enterProject(res.summary, 'auto');
      }
    } catch (err: unknown) {
      if (!createMissingFolders && isMissingFoldersError(err)) {
        // Nada é criado aqui: aguarda a confirmação do usuário.
        setMissingFoldersPrompt({
          path: trimmedPath,
          message: err instanceof Error && err.message ? err.message : 'Faltam pastas canônicas no projeto.'
        });
      } else {
        console.error('Erro ao abrir projeto:', err);
        setProjectError(projectOperationErrorMessage(err, 'open'));
      }
    } finally {
      setIsProjectLoading(false);
    }
  };

  // ---- Revisão (F07): o host é a fonte de verdade. Cada ação devolve as sugestões afetadas (merge por id). ----
  const applyDecisionResult = (result: DecisionResultDto) => {
    const updated = result.suggestions ?? [];
    if (updated.length > 0) {
      const byId = new Map(updated.map((s) => [s.suggestionId, s] as const));
      setSuggestions((prev) => prev.map((s) => byId.get(s.suggestionId) ?? s));
    }

    const missing = result.missingSuggestionIds ?? [];
    const skippedPlanMessage = describePlanSkippedReason(result.planSkippedReason);
    if (missing.length > 0 || result.planError || skippedPlanMessage) {
      // Os avisos podem coexistir: não esconder o erro do plano atrás do aviso de ids ausentes.
      const parts: string[] = [];
      if (missing.length > 0) {
        parts.push(`${missing.length} candidato(s) não foram encontrados no host; a lista foi recarregada.`);
        setSuggestionsReloadKey((k) => k + 1);
      }
      if (result.planError) parts.push(decisionPlanErrorMessage(result.planError));
      if (skippedPlanMessage) parts.push(skippedPlanMessage);
      setReviewNotice({ tone: 'warning', text: parts.join(' ') });
    } else if (result.planChanged && result.planRevision !== null) {
      setReviewNotice({ tone: 'info', text: `Plano atualizado: revisão r${result.planRevision}.` });
    } else {
      setReviewNotice(null);
    }

    // Contadores do projeto vêm do host (sem recalcular aprovados localmente).
    void refreshProject();
  };

  const runDecision = async (operation: () => Promise<DecisionResultDto>): Promise<boolean> => {
    setReviewError(null);
    try {
      const result = await operation();
      applyDecisionResult(result);
      return true;
    } catch (err) {
      console.error('Falha na decisão editorial:', err);
      setReviewError(describeDecisionError(err));
      // Após timeout ou erro, a decisão pode ter sido gravada no host: recarrega do host em vez de manter estado antigo.
      setSuggestionsReloadKey((k) => k + 1);
      void refreshProject();
      return false;
    }
  };

  const handleDecision = (suggestionId: string, action: any) =>
    runDecision(() => ipc.submitDecision({ suggestionId, action: action as any, reviewer: REVIEWER }));

  const handleModify = (suggestionId: string, startSeconds: number, endSeconds: number) =>
    runDecision(() =>
      ipc.submitDecision({
        suggestionId,
        action: 'modify',
        reviewer: REVIEWER,
        modifiedStartSeconds: startSeconds,
        modifiedEndSeconds: endSeconds
      })
    );

const handleApplyZoom = (suggestionId: string, zoom: SmartZoomParametersDto) =>    runDecision(() =>      ipc.submitDecision({ suggestionId, action: 'approve', reviewer: REVIEWER, parameters: { zoom } })    );
  const handleUndoDecision = (suggestionId: string) => runDecision(() => ipc.undoDecision(suggestionId));

  const handleRedoDecision = (suggestionId: string) => runDecision(() => ipc.redoDecision(suggestionId));

  const handleBatchDecide = (suggestionIds: string[], action: ReviewDecisionAction) =>
    runDecision(() => ipc.batchDecide({ suggestionIds, action: action as any, reviewer: REVIEWER }));

  const clearReviewFeedback = () => {
    setReviewError(null);
    setReviewNotice(null);
  };

  const reloadSuggestions = () => setSuggestionsReloadKey((k) => k + 1);

  // Fim de ciclo do pipeline (análise + pós-análise) em qualquer tela: notificação única no shell.
  usePipelineCompletionNotifier(projectId);

  // Download concluído em qualquer tela: reconsulta forçada para todas as telas verem o novo estado.
  useEffect(() => modelDownloadTracker.onSuccess(() => void integrationStatusStore.refresh()), []);

  // Ao navegar, revalida leituras antigas; a Sidebar não fica presa no resultado da primeira carga.
  useEffect(() => {
    void integrationStatusStore.ensureFresh();
  }, [currentTab]);

  // Falha na reconsulta mantém o último estado conhecido; não há sucesso inventado.
  const handleRefreshCaps = async () => {
    try {
      const caps = await ipc.send<EditorCapabilities>('editor.capabilities');
      setEditorCaps(caps);
    } catch (err) {
      console.error('Falha ao consultar capacidades do editor:', err);
    }
  };

  if (showSplash) {
    return (
      <SplashScreen
        onFinish={() => setShowSplash(false)}
        autoDismiss={true}
      />
    );
  }

  return (
    <div className="h-screen w-screen bg-[#070d18] text-slate-100 flex overflow-hidden font-sans">
      {/* Fixed Left Sidebar */}
      <Sidebar
        currentTab={currentTab}
        onSelectTab={setCurrentTab}
        editorCaps={editorCaps}
      />

      {/* Main Content Area */}
      <div className="flex-1 flex flex-col h-full overflow-hidden">
        {/* Top Header */}
        <TopHeader
          project={project}
          onRefresh={handleRefreshCaps}
          onOpenProjectDialog={() => {
            setModalMode('open');
            setProjectError(null);
          }}
          onCloseProject={handleCloseProject}
        />

        {/* Download do modelo: visível em qualquer tela */}
        <ModelDownloadBanner />

        {/* Dynamic View Router */}
        <main className="flex-1 flex overflow-hidden">
          <ErrorBoundary
            fallbackTitle="Falha na visualização atual"
            onReset={() => setCurrentTab('onboarding')}
          >
            {currentTab === 'overview' && (
              <DashboardOverviewView
                project={project}
                editorCaps={editorCaps}
                onCreateProject={() => {
                  setModalMode('create');
                  setProjectError(null);
                }}
                onOpenProject={() => {
                  setModalMode('open');
                  setProjectError(null);
                }}
                onNavigateToCheck={() => setCurrentTab('environment')}
                onNavigateToTab={(tab) => setCurrentTab(tab)}
              />
            )}

            {currentTab === 'environment' && (
              <EnvironmentCheckView
                editorCaps={editorCaps}
                onRefresh={handleRefreshCaps}
                onContinue={() => setCurrentTab('overview')}
              />
            )}

            {currentTab === 'onboarding' && (
              <WelcomeStartView
                recentProjects={recentProjects}
                onCreateProject={() => {
                  setModalMode('create');
                  setProjectError(null);
                }}
                onOpenProject={() => {
                  setModalMode('open');
                  setProjectError(null);
                }}
                onOpenRecent={(p) => handleOpenProject(p.rootPath)}
              />
            )}

            {(currentTab === 'library' || currentTab === 'transcription') && (
              <LibraryTranscriptionView
                project={project}
                entryMode={libraryEntry.mode}
                entryKey={libraryEntry.key}
                onLibraryChanged={refreshProject}
                onAnalysisFinished={handleAnalysisFinished}
                onNavigateToReview={(sugId) => {
                  setCurrentTab('review');
                  void reloadSuggestions();
                }}
              />
            )}

            {currentTab === 'timeline' && (
              <TimelinePlanningView project={project} onNavigateToExport={() => setCurrentTab('export')} />
            )}

            {currentTab === 'review' && (
              <ReviewDecisionsView
                projectId={projectId}
                suggestions={suggestions}
                loading={suggestionsLoading}
                loadError={suggestionsError}
                feedbackError={reviewError}
                notice={reviewNotice}
                onReload={reloadSuggestions}
                onClearFeedback={clearReviewFeedback}
                onDecide={handleDecision}
                onBatchDecide={handleBatchDecide}
                onModify={handleModify}
                onApplyZoom={handleApplyZoom}
                onUndo={handleUndoDecision}
                onRedo={handleRedoDecision}
                onNavigateToTimeline={() => setCurrentTab('timeline')}
              />
            )}

            {currentTab === 'settings' && (
              <SettingsIntegrationsView editorCaps={editorCaps} onRefreshCaps={handleRefreshCaps} projectId={projectId} />
            )}

            {currentTab === 'narrative' && (
              <MontageOverviewView project={project} onNavigateToTimeline={() => setCurrentTab('timeline')} />
            )}

            {currentTab === 'export' && (
              <ResolveHubView project={project} editorCaps={editorCaps} onRefreshCaps={handleRefreshCaps} />
            )}

            {(currentTab === 'audio' ||
              currentTab === 'animations') && (
              <div className="flex-1 flex flex-col items-center justify-center p-8 text-center bg-[#070d18]">
                <div className="w-16 h-16 rounded-2xl bg-[#0a1222] border border-slate-800 flex items-center justify-center text-cyan-400 mb-4 shadow-xl">
                  <span className="font-bold text-lg uppercase tracking-wider">{currentTab.substring(0, 3)}</span>
                </div>
                <h2 className="text-xl font-bold text-white mb-2 capitalize">
                  Módulo: {currentTab}
                </h2>
                <p className="text-xs text-slate-400 max-w-md mb-6 leading-relaxed">
                  Este módulo ainda não está disponível nesta versão.
                  Use a Revisão de Cortes ou a Verificação do Ambiente enquanto isso.
                </p>
                <div className="flex items-center gap-3">
                  <button
                    onClick={() => setCurrentTab('overview')}
                    className="px-4 py-2 rounded-xl bg-slate-800 hover:bg-slate-700 text-xs font-semibold text-slate-200 transition-colors"
                  >
                    Voltar para Visão Geral
                  </button>
                  <button
                    onClick={() => setCurrentTab('review')}
                    className="px-4 py-2 rounded-xl bg-gradient-to-r from-cyan-500 to-blue-600 text-xs font-bold text-white shadow-md shadow-cyan-950"
                  >
                    Ir para Revisão de Cortes
                  </button>
                </div>
              </div>
            )}
          </ErrorBoundary>
        </main>
      </div>

      {/* Modal 1: Criar Novo Projeto */}
      {modalMode === 'create' && (
        <div className="fixed inset-0 z-50 bg-black/80 backdrop-blur-sm flex items-center justify-center p-4">
          <div className="w-full max-w-lg bg-[#0a1222] border border-cyan-500/40 rounded-3xl p-6 shadow-2xl space-y-5 relative">
            <div className="flex items-center justify-between border-b border-slate-800 pb-3">
              <div className="flex items-center gap-2.5">
                <div className="w-8 h-8 rounded-xl bg-gradient-to-tr from-cyan-500 to-blue-600 flex items-center justify-center text-white shadow-md shadow-cyan-950">
                  <Plus className="w-4 h-4 stroke-[2.5]" />
                </div>
                <div>
                  <h3 className="text-base font-bold text-white">
                    Criar Novo Projeto
                  </h3>
                  <p className="text-[11px] text-cyan-400 font-medium">
                    Estrutura canônica inteligente do TravelCut Studio
                  </p>
                </div>
              </div>
              <button
                onClick={() => {
                  setModalMode(null);
                  setProjectError(null);
                }}
                className="text-slate-400 hover:text-white text-xs font-semibold p-1.5 rounded-lg hover:bg-slate-800/60"
              >
                ✕
              </button>
            </div>

            <p className="text-xs text-slate-300 leading-relaxed">
              Defina o nome do seu projeto e escolha a pasta onde os arquivos brutos, áudios e timelines serão organizados.
            </p>

            {projectError && (
              <div className="p-3.5 rounded-xl bg-rose-500/10 border border-rose-500/30 text-rose-300 text-xs flex items-start gap-2.5">
                <AlertCircle className="w-4 h-4 text-rose-400 shrink-0 mt-0.5" />
                <div className="flex-1 leading-relaxed">
                  <span className="font-bold block mb-0.5">Não foi possível criar o projeto:</span>
                  {projectError}
                </div>
              </div>
            )}

            {/* Campo 1: Nome do Projeto */}
            <div className="space-y-1.5">
              <label className="text-[11px] font-bold text-slate-300 uppercase tracking-wider block">
                Nome do Projeto
              </label>
              <input
                type="text"
                value={projectNameInput}
                onChange={(e) => {
                  setProjectNameInput(e.target.value);
                  setProjectError(null);
                }}
                className="w-full px-3.5 py-2.5 rounded-xl bg-[#070d18] border border-slate-700 text-xs text-white focus:outline-none focus:border-cyan-400 font-sans"
                placeholder="Ex: Viagem Santiago 2026"
              />
            </div>

            {/* Campo 2: Pasta de Destino com Procurar... */}
            <div className="space-y-1.5">
              <label className="text-[11px] font-bold text-slate-300 uppercase tracking-wider block">
                Pasta de Destino
              </label>
              <div className="flex items-center gap-2">
                <input
                  type="text"
                  value={createBasePathInput}
                  onChange={(e) => {
                    setCreateBasePathInput(e.target.value);
                    setProjectError(null);
                  }}
                  className="flex-1 px-3.5 py-2.5 rounded-xl bg-[#070d18] border border-slate-700 text-xs text-white focus:outline-none focus:border-cyan-400 font-mono truncate"
                  placeholder="Ex: D:\Videos\Viagem-2026"
                />
                <button
                  type="button"
                  onClick={() => handleSelectFolder('create')}
                  className="px-3.5 py-2.5 rounded-xl bg-cyan-950/60 hover:bg-cyan-900/60 border border-cyan-500/40 text-xs font-semibold text-cyan-400 hover:text-cyan-300 transition-colors flex items-center gap-1.5 shrink-0 shadow-sm"
                  title="Selecionar pasta no Windows"
                >
                  <FolderOpen className="w-4 h-4" />
                  Procurar...
                </button>
              </div>
              <div className="text-[11px] text-slate-400 pt-1 font-mono truncate">
                Caminho final:{' '}
                <span className="text-cyan-400 font-medium">
                  {createBasePathInput.replace(/[\\/]+$/, '')}\
                  {projectNameInput.trim() || 'Meu-Projeto'}
                </span>
              </div>
            </div>

            {/* Opção: Criar pastas canônicas */}
            <label className="flex items-center gap-2 text-xs text-slate-300 cursor-pointer select-none bg-slate-900/40 p-3 rounded-xl border border-slate-800">
              <input
                type="checkbox"
                checked={createFolders}
                onChange={(e) => setCreateFolders(e.target.checked)}
                className="rounded border-slate-700 bg-slate-800 text-cyan-500 focus:ring-cyan-500/20"
              />
              <span>
                Criar pastas canônicas (<code className="text-cyan-400">1-Bruto</code>, <code className="text-cyan-400">2-Assets</code>, <code className="text-cyan-400">3-Trabalho</code>, <code className="text-cyan-400">4-Final</code>)
              </span>
            </label>

            {/* Ações */}
            <div className="flex items-center justify-end gap-2.5 pt-2 border-t border-slate-800/80">
              <button
                onClick={() => {
                  setModalMode(null);
                  setProjectError(null);
                }}
                disabled={isProjectLoading}
                className="px-4 py-2 rounded-xl bg-slate-800 hover:bg-slate-700 text-xs font-semibold text-slate-300 transition-colors disabled:opacity-50"
              >
                Cancelar
              </button>
              <button
                onClick={() => handleCreateProject(projectNameInput, createBasePathInput)}
                disabled={isProjectLoading}
                className="px-5 py-2.5 rounded-xl bg-gradient-to-r from-cyan-500 to-blue-600 hover:from-cyan-400 hover:to-blue-500 text-xs font-bold text-white transition-all shadow-md shadow-cyan-950 flex items-center gap-2 disabled:opacity-50"
              >
                {isProjectLoading ? (
                  <>
                    <Loader2 className="w-3.5 h-3.5 animate-spin" />
                    Criando Projeto...
                  </>
                ) : (
                  <>
                    <Plus className="w-3.5 h-3.5" />
                    Criar Projeto
                  </>
                )}
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Modal 2: Abrir Projeto Existente */}
      {modalMode === 'open' && (
        <div className="fixed inset-0 z-50 bg-black/80 backdrop-blur-sm flex items-center justify-center p-4">
          <div className="w-full max-w-lg bg-[#0a1222] border border-slate-800 rounded-3xl p-6 shadow-2xl space-y-5 relative">
            <div className="flex items-center justify-between border-b border-slate-800 pb-3">
              <div className="flex items-center gap-2.5">
                <div className="w-8 h-8 rounded-xl bg-emerald-950/80 border border-emerald-500/40 flex items-center justify-center text-emerald-400 shadow-md">
                  <FolderOpen className="w-4 h-4" />
                </div>
                <div>
                  <h3 className="text-base font-bold text-white">
                    Abrir Projeto Existente
                  </h3>
                  <p className="text-[11px] text-emerald-400 font-medium">
                    Carregar mídias, transcrições e estado da edição
                  </p>
                </div>
              </div>
              <button
                onClick={() => {
                  setModalMode(null);
                  setProjectError(null);
                }}
                className="text-slate-400 hover:text-white text-xs font-semibold p-1.5 rounded-lg hover:bg-slate-800/60"
              >
                ✕
              </button>
            </div>

            <p className="text-xs text-slate-300 leading-relaxed">
              Selecione a pasta raiz do projeto já criado no TravelCut Studio ou que contenha suas mídias de viagem.
            </p>

            {projectError && (
              <div className="p-3.5 rounded-xl bg-rose-500/10 border border-rose-500/30 text-rose-300 text-xs flex items-start gap-2.5">
                <AlertCircle className="w-4 h-4 text-rose-400 shrink-0 mt-0.5" />
                <div className="flex-1 leading-relaxed">
                  <span className="font-bold block mb-0.5">Não foi possível abrir o projeto:</span>
                  {projectError}
                </div>
              </div>
            )}

            {/* Campo: Pasta do Projeto com Procurar... */}
            <div className="space-y-1.5">
              <label className="text-[11px] font-bold text-slate-300 uppercase tracking-wider block">
                Pasta do Projeto
              </label>
              <div className="flex items-center gap-2">
                <input
                  type="text"
                  value={projectPathInput}
                  onChange={(e) => {
                    setProjectPathInput(e.target.value);
                    setProjectError(null);
                    setMissingFoldersPrompt(null);
                  }}
                  className="flex-1 px-3.5 py-2.5 rounded-xl bg-[#070d18] border border-slate-700 text-xs text-white focus:outline-none focus:border-cyan-400 font-mono truncate"
                  placeholder="Ex: D:\Videos\Chapada_2026"
                />
                <button
                  type="button"
                  onClick={() => handleSelectFolder('open')}
                  className="px-3.5 py-2.5 rounded-xl bg-slate-800 hover:bg-slate-700 border border-slate-700 text-xs font-semibold text-cyan-400 hover:text-cyan-300 transition-colors flex items-center gap-1.5 shrink-0 shadow-sm"
                  title="Selecionar pasta no Windows"
                >
                  <FolderOpen className="w-4 h-4" />
                  Procurar...
                </button>
              </div>
            </div>

            {/* Confirmação explícita antes de criar pastas canônicas ausentes */}
            {missingFoldersPrompt && (
              <div
                role="alertdialog"
                aria-labelledby="missing-folders-title"
                className="p-3.5 rounded-xl bg-amber-950/40 border border-amber-500/40 text-amber-100 text-xs space-y-3"
              >
                <div id="missing-folders-title" className="font-bold flex items-center gap-2">
                  <AlertCircle className="w-4 h-4 text-amber-300 shrink-0" />
                  Faltam pastas canônicas neste projeto
                </div>
                <p className="leading-relaxed">{missingFoldersPrompt.message}</p>
                <p className="leading-relaxed text-amber-200/90">{CREATE_MISSING_FOLDERS_QUESTION}</p>
                <div className="flex items-center justify-end gap-2">
                  <button
                    type="button"
                    onClick={() => setMissingFoldersPrompt(null)}
                    disabled={isProjectLoading}
                    className="px-3 py-1.5 rounded-lg bg-slate-800 hover:bg-slate-700 text-slate-200 font-semibold disabled:opacity-50"
                  >
                    Cancelar
                  </button>
                  <button
                    type="button"
                    onClick={() => handleOpenProject(missingFoldersPrompt.path, true)}
                    disabled={isProjectLoading}
                    className="px-3 py-1.5 rounded-lg bg-amber-600 hover:bg-amber-500 text-white font-bold disabled:opacity-50"
                  >
                    Criar pastas e abrir
                  </button>
                </div>
              </div>
            )}

            {/* Projetos recentes (do banco local) */}
            {recentProjects.length > 0 && (
              <div className="space-y-1.5">
                <label className="text-[11px] font-bold text-slate-400 uppercase tracking-wider block">
                  Projetos Recentes
                </label>
                <div className="space-y-1.5 max-h-40 overflow-y-auto">
                  {recentProjects.map((p) => (
                    <button
                      key={p.projectId}
                      type="button"
                      onClick={() => setProjectPathInput(p.rootPath)}
                      className="w-full text-left p-2.5 rounded-xl bg-slate-900/60 hover:bg-slate-800/80 border border-slate-800 text-xs text-slate-300 flex items-center justify-between transition-colors group"
                    >
                      <div className="flex items-center gap-2 truncate">
                        <FolderOpen className="w-3.5 h-3.5 text-cyan-400 shrink-0" />
                        <span className="font-semibold text-white group-hover:text-cyan-400 transition-colors">
                          {p.name}
                        </span>
                        <span className="text-[11px] text-slate-400 truncate">({p.rootPath})</span>
                      </div>
                      <span className="text-[10px] text-cyan-400 font-bold shrink-0">Selecionar</span>
                    </button>
                  ))}
                </div>
              </div>
            )}

            {/* Opção: Garantir pastas */}
            <label className="flex items-center gap-2 text-xs text-slate-300 cursor-pointer select-none bg-slate-900/40 p-3 rounded-xl border border-slate-800">
              <input
                type="checkbox"
                checked={openCreateMissing}
                onChange={(e) => setOpenCreateMissing(e.target.checked)}
                className="rounded border-slate-700 bg-slate-800 text-cyan-500 focus:ring-cyan-500/20"
              />
              <span>
                Criar pastas canônicas ausentes sem perguntar (<code className="text-cyan-400">1-Bruto</code>, <code className="text-cyan-400">2-Assets</code>, etc.). Desmarcado, o app pede confirmação.
              </span>
            </label>

            {/* Ações */}
            <div className="flex items-center justify-end gap-2.5 pt-2 border-t border-slate-800/80">
              <button
                onClick={() => {
                  setModalMode(null);
                  setProjectError(null);
                }}
                disabled={isProjectLoading}
                className="px-4 py-2 rounded-xl bg-slate-800 hover:bg-slate-700 text-xs font-semibold text-slate-300 transition-colors disabled:opacity-50"
              >
                Cancelar
              </button>
              <button
                onClick={() => handleOpenProject(projectPathInput)}
                disabled={isProjectLoading}
                className="px-5 py-2.5 rounded-xl bg-slate-800 hover:bg-slate-700 border border-slate-700 text-xs font-bold text-white transition-all shadow-md flex items-center gap-2 disabled:opacity-50 hover:border-cyan-500/50"
              >
                {isProjectLoading ? (
                  <>
                    <Loader2 className="w-3.5 h-3.5 animate-spin" />
                    Abrindo...
                  </>
                ) : (
                  <>
                    <FolderOpen className="w-3.5 h-3.5 text-cyan-400" />
                    Abrir Projeto
                  </>
                )}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};
