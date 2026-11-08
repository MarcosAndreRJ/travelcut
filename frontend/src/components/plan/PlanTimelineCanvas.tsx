import React, { useMemo, useRef } from 'react';
import { Maximize2, ZoomIn, ZoomOut } from 'lucide-react';
import type { EditPlanDto, PlanClipDto } from '../../types/ipc';
import { Badge, HypothesisBadge } from './PlanPrimitives';
import {
  adjacentClipId,
  buildTrackRows,
  rulerStepSeconds,
  sortedSections,
  timelineEndFrame,
  transitionMarkers,
  type TrackRow
} from './planModel';
import {
  formatDuration,
  formatSpeed,
  formatTimecode,
  framesToSeconds,
  isNormalSpeed,
  nominalFps,
  secondsToFrames
} from './planTime';
import { basename, roleLabel, trackKindLabel, transitionLabel } from './planFormat';
import { TimelineWaveform } from '../audio/TimelineWaveform';
import { AudioSyncBadge } from '../audio/AudioSyncBadge';

const LABEL_WIDTH_PX = 132;
/** Escala base: 4 px por segundo real em zoom 100%. */
const BASE_PX_PER_SECOND = 4;
const MIN_ZOOM = 0.05;
const MAX_ZOOM = 32;

interface PlanTimelineCanvasProps {
  plan: EditPlanDto;
  selectedClipId: string | null;
  onSelectClip: (clipId: string | null) => void;
  zoom: number;
  onZoomChange: (zoom: number) => void;
  currentFrame?: number;
  onSeekFrame?: (frame: number | ((prev: number) => number)) => void;
  /** Clipes cujo arquivo de mídia está ausente (SOURCE_FILE_MISSING): destaque vermelho. */
  missingClipIds?: ReadonlySet<string>;
  /** F14.2: Operações interativas da timeline */
  onMoveClip?: (clipId: string, newStartFrame: number) => void;
  onTrimClip?: (clipId: string, edge: 'start' | 'end', newFrame: number) => void;
  snappingEnabled?: boolean;
  snapGuide?: number | null;
  onSnapGuideChange?: (guide: number | null) => void;
  calculateSnap?: (frame: number, excludeClipIds?: string[]) => { snappedFrame: number; snappedTo: number | null };
  selectedClipIds?: ReadonlySet<string>;
}

const clampZoom = (value: number): number => Math.min(MAX_ZOOM, Math.max(MIN_ZOOM, value));

const TRACK_TONE: Record<string, string> = {
  video: 'bg-cyan-800/85 border-cyan-400/70',
  dialogue: 'bg-emerald-800/85 border-emerald-400/70',
  music: 'bg-amber-800/85 border-amber-400/70'
};

const MISSING_SOURCE_TONE = 'bg-rose-900/90 border-rose-400';
const clipTone = (kind: string): string => TRACK_TONE[kind] ?? 'bg-slate-700/85 border-slate-400/70';

/** Faixa V2 (B-roll) é sempre exibida, mesmo sem trilha declarada no plano. */
const B_ROLL_TRACK_ID = 'V2';
const EMPTY_B_ROLL_TEXT = 'sem clipes — B-roll ainda não planejado';
const withBRollRow = (rows: TrackRow[]): TrackRow[] =>
  rows.some((row) => row.trackId === B_ROLL_TRACK_ID)
    ? rows
    : [...rows, { trackId: B_ROLL_TRACK_ID, label: B_ROLL_TRACK_ID, kind: 'video', clips: [] }];

export const PlanTimelineCanvas: React.FC<PlanTimelineCanvasProps> = ({
  plan,
  selectedClipId,
  onSelectClip,
  zoom,
  onZoomChange,
  currentFrame = 0,
  onSeekFrame,
  missingClipIds,
  onMoveClip,
  onTrimClip,
  snappingEnabled = true,
  snapGuide = null,
  onSnapGuideChange,
  calculateSnap,
  selectedClipIds
}) => {
  const [activeDrag, setActiveDrag] = React.useState<{
    clipId: string;
    mode: 'move' | 'trim-start' | 'trim-end';
    startX: number;
    initialStartFrame: number;
    initialDurationFrames: number;
    deltaFrames: number;
  } | null>(null);

  React.useEffect(() => {
    if (!activeDrag) return;

    const handlePointerMove = (e: PointerEvent) => {
      const deltaPx = e.clientX - activeDrag.startX;
      const rawDeltaFrames = Math.round(deltaPx / pxPerFrame);

      if (activeDrag.mode === 'move') {
        let targetFrame = Math.max(0, activeDrag.initialStartFrame + rawDeltaFrames);
        if (snappingEnabled && calculateSnap) {
          const snap = calculateSnap(targetFrame, [activeDrag.clipId]);
          targetFrame = snap.snappedFrame;
          onSnapGuideChange?.(snap.snappedTo);
        } else {
          onSnapGuideChange?.(null);
        }
        setActiveDrag((prev) => (prev ? { ...prev, deltaFrames: targetFrame - prev.initialStartFrame } : null));
      } else if (activeDrag.mode === 'trim-start') {
        let targetStart = activeDrag.initialStartFrame + rawDeltaFrames;
        if (snappingEnabled && calculateSnap) {
          const snap = calculateSnap(targetStart, [activeDrag.clipId]);
          targetStart = snap.snappedFrame;
          onSnapGuideChange?.(snap.snappedTo);
        } else {
          onSnapGuideChange?.(null);
        }
        const maxStart = activeDrag.initialStartFrame + activeDrag.initialDurationFrames - 1;
        targetStart = Math.max(0, Math.min(maxStart, targetStart));
        setActiveDrag((prev) => (prev ? { ...prev, deltaFrames: targetStart - prev.initialStartFrame } : null));
      } else if (activeDrag.mode === 'trim-end') {
        let targetEnd = activeDrag.initialStartFrame + activeDrag.initialDurationFrames + rawDeltaFrames;
        if (snappingEnabled && calculateSnap) {
          const snap = calculateSnap(targetEnd, [activeDrag.clipId]);
          targetEnd = snap.snappedFrame;
          onSnapGuideChange?.(snap.snappedTo);
        } else {
          onSnapGuideChange?.(null);
        }
        targetEnd = Math.max(activeDrag.initialStartFrame + 1, targetEnd);
        setActiveDrag((prev) => (prev ? { ...prev, deltaFrames: targetEnd - (prev.initialStartFrame + prev.initialDurationFrames) } : null));
      }
    };

    const handlePointerUp = () => {
      if (activeDrag) {
        if (activeDrag.mode === 'move' && onMoveClip) {
          const finalFrame = Math.max(0, activeDrag.initialStartFrame + activeDrag.deltaFrames);
          if (finalFrame !== activeDrag.initialStartFrame) {
            onMoveClip(activeDrag.clipId, finalFrame);
          }
        } else if (activeDrag.mode === 'trim-start' && onTrimClip) {
          const finalStart = Math.max(0, activeDrag.initialStartFrame + activeDrag.deltaFrames);
          if (finalStart !== activeDrag.initialStartFrame) {
            onTrimClip(activeDrag.clipId, 'start', finalStart);
          }
        } else if (activeDrag.mode === 'trim-end' && onTrimClip) {
          const finalEnd = Math.max(activeDrag.initialStartFrame + 1, activeDrag.initialStartFrame + activeDrag.initialDurationFrames + activeDrag.deltaFrames);
          if (finalEnd !== activeDrag.initialStartFrame + activeDrag.initialDurationFrames) {
            onTrimClip(activeDrag.clipId, 'end', finalEnd);
          }
        }
      }
      setActiveDrag(null);
      onSnapGuideChange?.(null);
    };

    window.addEventListener('pointermove', handlePointerMove);
    window.addEventListener('pointerup', handlePointerUp);
    return () => {
      window.removeEventListener('pointermove', handlePointerMove);
      window.removeEventListener('pointerup', handlePointerUp);
    };
  }, [activeDrag, pxPerFrame, snappingEnabled, calculateSnap, onSnapGuideChange, onMoveClip, onTrimClip]);
  const scrollRef = useRef<HTMLDivElement>(null);
  const clipRefs = useRef(new Map<string, HTMLButtonElement>());
  const scrubbingRef = useRef<boolean>(false);
  const tb = plan.timeBase;

  const secPerFrame = tb.frameDurationNum / tb.frameDurationDen;
  const pxPerSecond = BASE_PX_PER_SECOND * zoom;
  const pxPerFrame = secPerFrame * pxPerSecond;
  const totalFrames = timelineEndFrame(plan);
  const totalSeconds = framesToSeconds(totalFrames, tb);
  const contentWidth = Math.max(640, Math.ceil(totalFrames * pxPerFrame) + 32);
  const fps = nominalFps(tb);

  const rows = useMemo(() => withBRollRow(buildTrackRows(plan)), [plan]);
  const sections = useMemo(() => sortedSections(plan), [plan]);
  const markers = useMemo(() => transitionMarkers(plan), [plan]);
  const markersByTrack = useMemo(() => {
    const map = new Map<string, typeof markers>();
    for (const marker of markers) {
      const list = map.get(marker.toClip.trackId) ?? [];
      list.push(marker);
      map.set(marker.toClip.trackId, list);
    }
    return map;
  }, [markers]);

  const stepSeconds = rulerStepSeconds(pxPerSecond, totalSeconds);
  const ticks: { key: number; x: number; label: string }[] = [];
  if (Number.isFinite(totalSeconds)) {
    const lastTick = Math.ceil(totalSeconds / stepSeconds) * stepSeconds;
    for (let k = 0; k * stepSeconds <= lastTick; k++) {
      const seconds = k * stepSeconds;
      const frame = secondsToFrames(seconds, tb);
      ticks.push({ key: k, x: frame * pxPerFrame, label: formatTimecode(frame, tb) });
    }
  }

  const fitToWidth = () => {
    const available = (scrollRef.current?.clientWidth ?? 0) - LABEL_WIDTH_PX - 32;
    if (!(available > 0) || !(totalSeconds > 0)) return;
    onZoomChange(clampZoom(available / (totalSeconds * BASE_PX_PER_SECOND)));
  };

  const setSelectedRef = (clipId: string, el: HTMLButtonElement | null) => {
    if (el) clipRefs.current.set(clipId, el);
    else clipRefs.current.delete(clipId);
  };

  const moveSelection = (delta: 1 | -1) => {
    const next = adjacentClipId(plan, selectedClipId, delta);
    if (!next) return;
    onSelectClip(next);
    const el = clipRefs.current.get(next);
    el?.scrollIntoView?.({ block: 'nearest', inline: 'nearest' });
  };

  const onKeyDown = (event: React.KeyboardEvent<HTMLDivElement>) => {
    if (event.key === 'ArrowRight') {
      event.preventDefault();
      moveSelection(1);
    } else if (event.key === 'ArrowLeft') {
      event.preventDefault();
      moveSelection(-1);
    } else if (event.key === 'Escape' && selectedClipId) {
      event.preventDefault();
      onSelectClip(null);
    }
  };

  /** Frame sob o ponteiro na régua (limitado a [0, fim da timeline]). */
  const frameAtPointer = (clientX: number, element: HTMLElement): number => {
    const rect = element.getBoundingClientRect();
    if (!(pxPerFrame > 0)) return 0;
    return Math.max(0, Math.min(totalFrames, Math.round((clientX - rect.left) / pxPerFrame)));
  };

  // Agulha de scrub: arrastar na régua move o frame atual (captura o ponteiro durante o arraste).
  const handleRulerPointerDown = (e: React.PointerEvent<HTMLDivElement>) => {
    if (!onSeekFrame || e.button !== 0) return;
    scrubbingRef.current = true;
    e.currentTarget.setPointerCapture?.(e.pointerId);
    onSeekFrame(frameAtPointer(e.clientX, e.currentTarget));
  };
  const handleRulerPointerMove = (e: React.PointerEvent<HTMLDivElement>) => {
    if (!scrubbingRef.current || !onSeekFrame) return;
    onSeekFrame(frameAtPointer(e.clientX, e.currentTarget));
  };
  const endRulerScrub = (e: React.PointerEvent<HTMLDivElement>) => {
    scrubbingRef.current = false;
    e.currentTarget.releasePointerCapture?.(e.pointerId);
  };

  const clipLabel = (clip: PlanClipDto, missingSource: boolean): string => {
    const parts = [
      basename(clip.relativePath),
      `trilha ${clip.trackId}`,
      `início ${formatTimecode(clip.timelineStartFrame, tb)}`,
      `duração ${formatTimecode(clip.durationFrames, tb)}`
    ];
    if (!isNormalSpeed(clip.speedNum, clip.speedDen)) parts.push(`velocidade ${formatSpeed(clip.speedNum, clip.speedDen)}`);
    if (clip.needsHumanReview) parts.push('precisa de revisão');
    if (missingSource) parts.push('arquivo de mídia ausente');
    return parts.join(', ');
  };

  const sectionsWidthPx = (startFrame: number, endFrame: number) => ({
    left: startFrame * pxPerFrame,
    width: Math.max(4, (endFrame - startFrame) * pxPerFrame)
  });

  const playheadX = currentFrame * pxPerFrame;

  return (
    <div className="flex flex-col min-h-0 flex-1 rounded-xl border border-slate-800 bg-[#080d19] overflow-hidden">
      {/* Barra de ferramentas da timeline */}
      <div className="flex flex-wrap items-center justify-between gap-2 px-3 py-2 border-b border-slate-800 bg-[#0a1222]">
        <div className="flex items-center gap-2 text-[11px] text-slate-400 min-w-0">
          <span className="font-semibold text-slate-200">Timeline planejada</span>
          <span className="font-mono text-slate-300" data-testid="timeline-duration">
            {formatDuration(totalSeconds)}
          </span>
          <span className="font-mono text-slate-500" title="Timecode NDF com fps nominal">
            {formatTimecode(totalFrames, tb)} · NDF {fps} fps
          </span>
          {onSeekFrame && (
            <span className="ml-2 font-mono text-[10px] px-1.5 py-0.5 rounded bg-cyan-950/80 border border-cyan-700/50 text-cyan-300">
              Posição: {formatTimecode(currentFrame, tb)}
            </span>
          )}
        </div>
        <div className="flex items-center gap-1.5">
          <button
            type="button"
            aria-label="Reduzir zoom da timeline"
            onClick={() => onZoomChange(clampZoom(zoom / 1.5))}
            className="p-1.5 rounded-lg text-slate-300 hover:bg-slate-800 focus:outline-none focus-visible:ring-2 focus-visible:ring-cyan-400"
          >
            <ZoomOut className="w-3.5 h-3.5" aria-hidden="true" />
          </button>
          <span className="font-mono text-[11px] text-cyan-300 w-12 text-center" aria-live="polite">
            {Math.round(zoom * 100)}%
          </span>
          <button
            type="button"
            aria-label="Ampliar zoom da timeline"
            onClick={() => onZoomChange(clampZoom(zoom * 1.5))}
            className="p-1.5 rounded-lg text-slate-300 hover:bg-slate-800 focus:outline-none focus-visible:ring-2 focus-visible:ring-cyan-400"
          >
            <ZoomIn className="w-3.5 h-3.5" aria-hidden="true" />
          </button>
          <button
            type="button"
            aria-label="Ajustar timeline à janela"
            onClick={fitToWidth}
            className="ml-1 px-2 py-1 rounded-lg text-[11px] text-slate-300 hover:bg-slate-800 flex items-center gap-1 focus:outline-none focus-visible:ring-2 focus-visible:ring-cyan-400"
          >
            <Maximize2 className="w-3.5 h-3.5" aria-hidden="true" /> Ajustar
          </button>
        </div>
      </div>

      {/* Área rolável: régua, seções e trilhas */}
      <div
        ref={scrollRef}
        tabIndex={0}
        role="group"
        aria-label="Clipes da timeline. Use as setas para navegar e Esc para limpar a seleção."
        onKeyDown={onKeyDown}
        className="flex-1 min-h-0 overflow-auto focus:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-cyan-400 relative"
        data-testid="timeline-scroll"
      >
        <div className="relative" style={{ width: LABEL_WIDTH_PX + contentWidth }}>
          {/* Linha vertical do playhead através de toda a timeline */}
          {snapGuide !== null && (
            <div
              className="absolute top-0 bottom-0 pointer-events-none z-30 w-0.5 bg-cyan-400 shadow-[0_0_8px_rgba(34,211,238,0.9)]"
              style={{ left: LABEL_WIDTH_PX + snapGuide * pxPerFrame }}
            >
              <div className="absolute top-0 -left-1.5 w-3.5 h-3.5 bg-cyan-400 rotate-45 shadow-sm" />
            </div>
          )}
          {onSeekFrame && (
            <div
              className="absolute top-0 bottom-0 pointer-events-none z-40 flex flex-col items-center"
              style={{ left: LABEL_WIDTH_PX + playheadX }}
            >
              <div className="flex items-center gap-1 -translate-y-1">
                <div className="w-2.5 h-2.5 bg-red-500 rotate-45 shadow-sm" />
                <span
                  data-testid="playhead-timecode"
                  className="font-mono text-[10px] px-1 rounded bg-red-600/90 text-white whitespace-nowrap"
                >
                  {formatTimecode(currentFrame, tb)}
                </span>
              </div>
              <div className="w-0.5 flex-1 bg-red-500/80 shadow-[0_0_4px_rgba(239,68,68,0.8)]" />
            </div>
          )}

          {/* Régua com timecode NDF e suporte a clique/scrub */}
          <div className="sticky top-0 z-20 flex h-7 bg-[#0a1222] border-b border-slate-800">
            <div
              className="sticky left-0 z-30 bg-[#0a1222] border-r border-slate-800 text-[10px] font-semibold text-slate-400 flex items-center px-3"
              style={{ width: LABEL_WIDTH_PX }}
            >
              Régua (NDF)
            </div>
            <div
              className={`relative select-none touch-none ${onSeekFrame ? 'cursor-ew-resize' : ''}`}
              style={{ width: contentWidth }}
              data-testid="timeline-ruler"
              onPointerDown={handleRulerPointerDown}
              onPointerMove={handleRulerPointerMove}
              onPointerUp={endRulerScrub}
              onPointerCancel={endRulerScrub}
            >
              {ticks.map((tick) => (
                <div key={tick.key} className="absolute top-0 bottom-0 pointer-events-none" style={{ left: tick.x }}>
                  <div className="w-px h-2 bg-slate-600" />
                  <span className="absolute top-2 left-1 whitespace-nowrap font-mono text-[10px] text-slate-400">
                    {tick.label}
                  </span>
                </div>
              ))}
            </div>
          </div>

          {/* Faixa de seções */}
          <div className="flex h-12 border-b border-slate-800/80">
            <div
              className="sticky left-0 z-10 bg-[#080e1b] border-r border-slate-800 px-3 flex flex-col justify-center text-[11px] font-bold text-indigo-300"
              style={{ width: LABEL_WIDTH_PX }}
            >
              Seções
              <span className="text-[9px] font-normal text-slate-500">estrutura narrativa</span>
            </div>
            <div className="relative" style={{ width: contentWidth }} data-testid="timeline-sections">
              {sections.length === 0 && (
                <span className="absolute left-2 top-3 text-[11px] text-slate-500">Nenhuma seção neste plano.</span>
              )}
              {sections.map((section) => {
                const pos = sectionsWidthPx(section.startFrame, section.endFrame);
                return (
                  <div
                    key={section.sectionId}
                    className="absolute top-1 bottom-1 rounded-md border border-indigo-400/40 bg-indigo-950/70 px-2 py-0.5 overflow-hidden"
                    style={pos}
                    title={`${roleLabel(section.role)}: ${section.title}`}
                    data-testid="timeline-section"
                  >
                    <div className="flex items-center gap-1.5 min-w-0">
                      <span className="text-[10px] font-bold text-indigo-200 truncate">
                        {roleLabel(section.role)} · {section.title}
                      </span>
                      {section.isHypothesis && <HypothesisBadge />}
                    </div>
                  </div>
                );
              })}
            </div>
          </div>

          {/* Trilhas */}
          {rows.map((row) => (
            <div key={row.trackId} className="flex h-16 border-b border-slate-800/70" data-testid="timeline-track">
              <div
                className="sticky left-0 z-10 bg-[#080d19] border-r border-slate-800 px-3 flex flex-col justify-center min-w-0"
                style={{ width: LABEL_WIDTH_PX }}
              >
                <span className="text-xs font-bold text-slate-100 truncate" title={row.label}>
                  {row.trackId}
                </span>
                <span className="text-[10px] text-slate-500 truncate">{trackKindLabel(row.kind)}</span>
              </div>
              <div className="relative" style={{ width: contentWidth }}>
                {row.clips.length === 0 && (
                  <span
                    className="absolute left-2 top-1/2 -translate-y-1/2 text-[11px] text-slate-600"
                    data-testid={row.trackId === B_ROLL_TRACK_ID ? 'b-roll-empty' : undefined}
                  >
                    {row.trackId === B_ROLL_TRACK_ID ? EMPTY_B_ROLL_TEXT : 'Sem clipes nesta trilha'}
                  </span>
                )}
                {row.clips.map((clip) => {
                  const selected = clip.clipId === selectedClipId || (selectedClipIds?.has(clip.clipId) ?? false);
                  const isLinkedToSelected = Boolean(
                    selectedClipId &&
                      (clip.linkedClipId === selectedClipId ||
                        plan.clips.find((c) => c.clipId === selectedClipId)?.linkedClipId === clip.clipId)
                  );
                  const speedBadge = !isNormalSpeed(clip.speedNum, clip.speedDen);
                  const missingSource = missingClipIds?.has(clip.clipId) ?? false;

                  let leftPx = clip.timelineStartFrame * pxPerFrame;
                  let widthPx = Math.max(3, clip.durationFrames * pxPerFrame);
                  if (activeDrag?.clipId === clip.clipId) {
                    if (activeDrag.mode === 'move') {
                      leftPx = Math.max(0, (clip.timelineStartFrame + activeDrag.deltaFrames) * pxPerFrame);
                    } else if (activeDrag.mode === 'trim-start') {
                      leftPx = Math.max(0, (clip.timelineStartFrame + activeDrag.deltaFrames) * pxPerFrame);
                      widthPx = Math.max(3, (clip.durationFrames - activeDrag.deltaFrames) * pxPerFrame);
                    } else if (activeDrag.mode === 'trim-end') {
                      widthPx = Math.max(3, (clip.durationFrames + activeDrag.deltaFrames) * pxPerFrame);
                    }
                  }

                  const pos = {
                    left: leftPx,
                    width: widthPx
                  };
                  const label = clipLabel(clip, missingSource);
                  return (
                    <button
                      key={clip.clipId}
                      ref={(el) => setSelectedRef(clip.clipId, el)}
                      type="button"
                      onClick={() => onSelectClip(clip.clipId)}
                      onPointerDown={(e) => {
                        if (e.button !== 0) return;
                        onSelectClip(clip.clipId);
                        setActiveDrag({
                          clipId: clip.clipId,
                          mode: 'move',
                          startX: e.clientX,
                          initialStartFrame: clip.timelineStartFrame,
                          initialDurationFrames: clip.durationFrames,
                          deltaFrames: 0,
                        });
                      }}
                      aria-pressed={selected}
                      aria-label={label}
                      title={label}
                      data-testid="timeline-clip"
                      data-missing-source={missingSource ? 'true' : undefined}
                      className={`group absolute top-1.5 bottom-1.5 rounded border px-1.5 py-0.5 text-left overflow-hidden flex flex-col justify-center gap-0.5 transition-colors cursor-grab active:cursor-grabbing hover:brightness-125 focus:outline-none focus-visible:ring-2 focus-visible:ring-cyan-300 ${
                        missingSource ? MISSING_SOURCE_TONE : clipTone(row.kind)
                      } ${selected ? 'ring-2 ring-cyan-300 z-10' : isLinkedToSelected ? 'ring-1 ring-cyan-400/80 border-dashed border-cyan-400' : ''} ${
                        clip.needsHumanReview ? 'border-2 border-amber-300 shadow-[0_0_0_1px_rgba(252,211,77,0.6)]' : ''
                      } ${missingSource ? 'border-2 shadow-[0_0_0_1px_rgba(251,113,133,0.8)]' : ''}`}
                      style={pos}
                    >
                      {/* Alça de aparar início (Trim In) */}
                      <div
                        className="absolute left-0 top-0 bottom-0 w-2.5 z-20 cursor-ew-resize hover:bg-cyan-400/60 rounded-l transition-colors"
                        title="Aparar início (Trim In)"
                        onPointerDown={(e) => {
                          e.stopPropagation();
                          e.preventDefault();
                          onSelectClip(clip.clipId);
                          setActiveDrag({
                            clipId: clip.clipId,
                            mode: 'trim-start',
                            startX: e.clientX,
                            initialStartFrame: clip.timelineStartFrame,
                            initialDurationFrames: clip.durationFrames,
                            deltaFrames: 0,
                          });
                        }}
                      />
                      {/* Alça de aparar fim (Trim Out) */}
                      <div
                        className="absolute right-0 top-0 bottom-0 w-2.5 z-20 cursor-ew-resize hover:bg-cyan-400/60 rounded-r transition-colors"
                        title="Aparar fim (Trim Out)"
                        onPointerDown={(e) => {
                          e.stopPropagation();
                          e.preventDefault();
                          onSelectClip(clip.clipId);
                          setActiveDrag({
                            clipId: clip.clipId,
                            mode: 'trim-end',
                            startX: e.clientX,
                            initialStartFrame: clip.timelineStartFrame,
                            initialDurationFrames: clip.durationFrames,
                            deltaFrames: 0,
                          });
                        }}
                      />
                      <span className="block truncate text-[10px] font-medium text-white">
                        {basename(clip.relativePath)}
                      </span>
                      <span className="flex items-center gap-1 min-w-0 overflow-hidden">
                        {missingSource && (
                          <Badge tone="rose" className="shrink-0">
                            arquivo ausente
                          </Badge>
                        )}
                        {speedBadge && (
                          <Badge tone="amber" className="shrink-0">
                            {formatSpeed(clip.speedNum, clip.speedDen)}
                          </Badge>
                        )}
                        {clip.zoom !== null && (
                          <Badge tone="cyan" className="shrink-0" title={`Zoom sugerido: ${clip.zoom}`}>
                            zoom sugerido
                          </Badge>
                        )}
                        {clip.needsHumanReview && (
                          <Badge tone="amber" className="shrink-0">
                            revisar
                          </Badge>
                        )}
                      </span>
                    </button>
                  );
                })}
                {(markersByTrack.get(row.trackId) ?? []).map(({ transition, toClip }) => (
                  <span
                    key={`${transition.fromClipId}-${transition.toClipId}`}
                    role="img"
                    aria-label={`Transição ${transitionLabel(transition.kind)} de entrada em ${basename(toClip.relativePath)}`}
                    title={`Transição: ${transitionLabel(transition.kind)} (${transition.durationFrames} quadros)`}
                    data-testid="timeline-transition"
                    className="absolute top-0 z-10 w-2.5 h-2.5 -translate-x-1/2 rotate-45 bg-violet-400 border border-violet-100"
                    style={{ left: toClip.timelineStartFrame * pxPerFrame }}
                  />
                ))}
              </div>
            </div>
          ))}

          {rows.length === 0 && (
            <p className="p-4 text-xs text-slate-500">Este plano não declara trilhas nem clipes.</p>
          )}

          {plan.clips.length === 0 && rows.length > 0 && (
            <p className="px-4 py-2 text-[11px] text-slate-500">Este plano não tem clipes. Seções permanecem hipóteses.</p>
          )}
        </div>
      </div>
    </div>
  );
};
