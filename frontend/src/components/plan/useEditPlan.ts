import { useCallback, useEffect, useRef, useState } from 'react';
import { IpcError, ipc } from '../../services/ipcBridge';
import type { EditPlanDto, EditorialProfileDto, PlanRevisionInfoDto, PlanValidationDto } from '../../types/ipc';
import { displayedValidation } from './planModel';

/**
 * Estado do Plano editorial (F06) para um projeto.
 * - 'no_project': nenhum projeto aberto (bloqueado).
 * - 'loading': carregando plan.get / plan.list.
 * - 'error': falha de carregamento (mensagem do host).
 * - 'empty': projeto sem plano (plan.get retornou null).
 * - 'ready': plano exibido (revisão selecionada ou a mais recente).
 */
export type PlanViewStatus = 'no_project' | 'loading' | 'error' | 'empty' | 'ready';
export type PlanBusyAction = 'generate' | 'revert' | 'validate' | 'select';

export interface EditPlanState {
  status: PlanViewStatus;
  /** Revisão exibida. */
  plan: EditPlanDto | null;
  revisions: PlanRevisionInfoDto[];
  latestRevision: number | null;
  /** Validação da revisão exibida (inclui revalidação manual da mesma revisão). */
  validation: PlanValidationDto | null;
  profile: EditorialProfileDto | null;
  loadError: string | null;
  actionError: string | null;
  busy: PlanBusyAction | null;
  selectRevision: (revision: number) => void;
  generate: () => void;
  revert: (toRevision: number) => void;
  validate: () => void;
  reload: () => void;
  dismissActionError: () => void;
}

/** Mensagens PT-BR para códigos estáveis do host; demais erros mostram a mensagem recebida. */
export const planErrorMessage = (err: unknown): string => {
  if (err instanceof IpcError) {
    switch (err.code) {
      case 'NO_MEDIA_TO_PLAN':
        return 'Não há mídia de vídeo analisada para gerar o plano. Importe e analise as mídias antes.';
      case 'PROJECT_NOT_FOUND':
        return 'Projeto não encontrado no host. Reabra o projeto e tente novamente.';
      case 'PLAN_NOT_FOUND':
        return 'Esta revisão não existe mais no histórico do plano.';
      case 'PLAN_FILE_CORRUPT':
        return 'O arquivo desta revisão está corrompido ou em formato antigo. Gere uma nova revisão.';
      case 'PLAN_TAMPERED':
        return 'O arquivo desta revisão foi alterado fora do TravelCut (fingerprint diferente). Não é possível revertê-la.';
      case 'PLAN_REVISION_CONFLICT':
        return 'Outra geração do plano gravou esta revisão ao mesmo tempo. Recarregue e tente de novo.';
      case 'PLAN_FILE_MISSING':
        return 'O arquivo desta revisão sumiu de 3-Trabalho/plans. Ele não é reconstruído automaticamente; gere uma nova revisão.';
      default:
        break;
    }
  }
  return err instanceof Error ? err.message : String(err);
};

interface LoadedData {
  projectId: string;
  latest: EditPlanDto | null;
  revisions: PlanRevisionInfoDto[];
  profile: EditorialProfileDto | null;
}

/** refreshKey (ex.: project.lastModifiedUtc) refaz plan.get/plan.list quando o resumo do projeto muda. */
export function useEditPlan(projectId: string | null, refreshKey: string | null = null): EditPlanState {
  const [loaded, setLoaded] = useState<LoadedData | null>(null);
  const [loadError, setLoadError] = useState<{ projectId: string; message: string } | null>(null);
  const [viewedPlan, setViewedPlan] = useState<EditPlanDto | null>(null);
  const [revisions, setRevisions] = useState<PlanRevisionInfoDto[]>([]);
  const [actionError, setActionError] = useState<string | null>(null);
  const [busy, setBusy] = useState<PlanBusyAction | null>(null);
  const [validationOverride, setValidationOverride] = useState<{ revision: number; validation: PlanValidationDto } | null>(null);
  const [reloadKey, setReloadKey] = useState(0);

  // Respostas de um projeto anterior não podem sobrescrever o estado atual.
  const projectRef = useRef<string | null>(projectId);
  useEffect(() => {
    projectRef.current = projectId;
  }, [projectId]);

  useEffect(() => {
    setActionError(null);
    setValidationOverride(null);
    setLoaded(null);
    setLoadError(null);
    setViewedPlan(null);
    setRevisions([]);
    if (!projectId) return;

    let cancelled = false;
    (async () => {
      try {
        const [latest, list, profile] = await Promise.all([
          ipc.getEditPlan(projectId, null),
          ipc.listPlanRevisions(projectId),
          ipc.getEditorialProfile().catch(() => null)
        ]);
        if (cancelled) return;
        setLoaded({ projectId, latest, revisions: list, profile });
        setViewedPlan(latest);
        setRevisions(list);
      } catch (err) {
        if (cancelled) return;
        setLoadError({ projectId, message: planErrorMessage(err) });
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [projectId, refreshKey, reloadKey]);

  const isCurrent = useCallback((pid: string) => projectRef.current === pid, []);

  const selectRevision = useCallback(
    (revision: number) => {
      if (!projectId || busy) return;
      const pid = projectId;
      setBusy('select');
      setActionError(null);
      ipc
        .getEditPlan(pid, revision)
        .then((plan) => {
          if (!isCurrent(pid)) return;
          if (plan) setViewedPlan(plan);
          else setActionError(`A revisão R${revision} não foi encontrada.`);
        })
        .catch((err) => {
          if (isCurrent(pid)) setActionError(planErrorMessage(err));
        })
        .finally(() => {
          if (isCurrent(pid)) setBusy(null);
        });
    },
    [projectId, busy, isCurrent]
  );

  const generate = useCallback(() => {
    if (!projectId || busy) return;
    const pid = projectId;
    setBusy('generate');
    setActionError(null);
    ipc
      .generateEditPlan(pid)
      .then(async (created) => {
        const list = await ipc.listPlanRevisions(pid);
        if (!isCurrent(pid)) return;
        setViewedPlan(created);
        setRevisions(list);
        setValidationOverride(null);
      })
      .catch((err) => {
        if (isCurrent(pid)) setActionError(planErrorMessage(err));
      })
      .finally(() => {
        if (isCurrent(pid)) setBusy(null);
      });
  }, [projectId, busy, isCurrent]);

  const revert = useCallback(
    (toRevision: number) => {
      if (!projectId || busy) return;
      const pid = projectId;
      setBusy('revert');
      setActionError(null);
      ipc
        .revertPlan(pid, toRevision)
        .then(async (created) => {
          const list = await ipc.listPlanRevisions(pid);
          if (!isCurrent(pid)) return;
          setViewedPlan(created);
          setRevisions(list);
          setValidationOverride(null);
        })
        .catch((err) => {
          if (isCurrent(pid)) setActionError(planErrorMessage(err));
        })
        .finally(() => {
          if (isCurrent(pid)) setBusy(null);
        });
    },
    [projectId, busy, isCurrent]
  );

  const validate = useCallback(() => {
    if (!projectId || !viewedPlan || busy) return;
    const pid = projectId;
    const revision = viewedPlan.revision;
    setBusy('validate');
    setActionError(null);
    ipc
      .validatePlan(pid, revision)
      .then((validation) => {
        if (isCurrent(pid)) setValidationOverride({ revision, validation });
      })
      .catch((err) => {
        if (isCurrent(pid)) setActionError(planErrorMessage(err));
      })
      .finally(() => {
        if (isCurrent(pid)) setBusy(null);
      });
  }, [projectId, viewedPlan, busy, isCurrent]);

  const reload = useCallback(() => setReloadKey((k) => k + 1), []);
  const dismissActionError = useCallback(() => setActionError(null), []);

  const isLoadedForProject = !!projectId && loaded?.projectId === projectId;
  const failed = !!projectId && loadError?.projectId === projectId ? loadError : null;

  let status: PlanViewStatus;
  if (!projectId) status = 'no_project';
  else if (failed) status = 'error';
  else if (!isLoadedForProject) status = 'loading';
  else status = viewedPlan ? 'ready' : 'empty';

  const latestRevision = revisions.length > 0
    ? Math.max(...revisions.map((r) => r.revision))
    : loaded?.latest?.revision ?? null;

  return {
    status,
    plan: status === 'ready' ? viewedPlan : null,
    revisions,
    latestRevision,
    validation: viewedPlan ? displayedValidation(viewedPlan, validationOverride) : null,
    profile: isLoadedForProject ? loaded?.profile ?? null : null,
    loadError: failed ? failed.message : null,
    actionError,
    busy,
    selectRevision,
    generate,
    revert,
    validate,
    reload,
    dismissActionError
  };
}
