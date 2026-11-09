import { useEffect, useState } from 'react';
import { ipc } from '../../services/ipcBridge';
import type { EditPlanDto } from '../../types/ipc';
import type { PlanRemovedState } from './reviewModel';

interface Loaded {
  projectId: string;
  plan: EditPlanDto | null;
  error: boolean;
}

export interface PlanSnapshot {
  removed: PlanRemovedState;
  /** Plano da revisão mais recente (null sem plano, carregando ou com erro). */
  plan: EditPlanDto | null;
}

/**
 * Plano da revisão mais recente (plan.get via ipc.getEditPlan) e tempo removido.
 * Recarrega quando refreshKey muda (cada decisão pode regenerar o plano no host).
 * Resultado de um projeto anterior nunca é exibido para o projeto atual.
 */
export function usePlanSnapshot(projectId: string | null, refreshKey: unknown): PlanSnapshot {
  const [loaded, setLoaded] = useState<Loaded | null>(null);

  useEffect(() => {
    if (!projectId) return undefined;
    let active = true;
    ipc
      .getEditPlan(projectId, null)
      .then((plan) => {
        if (active) setLoaded({ projectId, plan, error: false });
      })
      .catch(() => {
        if (active) setLoaded({ projectId, plan: null, error: true });
      });
    return () => {
      active = false;
    };
  }, [projectId, refreshKey]);

  if (!projectId) return { removed: { status: 'none' }, plan: null };
  if (!loaded || loaded.projectId !== projectId) return { removed: { status: 'loading' }, plan: null };
  if (loaded.error) return { removed: { status: 'error' }, plan: null };
  const plan = loaded.plan;
  return {
    removed: plan
      ? { status: 'ready', revision: plan.revision, removedSourceUs: plan.summary.removedSourceUs }
      : { status: 'none' },
    plan
  };
}

/** Só o tempo removido (mantido por compatibilidade). */
export function usePlanRemoved(projectId: string | null, refreshKey: unknown): PlanRemovedState {
  return usePlanSnapshot(projectId, refreshKey).removed;
}
