import { useEffect, useSyncExternalStore } from 'react';
import {
  integrationStatusStore,
  slotStaleReason,
  type IntegrationStatusState
} from '../../services/integrationStatusStore';
import type { AnalysisEnvironmentDto, MotionCapabilitiesDto } from '../../types/ipc';

export interface IntegrationStatusSource {
  /** Última leitura válida; continua aqui mesmo se a consulta seguinte falhar. */
  analysisEnv: AnalysisEnvironmentDto | null;
  /** Falha de consulta sem nenhuma leitura válida (diferente de "ainda não consultado"). */
  analysisError: string | null;
  analysisStaleReason: string | null;
  analysisCheckedAtMs: number | null;
  motionCaps: MotionCapabilitiesDto | null;
  motionError: string | null;
  motionStaleReason: string | null;
  motionCheckedAtMs: number | null;
  loading: boolean;
  /** Reconsulta forçada no host; todas as telas recebem o resultado. */
  refresh: () => Promise<void>;
}

/** Projeção do estado autoritativo para as telas. Falha só aparece como erro quando não há leitura válida. */
export function toIntegrationSource(
  state: IntegrationStatusState,
  nowMs: number,
  refresh: () => Promise<void>
): IntegrationStatusSource {
  const { analysis, motion } = state;
  return {
    analysisEnv: analysis.value,
    analysisError: analysis.value ? null : analysis.error,
    analysisStaleReason: slotStaleReason(analysis, nowMs),
    analysisCheckedAtMs: analysis.checkedAtMs,
    motionCaps: motion.value,
    motionError: motion.value ? null : motion.error,
    motionStaleReason: slotStaleReason(motion, nowMs),
    motionCheckedAtMs: motion.checkedAtMs,
    loading: state.loading,
    refresh
  };
}

export function useIntegrationStatus(): IntegrationStatusSource {
  const state = useSyncExternalStore(
    integrationStatusStore.subscribe,
    integrationStatusStore.getState,
    integrationStatusStore.getState
  );

  useEffect(() => {
    void integrationStatusStore.ensureFresh();
  }, []);

  return toIntegrationSource(state, Date.now(), integrationStatusStore.refresh);
}
