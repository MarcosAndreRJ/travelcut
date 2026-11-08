import React from 'react';
import type { SceneClassificationDto, StageState } from '../../types/ipc';
import { confidenceText, confidenceTone, sceneCategoryLabel, stageStateLabel } from './analysisFormat';

interface SceneCardProps {
  scene: SceneClassificationDto | null;
  sceneStageState: StageState | undefined;
}

/** Classificação de cena: categoria, selo "hipótese" quando aplicável e evidências listadas. */
export const SceneCard: React.FC<SceneCardProps> = ({ scene, sceneStageState }) => {
  if (!scene) {
    return (
      <p className="text-[11px] text-slate-400">
        {sceneStageState
          ? `Etapa de cena: ${stageStateLabel(sceneStageState)}.`
          : 'Sem classificação de cena para este clipe.'}
      </p>
    );
  }

  const speech =
    scene.hasSpeech === null || scene.hasSpeech === undefined
      ? 'não determinada'
      : scene.hasSpeech
        ? 'detectada'
        : 'não detectada';

  return (
    <div className="space-y-2">
      <div className="flex flex-wrap items-center gap-2">
        <span className="px-2 py-0.5 rounded bg-cyan-500/15 border border-cyan-500/30 text-cyan-200 text-xs font-bold">
          {sceneCategoryLabel(scene.category)}
        </span>
        {scene.isHypothesis && (
          <span className="px-2 py-0.5 rounded border border-amber-500/40 text-amber-300 text-[10px] font-bold uppercase tracking-wide">
            hipótese
          </span>
        )}
        <span className={`px-1.5 py-0.5 rounded border text-[10px] font-bold ${confidenceTone(scene.confidenceBand)}`}>
          Confiança {confidenceText(scene.confidenceBand)}
        </span>
      </div>
      <p className="text-[11px] text-slate-400">Fala: {speech}</p>
      {scene.isHypothesis && (
        <p className="text-[10px] text-amber-200/80">Classificação sugerida: revisão humana recomendada.</p>
      )}
      {(scene.evidence?.length ?? 0) > 0 ? (
        <ul className="list-disc pl-4 text-[11px] text-slate-300 space-y-0.5">
          {scene.evidence?.map((ev, idx) => (
            <li key={idx}>{ev}</li>
          ))}
        </ul>
      ) : (
        <p className="text-[11px] text-slate-500">Sem evidências registradas.</p>
      )}
    </div>
  );
};
