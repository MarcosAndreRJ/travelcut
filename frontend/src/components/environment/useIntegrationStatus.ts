import { useCallback, useEffect, useRef, useState } from 'react';
import { ipc } from '../../services/ipcBridge';
import type { AnalysisEnvironmentDto, MotionCapabilitiesDto } from '../../types/ipc';
import { errorMessage } from '../analysis/analysisFormat';

export interface IntegrationStatusSource {
  analysisEnv: AnalysisEnvironmentDto | null;
  analysisError: string | null;
  motionCaps: MotionCapabilitiesDto | null;
  motionError: string | null;
  loading: boolean;
  /** Reconsulta analysis.environment e motion.capabilities no host. */
  refresh: () => Promise<void>;
}

/**
 * Consulta as capacidades reais de análise e animação. Falhas viram mensagem visível (não sucesso);
 * antes da primeira resposta os dados ficam null e as linhas aparecem como "Não verificado".
 */
export function useIntegrationStatus(): IntegrationStatusSource {
  const [analysisEnv, setAnalysisEnv] = useState<AnalysisEnvironmentDto | null>(null);
  const [analysisError, setAnalysisError] = useState<string | null>(null);
  const [motionCaps, setMotionCaps] = useState<MotionCapabilitiesDto | null>(null);
  const [motionError, setMotionError] = useState<string | null>(null);
  const [loading, setLoading] = useState<boolean>(false);
  const mounted = useRef<boolean>(true);

  useEffect(() => {
    mounted.current = true;
    return () => {
      mounted.current = false;
    };
  }, []);

  const refresh = useCallback(async (): Promise<void> => {
    setLoading(true);
    const [env, motion] = await Promise.allSettled([ipc.getAnalysisEnvironment(), ipc.getMotionCapabilities()]);
    if (!mounted.current) return;

    if (env.status === 'fulfilled') {
      setAnalysisEnv(env.value);
      setAnalysisError(null);
    } else {
      console.error('Falha ao consultar ambiente de análise:', env.reason);
      setAnalysisEnv(null);
      setAnalysisError(errorMessage(env.reason));
    }

    if (motion.status === 'fulfilled') {
      setMotionCaps(motion.value);
      setMotionError(null);
    } else {
      console.error('Falha ao consultar capacidades de animação:', motion.reason);
      setMotionCaps(null);
      setMotionError(errorMessage(motion.reason));
    }
    setLoading(false);
  }, []);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  return { analysisEnv, analysisError, motionCaps, motionError, loading, refresh };
}
