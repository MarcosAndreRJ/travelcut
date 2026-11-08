import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import type { EditPlanDto, PlanClipDto, PlanConflictDto, PlanEditorialOperationDto } from '../../types/ipc';
import { ipc } from '../../services/ipcBridge';

export interface SnapResult {
  snappedFrame: number;
  snappedTo: number | null;
}

export interface UseTimelineEditorOptions {
  initialPlan: EditPlanDto | null;
  projectId: string;
  currentFrame?: number;
  onPlanChanged?: (plan: EditPlanDto) => void;
}

export function useTimelineEditor({
  initialPlan,
  projectId,
  currentFrame = 0,
  onPlanChanged,
}: UseTimelineEditorOptions) {
  const [plan, setPlan] = useState<EditPlanDto | null>(initialPlan);
  const [history, setHistory] = useState<EditPlanDto[]>(initialPlan ? [initialPlan] : []);
  const [historyIndex, setHistoryIndex] = useState(0);
  const [selectedClipId, setSelectedClipId] = useState<string | null>(null);
  const [snappingEnabled, setSnappingEnabled] = useState(true);
  const [snapGuide, setSnapGuide] = useState<number | null>(null);
  const [isSaving, setIsSaving] = useState(false);
  const [saveError, setSaveError] = useState<string | null>(null);
  const [conflicts, setConflicts] = useState<PlanConflictDto[]>([]);
  const operationsLogRef = useRef<PlanEditorialOperationDto[]>([]);

  // Atualiza quando initialPlan muda externamente (p. ex. reabertura ou nova revisão)
  useEffect(() => {
    if (initialPlan) {
      setPlan(initialPlan);
      setHistory([initialPlan]);
      setHistoryIndex(0);
      operationsLogRef.current = [];
    }
  }, [initialPlan?.planId, initialPlan?.revision]);

  const canUndo = historyIndex > 0;
  const canRedo = historyIndex < history.length - 1;
  const isDirty = useMemo(() => {
    if (!initialPlan || !plan) return false;
    return initialPlan.revision !== plan.revision || historyIndex > 0;
  }, [initialPlan, plan, historyIndex]);

  const commitPlan = useCallback(
    (newPlan: EditPlanDto, op?: PlanEditorialOperationDto) => {
      // Recalcula maxEndFrame no summary
      const maxEnd = (newPlan.clips ?? []).reduce(
        (acc, c) => Math.max(acc, c.timelineStartFrame + c.durationFrames),
        0
      );
      const updatedPlan: EditPlanDto = {
        ...newPlan,
        summary: {
          ...newPlan.summary,
          timelineDurationFrames: maxEnd,
        },
      };

      if (op) {
        operationsLogRef.current.push(op);
      }

      setHistory((prev) => {
        const next = prev.slice(0, historyIndex + 1);
        next.push(updatedPlan);
        return next;
      });
      setHistoryIndex((prev) => prev + 1);
      setPlan(updatedPlan);
      onPlanChanged?.(updatedPlan);
    },
    [historyIndex, onPlanChanged]
  );

  const undo = useCallback(() => {
    if (!canUndo) return;
    const targetIdx = historyIndex - 1;
    const targetPlan = history[targetIdx];
    setHistoryIndex(targetIdx);
    setPlan(targetPlan);
    onPlanChanged?.(targetPlan);
  }, [canUndo, history, historyIndex, onPlanChanged]);

  const redo = useCallback(() => {
    if (!canRedo) return;
    const targetIdx = historyIndex + 1;
    const targetPlan = history[targetIdx];
    setHistoryIndex(targetIdx);
    setPlan(targetPlan);
    onPlanChanged?.(targetPlan);
  }, [canRedo, history, historyIndex, onPlanChanged]);

  // Snapping: encontra o ponto de snap mais próximo dentro do limiar (default: 8 frames)
  const calculateSnap = useCallback(
    (targetFrame: number, excludeClipIds: string[] = [], thresholdFrames = 8): SnapResult => {
      if (!snappingEnabled || !plan) return { snappedFrame: targetFrame, snappedTo: null };

      const points = new Set<number>([0, currentFrame]);
      for (const c of plan.clips ?? []) {
        if (!excludeClipIds.includes(c.clipId)) {
          points.add(c.timelineStartFrame);
          points.add(c.timelineStartFrame + c.durationFrames);
        }
      }

      let closest: number | null = null;
      let minDiff = thresholdFrames + 1;

      for (const p of points) {
        const diff = Math.abs(p - targetFrame);
        if (diff <= thresholdFrames && diff < minDiff) {
          minDiff = diff;
          closest = p;
        }
      }

      if (closest !== null) {
        return { snappedFrame: closest, snappedTo: closest };
      }
      return { snappedFrame: targetFrame, snappedTo: null };
    },
    [snappingEnabled, plan, currentFrame]
  );

  // Mover clipe (e seu par vinculado)
  const moveClip = useCallback(
    (clipId: string, targetStartFrame: number) => {
      if (!plan) return;
      const primary = plan.clips.find((c) => c.clipId === clipId);
      if (!primary) return;

      const linked = primary.linkedClipId
        ? plan.clips.find((c) => c.clipId === primary.linkedClipId)
        : null;

      const delta = targetStartFrame - primary.timelineStartFrame;
      const newPrimaryStart = Math.max(0, targetStartFrame);
      const newLinkedStart = linked ? Math.max(0, linked.timelineStartFrame + delta) : null;

      const updatedClips = plan.clips.map((c) => {
        if (c.clipId === primary.clipId) {
          return { ...c, timelineStartFrame: newPrimaryStart };
        }
        if (linked && c.clipId === linked.clipId && newLinkedStart !== null) {
          return { ...c, timelineStartFrame: newLinkedStart };
        }
        return c;
      });

      const op: PlanEditorialOperationDto = {
        operationId: crypto.randomUUID(),
        type: 'MOVE',
        clipId: primary.clipId,
        linkedClipId: linked?.clipId ?? null,
        newStartFrame: newPrimaryStart,
        timestampUtc: new Date().toISOString(),
      };

      commitPlan({ ...plan, clips: updatedClips }, op);
    },
    [plan, commitPlan]
  );

  // Aparar clipe no início ou fim (preservando limites de mídia e V1/A1 sync)
  const trimClip = useCallback(
    (clipId: string, edge: 'start' | 'end', newFrame: number) => {
      if (!plan) return;
      const primary = plan.clips.find((c) => c.clipId === clipId);
      if (!primary) return;

      const linked = primary.linkedClipId
        ? plan.clips.find((c) => c.clipId === primary.linkedClipId)
        : null;

      const tbNum = plan.timeBase.frameDurationNum;
      const tbDen = plan.timeBase.frameDurationDen;

      let newStartFrame = primary.timelineStartFrame;
      let newDuration = primary.durationFrames;
      let newSourceIn = primary.sourceInUs;
      let newSourceOut = primary.sourceOutUs;

      if (edge === 'start') {
        const clampedFrame = Math.max(0, Math.min(newFrame, primary.timelineStartFrame + primary.durationFrames - 1));
        const deltaFrames = clampedFrame - primary.timelineStartFrame;
        const deltaUs = Math.round((deltaFrames * tbNum * 1_000_000) / tbDen);

        newStartFrame = clampedFrame;
        newDuration = primary.durationFrames - deltaFrames;
        newSourceIn = Math.max(0, primary.sourceInUs + deltaUs);
      } else {
        const clampedFrame = Math.max(primary.timelineStartFrame + 1, newFrame);
        const deltaFrames = clampedFrame - (primary.timelineStartFrame + primary.durationFrames);
        const deltaUs = Math.round((deltaFrames * tbNum * 1_000_000) / tbDen);

        newDuration = clampedFrame - primary.timelineStartFrame;
        newSourceOut = primary.sourceOutUs + deltaUs;
      }

      if (newDuration <= 0 || newSourceOut <= newSourceIn) return;

      const updatedClips = plan.clips.map((c) => {
        if (c.clipId === primary.clipId || (linked && c.clipId === linked.clipId)) {
          return {
            ...c,
            timelineStartFrame: newStartFrame,
            durationFrames: newDuration,
            sourceInUs: newSourceIn,
            sourceOutUs: newSourceOut,
          };
        }
        return c;
      });

      const op: PlanEditorialOperationDto = {
        operationId: crypto.randomUUID(),
        type: 'TRIM',
        clipId: primary.clipId,
        linkedClipId: linked?.clipId ?? null,
        newStartFrame,
        newDurationFrames: newDuration,
        sourceInUs: newSourceIn,
        sourceOutUs: newSourceOut,
        timestampUtc: new Date().toISOString(),
      };

      commitPlan({ ...plan, clips: updatedClips }, op);
    },
    [plan, commitPlan]
  );

  // Dividir clipe no playhead (split)
  const splitClipAtPlayhead = useCallback(
    (clipId?: string) => {
      if (!plan) return;
      const targetId = clipId ?? selectedClipId;
      if (!targetId) return;

      const primary = plan.clips.find((c) => c.clipId === targetId);
      if (!primary) return;

      const splitFrame = currentFrame;
      if (
        splitFrame <= primary.timelineStartFrame ||
        splitFrame >= primary.timelineStartFrame + primary.durationFrames
      ) {
        return; // Playhead fora dos limites do clipe
      }

      const linked = primary.linkedClipId
        ? plan.clips.find((c) => c.clipId === primary.linkedClipId)
        : null;

      const tbNum = plan.timeBase.frameDurationNum;
      const tbDen = plan.timeBase.frameDurationDen;

      const leftDuration = splitFrame - primary.timelineStartFrame;
      const rightDuration = primary.durationFrames - leftDuration;
      const leftDeltaUs = Math.round((leftDuration * tbNum * 1_000_000) / tbDen);
      const splitSourceUs = primary.sourceInUs + leftDeltaUs;

      const newPrimaryPart2Id = crypto.randomUUID();
      const newLinkedPart2Id = linked ? crypto.randomUUID() : null;

      const updatedClips: PlanClipDto[] = [];

      for (const c of plan.clips) {
        if (c.clipId === primary.clipId) {
          // Parte 1 (esquerda)
          updatedClips.push({
            ...c,
            durationFrames: leftDuration,
            sourceOutUs: splitSourceUs,
          });
          // Parte 2 (direita)
          updatedClips.push({
            ...c,
            clipId: newPrimaryPart2Id,
            timelineStartFrame: splitFrame,
            durationFrames: rightDuration,
            sourceInUs: splitSourceUs,
            linkedClipId: newLinkedPart2Id,
          });
        } else if (linked && c.clipId === linked.clipId) {
          // Parte 1 áudio (esquerda)
          updatedClips.push({
            ...c,
            durationFrames: leftDuration,
            sourceOutUs: splitSourceUs,
          });
          // Parte 2 áudio (direita)
          updatedClips.push({
            ...c,
            clipId: newLinkedPart2Id!,
            timelineStartFrame: splitFrame,
            durationFrames: rightDuration,
            sourceInUs: splitSourceUs,
            linkedClipId: newPrimaryPart2Id,
          });
        } else {
          updatedClips.push(c);
        }
      }

      const op: PlanEditorialOperationDto = {
        operationId: crypto.randomUUID(),
        type: 'SPLIT',
        clipId: primary.clipId,
        linkedClipId: linked?.clipId ?? null,
        splitAtFrame: splitFrame,
        timestampUtc: new Date().toISOString(),
      };

      commitPlan({ ...plan, clips: updatedClips }, op);
      setSelectedClipId(newPrimaryPart2Id);
    },
    [plan, selectedClipId, currentFrame, commitPlan]
  );

  // Duplicar clipe (e seu par vinculado)
  const duplicateClip = useCallback(
    (clipId?: string) => {
      if (!plan) return;
      const targetId = clipId ?? selectedClipId;
      if (!targetId) return;

      const primary = plan.clips.find((c) => c.clipId === targetId);
      if (!primary) return;

      const linked = primary.linkedClipId
        ? plan.clips.find((c) => c.clipId === primary.linkedClipId)
        : null;

      const newStartFrame = primary.timelineStartFrame + primary.durationFrames;
      const newPrimaryId = crypto.randomUUID();
      const newLinkedId = linked ? crypto.randomUUID() : null;

      const duplicatedPrimary: PlanClipDto = {
        ...primary,
        clipId: newPrimaryId,
        timelineStartFrame: newStartFrame,
        linkedClipId: newLinkedId,
      };

      const duplicatedClips = [...plan.clips, duplicatedPrimary];
      if (linked && newLinkedId) {
        duplicatedClips.push({
          ...linked,
          clipId: newLinkedId,
          timelineStartFrame: newStartFrame,
          linkedClipId: newPrimaryId,
        });
      }

      const op: PlanEditorialOperationDto = {
        operationId: crypto.randomUUID(),
        type: 'DUPLICATE',
        clipId: primary.clipId,
        linkedClipId: linked?.clipId ?? null,
        newStartFrame,
        timestampUtc: new Date().toISOString(),
      };

      commitPlan({ ...plan, clips: duplicatedClips }, op);
      setSelectedClipId(newPrimaryId);
    },
    [plan, selectedClipId, commitPlan]
  );

  // Excluir clipe (e seu par vinculado)
  const deleteClip = useCallback(
    (clipId?: string) => {
      if (!plan) return;
      const targetId = clipId ?? selectedClipId;
      if (!targetId) return;

      const primary = plan.clips.find((c) => c.clipId === targetId);
      if (!primary) return;

      const linked = primary.linkedClipId
        ? plan.clips.find((c) => c.clipId === primary.linkedClipId)
        : null;

      const updatedClips = plan.clips.filter(
        (c) => c.clipId !== primary.clipId && (!linked || c.clipId !== linked.clipId)
      );

      const op: PlanEditorialOperationDto = {
        operationId: crypto.randomUUID(),
        type: 'DELETE',
        clipId: primary.clipId,
        linkedClipId: linked?.clipId ?? null,
        timestampUtc: new Date().toISOString(),
      };

      commitPlan({ ...plan, clips: updatedClips }, op);
      setSelectedClipId(null);
    },
    [plan, selectedClipId, commitPlan]
  );

  // Salvar revisao persistente no backend C#
  const saveRevision = useCallback(async () => {
    if (!plan || !projectId) return null;
    setIsSaving(true);
    setSaveError(null);
    try {
      const saved = await ipc.savePlanRevision(
        projectId,
        plan,
        'user',
        operationsLogRef.current
      );
      setPlan(saved);
      setHistory([saved]);
      setHistoryIndex(0);
      operationsLogRef.current = [];
      onPlanChanged?.(saved);
      return saved;
    } catch (err) {
      const msg = err instanceof Error ? err.message : String(err);
      setSaveError(msg);
      return null;
    } finally {
      setIsSaving(false);
    }
  }, [plan, projectId, onPlanChanged]);

  // Reconciliar plano manual com nova revisao automatica (F14.1)
  const reconcile = useCallback(async () => {
    if (!projectId) return null;
    try {
      const res = await ipc.reconcilePlan(projectId);
      if (res.reconciledPlan) {
        commitPlan(res.reconciledPlan);
        setConflicts(res.conflicts ?? []);
      }
      return res;
    } catch (err) {
      const msg = err instanceof Error ? err.message : String(err);
      setSaveError(`Falha ao reconciliar: ${msg}`);
      return null;
    }
  }, [projectId, commitPlan]);

  // Atalhos de teclado (Ctrl+Z, Ctrl+Y, S, Delete, Backspace, Ctrl+D)
  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      // Evita atalhos dentro de inputs/textareas
      if (['INPUT', 'TEXTAREA'].includes((e.target as HTMLElement)?.tagName)) return;

      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'z') {
        if (e.shiftKey) {
          e.preventDefault();
          redo();
        } else {
          e.preventDefault();
          undo();
        }
      } else if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'y') {
        e.preventDefault();
        redo();
      } else if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'd') {
        e.preventDefault();
        duplicateClip();
      } else if (e.key === 'Delete' || e.key === 'Backspace') {
        if (selectedClipId) {
          e.preventDefault();
          deleteClip();
        }
      } else if (e.key.toLowerCase() === 's') {
        if (selectedClipId) {
          e.preventDefault();
          splitClipAtPlayhead();
        }
      }
    };

    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [undo, redo, duplicateClip, deleteClip, splitClipAtPlayhead, selectedClipId]);

  return {
    plan,
    canUndo,
    canRedo,
    isDirty,
    isSaving,
    saveError,
    conflicts,
    selectedClipId,
    snappingEnabled,
    snapGuide,
    setSelectedClipId,
    setSnappingEnabled,
    setSnapGuide,
    calculateSnap,
    moveClip,
    trimClip,
    splitClipAtPlayhead,
    duplicateClip,
    deleteClip,
    undo,
    redo,
    saveRevision,
    reconcile,
  };
}
