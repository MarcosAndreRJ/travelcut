import React from 'react';
import { Info } from 'lucide-react';
import type { EditPlanDto, PlanClipDto } from '../../types/ipc';
import { Badge, HypothesisBadge } from './PlanPrimitives';
import { findClip } from './planModel';
import { basename, confidenceClass, confidenceLabel, roleLabel, transitionLabel } from './planFormat';
import {
  formatDuration,
  formatSeconds3,
  formatSourceTimecode,
  formatSpeed,
  formatTimecode,
  formatZoom,
  framesToSeconds,
  isNormalSpeed,
  usToSeconds
} from './planTime';

interface ClipInspectorProps {
  plan: EditPlanDto;
  clip: PlanClipDto | null;
  onSelectClip: (clipId: string) => void;
}

const Row: React.FC<{ label: string; children: React.ReactNode }> = ({ label, children }) => (
  <div className="grid grid-cols-[7.5rem_1fr] gap-2 py-1 text-xs">
    <dt className="text-slate-400">{label}</dt>
    <dd className="text-slate-100 min-w-0 break-words">{children}</dd>
  </div>
);

const Section: React.FC<{ title: string; children: React.ReactNode }> = ({ title, children }) => (
  <section className="space-y-1 pt-3 border-t border-slate-800/80 first:border-t-0 first:pt-0">
    <h4 className="text-[11px] font-bold uppercase tracking-wider text-slate-400">{title}</h4>
    {children}
  </section>
);

export const ClipInspector: React.FC<ClipInspectorProps> = ({ plan, clip, onSelectClip }) => {
  if (!clip) {
    return (
      <aside aria-label="Detalhes do clipe" className="flex flex-col items-center justify-center text-center gap-2 p-6 h-full">
        <Info className="w-5 h-5 text-slate-500" aria-hidden="true" />
        <p className="text-xs text-slate-400 max-w-xs">
          Selecione um clipe na timeline para ver fonte, duração na timeline, justificativa, evidências e sugestões vinculadas.
        </p>
      </aside>
    );
  }

  const tb = plan.timeBase;
  const section = clip.sectionId ? plan.sections.find((s) => s.sectionId === clip.sectionId) ?? null : null;
  const linked = findClip(plan, clip.linkedClipId);
  const incoming = plan.transitions.filter((t) => t.toClipId === clip.clipId);
  const outgoing = plan.transitions.filter((t) => t.fromClipId === clip.clipId);
  const sourceDurationSec = usToSeconds(clip.sourceOutUs - clip.sourceInUs);
  const speedText = isNormalSpeed(clip.speedNum, clip.speedDen)
    ? '1× (normal)'
    : formatSpeed(clip.speedNum, clip.speedDen);

  return (
    <aside aria-label="Detalhes do clipe" className="flex flex-col gap-3 p-4 h-full overflow-y-auto">
      <div className="space-y-1">
        <div className="flex items-center gap-2 flex-wrap">
          <span className="text-[10px] font-mono text-cyan-400">{clip.clipId}</span>
          <Badge tone="slate">Trilha {clip.trackId}</Badge>
          {clip.needsHumanReview && <Badge tone="amber">precisa de revisão</Badge>}
        </div>
        <h3 className="text-sm font-bold text-white break-all">{basename(clip.relativePath)}</h3>
        <p className="text-[11px] text-slate-500 break-all">{clip.relativePath}</p>
        {section && (
          <p className="text-[11px] text-indigo-300 flex items-center gap-1.5 flex-wrap">
            Seção {section.order}: {roleLabel(section.role)} · {section.title}
            {section.isHypothesis && <HypothesisBadge />}
          </p>
        )}
      </div>

      <Section title="Fonte (tempo real da mídia)">
        <dl>
          <Row label="Entrada">
            <span className="font-mono">{formatSeconds3(usToSeconds(clip.sourceInUs))}</span>
            <span className="block font-mono text-[10px] text-slate-500">{formatSourceTimecode(clip.sourceInUs)}</span>
          </Row>
          <Row label="Saída">
            <span className="font-mono">{formatSeconds3(usToSeconds(clip.sourceOutUs))}</span>
            <span className="block font-mono text-[10px] text-slate-500">{formatSourceTimecode(clip.sourceOutUs)}</span>
          </Row>
          <Row label="Trecho na fonte">
            <span className="font-mono">{formatSeconds3(sourceDurationSec)}</span>
          </Row>
        </dl>
      </Section>

      <Section title="Timeline (planejada)">
        <dl>
          <Row label="Início">
            <span className="font-mono">{formatTimecode(clip.timelineStartFrame, tb)}</span>
            <span className="block font-mono text-[10px] text-slate-500">
              {formatSeconds3(framesToSeconds(clip.timelineStartFrame, tb))}
            </span>
          </Row>
          <Row label="Duração">
            <span className="font-mono">{formatTimecode(clip.durationFrames, tb)}</span>
            <span className="block font-mono text-[10px] text-slate-500">
              {formatSeconds3(framesToSeconds(clip.durationFrames, tb))} · {formatDuration(framesToSeconds(clip.durationFrames, tb))} · {clip.durationFrames} quadros
            </span>
          </Row>
          <Row label="Velocidade">{speedText}</Row>
          <Row label="Zoom">
            {clip.zoom === null ? 'Sem zoom' : <span>{formatZoom(clip.zoom ?? 1)} <Badge tone="cyan">zoom sugerido</Badge></span>}
          </Row>
        </dl>
      </Section>

      <Section title="Análise">
        <dl>
          <Row label="Confiança">
            <span className={`inline-flex px-1.5 py-0.5 rounded border text-[11px] font-semibold ${confidenceClass(clip.confidenceBand)}`}>
              {confidenceLabel(clip.confidenceBand)}
            </span>
            <span className="block text-[10px] text-slate-500">Faixa qualitativa, não percentual.</span>
          </Row>
          <Row label="Revisão humana">{clip.needsHumanReview ? 'Necessária' : 'Não marcada'}</Row>
        </dl>
        <p className="text-xs text-slate-200 leading-relaxed pt-1">{clip.rationale || 'Sem justificativa registrada.'}</p>
        <div>
          <h5 className="text-[11px] font-semibold text-slate-400 mb-1">Evidências</h5>
          {(clip.evidence?.length ?? 0) === 0 ? (
            <p className="text-[11px] text-slate-500">Nenhuma evidência registrada.</p>
          ) : (
            <ul className="list-disc pl-4 space-y-0.5 text-[11px] text-slate-300">
              {clip.evidence?.map((item, index) => (
                <li key={`${index}-${item}`}>{item}</li>
              ))}
            </ul>
          )}
        </div>
      </Section>

      <Section title="Vínculos">
        <div className="space-y-1 text-xs">
          <p className="text-slate-400">
            Sugestões vinculadas:{' '}
            {(clip.suggestionIds?.length ?? 0) === 0 ? (
              <span className="text-slate-500">nenhuma</span>
            ) : (
              <span className="font-mono text-slate-200 break-all">{clip.suggestionIds?.join(', ')}</span>
            )}
          </p>
          <p className="text-slate-400">
            Clipe vinculado:{' '}
            {linked ? (
              <button
                type="button"
                onClick={() => onSelectClip(linked.clipId)}
                className="font-mono text-cyan-300 underline underline-offset-2 hover:text-cyan-200 focus:outline-none focus-visible:ring-2 focus-visible:ring-cyan-400 rounded"
              >
                {linked.trackId} · {basename(linked.relativePath)}
              </button>
            ) : (
              <span className="text-slate-500">nenhum</span>
            )}
          </p>
          {incoming.map((t) => (
            <p key={`in-${t.fromClipId}`} className="text-slate-400">
              Entrada: {transitionLabel(t.kind)} ({t.durationFrames} quadros)
            </p>
          ))}
          {outgoing.map((t) => (
            <p key={`out-${t.toClipId}`} className="text-slate-400">
              Saída: {transitionLabel(t.kind)} ({t.durationFrames} quadros)
            </p>
          ))}
        </div>
      </Section>
    </aside>
  );
};
