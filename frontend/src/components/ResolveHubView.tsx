import React, { useState } from 'react';
import { ClipboardList, Send } from 'lucide-react';
import type { EditorCapabilities, ProjectSummary } from '../types/ipc';
import { DemoBadge, StateCard } from './plan/PlanPrimitives';
import { ApplicationHistory } from './editor/ApplicationHistory';
import { ApplyPreviewPanel } from './editor/ApplyPreviewPanel';
import { ApplyResultPanel } from './editor/ApplyResultPanel';
import { CapabilityTable } from './editor/CapabilityTable';
import { EnvironmentCard } from './editor/EnvironmentCard';
import { EditorErrorNotice, primaryButtonClass } from './editor/EditorPrimitives';
import { describeEditorError, isEditorSimulated, type EditorErrorInfo } from './editor/editorModel';
import { useEditorApply } from './editor/useEditorApply';

interface ResolveHubViewProps {
  project: ProjectSummary | null;
  editorCaps: EditorCapabilities | null;
  onRefreshCaps: () => Promise<void>;
}

/**
 * Tela "Enviar ao DaVinci". Cria um projeto NOVO TravelCut_* no DaVinci a partir do plano, após simulação seca.
 * Sem regra de negócio aqui: estados e textos vêm do host (editor/1); o simulador aparece sempre marcado.
 */
export const ResolveHubView: React.FC<ResolveHubViewProps> = ({ project, editorCaps, onRefreshCaps }) => {
  const projectId = project?.projectId ?? null;
  const ctrl = useEditorApply(projectId);
  const [refreshing, setRefreshing] = useState(false);
  const [refreshError, setRefreshError] = useState<EditorErrorInfo | null>(null);
  const simulated = isEditorSimulated(editorCaps);

  const handleRefresh = async () => {
    setRefreshing(true);
    setRefreshError(null);
    try {
      await onRefreshCaps();
    } catch (err) {
      setRefreshError(describeEditorError(err));
    } finally {
      setRefreshing(false);
    }
  };

  return (
    <div className="flex-1 min-h-0 overflow-y-auto bg-[#070d18] text-slate-100 font-sans">
      <div className="max-w-6xl mx-auto p-6 space-y-5">
        {/* Cabeçalho */}
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div className="min-w-0">
            <div className="flex items-center gap-2">
              <Send className="w-5 h-5 text-cyan-400" aria-hidden="true" />
              <h2 className="text-base font-bold text-white">Enviar ao DaVinci</h2>
              <DemoBadge />
            </div>
            <p className="text-xs text-slate-400 mt-1 leading-relaxed max-w-3xl">
              Cria um projeto novo <span className="font-mono">TravelCut_*</span> no DaVinci Resolve a partir do plano.
              Projetos existentes nunca são abertos nem alterados pelo TravelCut.
            </p>
          </div>
        </div>

        {simulated && (
          <div
            role="status"
            className="px-3 py-2 rounded-xl bg-amber-950/50 border border-amber-500/40 text-xs font-semibold text-amber-200"
          >
            Simulador ativo: nada é enviado ao DaVinci Resolve. Os resultados abaixo são simulados.
          </div>
        )}

        {/* Ambiente e capacidades */}
        <EnvironmentCard
          caps={editorCaps}
          refreshing={refreshing}
          refreshError={refreshError}
          onRefresh={handleRefresh}
        />
        {editorCaps && <CapabilityTable operations={editorCaps.operations} />}

        {/* Envio do projeto ativo */}
        {!project ? (
          <StateCard
            tone="blocked"
            title="Nenhum projeto ativo"
            description="Abra ou crie um projeto e gere o plano na Tela 03 para preparar o envio ao DaVinci."
          />
        ) : (
          <section aria-label="Envio do projeto" className="space-y-4">
            <div className="flex flex-wrap items-center justify-between gap-3">
              <div className="min-w-0">
                <h3 className="text-sm font-bold text-white">Projeto: {project.name}</h3>
                <p className="text-[11px] text-slate-400">
                  Prepare o envio para ver o que seria criado. A preparação não envia nada ao DaVinci.
                </p>
              </div>
              <button
                type="button"
                onClick={() => void ctrl.prepare()}
                disabled={ctrl.preparing || ctrl.sending}
                className={`${primaryButtonClass} flex items-center gap-1.5`}
              >
                <ClipboardList className="w-3.5 h-3.5" aria-hidden="true" />
                <span>{ctrl.preparing ? 'Preparando…' : 'Preparar envio (simulação seca)'}</span>
              </button>
            </div>

            {ctrl.prepareError && (
              <EditorErrorNotice error={ctrl.prepareError} title="Não foi possível preparar o envio" />
            )}

            {!ctrl.preview && !ctrl.preparing && !ctrl.prepareError && (
              <StateCard
                tone="empty"
                title="Nenhuma preparação feita"
                description="A simulação seca lista projeto, timeline, clipes e bloqueios antes de qualquer envio."
              />
            )}

            {ctrl.preparing && !ctrl.preview && (
              <StateCard tone="loading" title="Preparando a simulação seca…" description="Lendo o plano e as capacidades do editor." />
            )}

            {ctrl.preview && (
              <ApplyPreviewPanel
                preview={ctrl.preview}
                confirmed={ctrl.confirmed}
                onConfirmChange={ctrl.setConfirmed}
                sending={ctrl.sending}
                onSend={() => void ctrl.send()}
              />
            )}

            {ctrl.sending && (
              <p role="status" className="text-xs text-cyan-200">
                Enviando ao editor… aguarde a resposta do host antes de tentar de novo.
              </p>
            )}

            {ctrl.sendError && <EditorErrorNotice error={ctrl.sendError} title="O envio falhou" />}

            {ctrl.result && <ApplyResultPanel result={ctrl.result} />}
          </section>
        )}

        {/* Histórico do projeto */}
        {project && (
          <ApplicationHistory
            history={ctrl.history}
            status={ctrl.historyStatus}
            error={ctrl.historyError}
            onReload={() => void ctrl.reloadHistory()}
          />
        )}
      </div>
    </div>
  );
};
