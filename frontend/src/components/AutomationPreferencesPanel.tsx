import React, { useEffect, useState } from 'react';
import { ipc } from '../services/ipcBridge';
import type { AutomationProfile } from '../types/ipc';

export const AUTOMATION_PROFILE_OPTIONS: { value: AutomationProfile; label: string; description: string }[] = [
  {
    value: 'conservative',
    label: 'Conservador',
    description: 'Aplica automaticamente só os cortes de alta confiança e envia o resto para revisão.'
  },
  {
    value: 'balanced',
    label: 'Equilibrado',
    description: 'Aplica cortes de confiança média e alta e deixa os casos ambíguos para revisão.'
  },
  {
    value: 'aggressive',
    label: 'Agressivo',
    description: 'Aplica a maioria dos cortes sugeridos e revisa apenas os casos mais incertos.'
  }
];

const messageOf = (error: unknown): string => (error instanceof Error ? error.message : 'Erro desconhecido no host.');

interface AutomationPreferencesPanelProps {
  projectId: string;
}

/**
 * Preferências de automação do projeto (perfil + AutoProcess).
 * Trocar o perfil deixa a revisão do plano desatualizada (PLAN_OUTDATED) até novo plan.generate.
 */
export const AutomationPreferencesPanel: React.FC<AutomationPreferencesPanelProps> = ({ projectId }) => {
  const [profile, setProfile] = useState<AutomationProfile | null>(null);
  const [autoProcess, setAutoProcess] = useState<boolean | null>(null);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const [saveError, setSaveError] = useState<string | null>(null);
  const [outdated, setOutdated] = useState(false);
  const [regenerating, setRegenerating] = useState(false);
  const [regenerateMessage, setRegenerateMessage] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    setLoadError(null);
    setOutdated(false);
    setRegenerateMessage(null);
    Promise.all([ipc.getAutomationProfile(projectId), ipc.getAutomationOptions(projectId)])
      .then(([p, o]) => {
        if (cancelled) return;
        setProfile(p.profile);
        setAutoProcess(o.autoProcess);
      })
      .catch((error: unknown) => {
        if (!cancelled) setLoadError(messageOf(error));
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [projectId]);

  const changeProfile = async (next: AutomationProfile) => {
    if (saving || next === profile) return;
    setSaving(true);
    setSaveError(null);
    try {
      const dto = await ipc.setAutomationProfile(projectId, next);
      setProfile(dto.profile);
      setOutdated(true);
      setRegenerateMessage(null);
    } catch (error) {
      setSaveError(messageOf(error));
    } finally {
      setSaving(false);
    }
  };

  const changeAutoProcess = async (next: boolean) => {
    if (saving || next === autoProcess) return;
    setSaving(true);
    setSaveError(null);
    try {
      const dto = await ipc.setAutomationOptions(projectId, next);
      setAutoProcess(dto.autoProcess);
    } catch (error) {
      setSaveError(messageOf(error));
    } finally {
      setSaving(false);
    }
  };

  const regeneratePlan = async () => {
    if (regenerating) return;
    setRegenerating(true);
    setRegenerateMessage(null);
    try {
      await ipc.generateEditPlan(projectId);
      setOutdated(false);
      setRegenerateMessage('Plano regenerado com o perfil atual.');
    } catch (error) {
      setRegenerateMessage(`Falha ao regenerar o plano: ${messageOf(error)}`);
    } finally {
      setRegenerating(false);
    }
  };

  if (loading) {
    return <p role="status" className="text-xs text-slate-400">Carregando preferências de automação…</p>;
  }

  if (loadError) {
    return (
      <p role="alert" className="text-xs text-rose-300">
        Não foi possível ler as preferências de automação: {loadError}
      </p>
    );
  }

  return (
    <section aria-labelledby="automation-prefs-title" className="space-y-5">
      <div>
        <h3 id="automation-prefs-title" className="text-sm font-bold text-white">
          Automação do projeto
        </h3>
      </div>

      <div role="radiogroup" aria-label="Perfil de automação" className="grid grid-cols-1 md:grid-cols-3 gap-3">
        {AUTOMATION_PROFILE_OPTIONS.map((option) => {
          const checked = profile === option.value;
          return (
            <button
              key={option.value}
              type="button"
              role="radio"
              aria-checked={checked}
              disabled={saving}
              onClick={() => void changeProfile(option.value)}
              className={`text-left p-4 rounded-xl border focus:outline-none focus-visible:ring-2 focus-visible:ring-cyan-400 disabled:opacity-60 ${
                checked ? 'border-cyan-400 bg-cyan-950/30' : 'border-slate-800 bg-[#0a1424] hover:border-slate-600'
              }`}
            >
              <span className="block text-xs font-bold text-white">{option.label}</span>
              <span className="block mt-1 text-xs text-slate-400 leading-snug">{option.description}</span>
            </button>
          );
        })}
      </div>

      <div className="flex items-center justify-between gap-4 p-4 rounded-xl bg-[#0a1424] border border-slate-800">
        <div>
          <span id="auto-process-label" className="block text-xs font-bold text-white">
            Processamento automático após a análise
          </span>
          <span className="block text-xs text-slate-400">Gera o plano e aplica os cortes do perfil ao terminar a análise.</span>
        </div>
        <button
          type="button"
          role="switch"
          aria-checked={autoProcess === true}
          aria-labelledby="auto-process-label"
          disabled={saving || autoProcess === null}
          onClick={() => void changeAutoProcess(autoProcess !== true)}
          className={`relative w-11 h-6 rounded-full transition-colors focus:outline-none focus-visible:ring-2 focus-visible:ring-cyan-400 disabled:opacity-60 ${
            autoProcess ? 'bg-cyan-500' : 'bg-slate-700'
          }`}
        >
          <span
            className={`absolute top-0.5 left-0.5 w-5 h-5 rounded-full bg-white transition-transform ${
              autoProcess ? 'translate-x-5' : ''
            }`}
          />
        </button>
      </div>

      {saveError && (
        <p role="alert" className="text-xs text-rose-300">
          Não foi possível salvar: {saveError}
        </p>
      )}

      {outdated && (
        <div role="status" className="p-4 rounded-xl bg-amber-950/30 border border-amber-500/30 text-amber-200 text-xs space-y-3">
          <p>
            A revisão do plano ficou desatualizada (PLAN_OUTDATED) porque o perfil mudou. Regenere o plano para aplicar o
            novo perfil.
          </p>
          <button
            type="button"
            onClick={() => void regeneratePlan()}
            disabled={regenerating}
            className="px-3 py-1.5 rounded-lg bg-amber-500/20 border border-amber-400/50 text-amber-100 font-semibold hover:bg-amber-500/30 focus:outline-none focus-visible:ring-2 focus-visible:ring-cyan-400 disabled:opacity-60"
          >
            {regenerating ? 'Regenerando…' : 'Regenerar plano'}
          </button>
        </div>
      )}

      {regenerateMessage && (
        <p role="status" className="text-xs text-slate-300">
          {regenerateMessage}
        </p>
      )}
    </section>
  );
};
