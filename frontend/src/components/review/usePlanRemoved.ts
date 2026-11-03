import { useEffect, useState } from 'react';
import { ipc } from '../../services/ipcBridge';
import type { PlanRemovedState } from './reviewModel';

interface Loaded {
  projectId: string;
  value: PlanRemovedState;
}

/**
 * Tempo removido da revisão mais recente do plano (plan.get via ipc.getEditPlan).
 * Recarrega quando refreshKey muda (cada decisão pode regenerar o plano no host).
 * Resultado de um projeto anterior nunca é exibido para o projeto atual.
 */
export function usePlanRemoved(projectId: string | null, refreshKey: unknown): PlanRemovedState {
  const [loaded, setLoaded] = useState<Loaded | null>(null);

  useEffect(() => {
    if (!projectId) return undefined;
    let active = true;
    ipc
      .getEditPlan(projectId, null)
      .then((plan) => {
        if (!active) return;
        const value: PlanRemovedState = plan
          ? { status: 'ready', revision: plan.revision, removedSourceUs: plan.summary.removedSourceUs }
          : { status: 'none' };
        setLoaded({ projectId, value });
      })
      .catch(() => {
        if (active) setLoaded({ projectId, value: { status: 'error' } });
      });
    return () => {
      active = false;
    };
  }, [projectId, refreshKey]);

  if (!projectId) return { status: 'none' };
  if (!loaded || loaded.projectId !== projectId) return { status: 'loading' };
  return loaded.value;
}
