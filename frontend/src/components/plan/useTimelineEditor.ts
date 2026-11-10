import { useCallback, useEffect, useRef, useState } from 'react';
import type { EditPlanDto, PlanClipDto, PlanConflictDto, PlanEditorialOperationDto } from '../../types/ipc';
import { IpcError, ipc } from '../../services/ipcBridge';
import {
  EMPTY_HISTORY,
  classifySaveError,
  type SaveErrorInfo,
  canRedoHistory,
  canUndoHistory,
  createHistory,
  currentPlan,
  decideAcceptPayload,
  decideIncoming,
  isBaseOutdated,
  isHistoryDirty,
  markHistorySaved,
  reapplyOperations,
  resolveLatestRevision,
  operationsSinceSaved,
  pushHistory,
  redoHistory,
  undoHistory,
  withTimelineDuration,
  type PlanRef,
  type TimelineHistory
} from './timelineEditorState';

export interface SnapResult {
  snappedFrame: number;
  snappedTo: number | null;
}

export interface UseTimelineEditorOptions {
  initialPlan: EditPlanDto | null;
  projectId: string;
  currentFrame?: number;
  onPlanChanged?: (plan: EditPlanDto) => void;
  /** Revisão mais nova da lista do host (planState.latestRevision). */
  latestRevision?: number | null;
}

export function useTimelineEditor({
  initialPlan,
  projectId,
  currentFrame = 0,
  onPlanChanged,
  latestRevision = null,
}: UseTimelineEditorOptions) {
  const [hist, setHist] = useState<TimelineHistory>(() => (initialPlan ? createHistory(initialPlan) : EMPTY_HISTORY));
  const histRef = useRef(hist);
  histRef.current = hist;
  // Revisão sobre a qual o editor está baseado (base do próximo save).
  const baseRef = useRef<PlanRef | null>(initialPlan ? { planId: initialPlan.planId, revision: initialPlan.revision } : null);
  const [base, setBase] = useState<PlanRef | null>(baseRef.current);
  // Maior revisão observada (vigente conhecida pelo editor).
  const [knownLatest, setKnownLatest] = useState<number | null>(initialPlan?.revision ?? null);
  // Revisão nova chegada de fora enquanto há edições não salvas (aguarda decisão do usuário).
  const [external, setExternal] = useState<PlanRef | null>(null);
  const initialPlanRef = useRef(initialPlan);
  initialPlanRef.current = initialPlan;
  const plan = currentPlan(hist);
  const [selectedClipId, setSelectedClipId] = useState<string | null>(null);
  const [snappingEnabled, setSnappingEnabled] = useState(true);
  const [snapGuide, setSnapGuide] = useState<number | null>(null);
  const [isSaving, setIsSaving] = useState(false);
  const [saveError, setSaveError] = useState<SaveErrorInfo | null>(null);
  const [conflicts, setConflicts] = useState<PlanConflictDto[]>([]);
  const [reconcilePreview, setReconcilePreview] = useState<{ plan: EditPlanDto; conflicts: PlanConflictDto[] } | null>(null);
  // Operações do usuário que não couberam na revisão nova após "Recarregar" (aviso ao usuário).
  const [reapplyDropped, setReapplyDropped] = useState(0);

  const updateBase = useCallback((next: PlanRef) => {
    baseRef.current = next;
    setBase(next);
  }, []);

  // Revisão vinda de fora (reabertura, nova revisão). Sem edições: adota. Com edições: preserva e avisa.
  useEffect(() => {
    if (!initialPlan) return;
    const incoming = { planId: initialPlan.planId, revision: initialPlan.revision };
    setKnownLatest((prev) => (prev === null ? incoming.revision : Math.max(prev, incoming.revision)));
    const decision = decideIncoming(baseRef.current, incoming, isHistoryDirty(histRef.current), 'external');
    if (decision === 'adopt') {
      updateBase(incoming);
      setExternal(null);
      setHist(createHistory(initialPlan));
    } else if (decision === 'banner') {
      setExternal(incoming);
    }
  }, [initialPlan?.planId, initialPlan?.revision, updateBase]);

  // "Recarregar" (após erro de base): busca a vigente. Mesmo plano: rebase leve; sem edições: adota.
  const reloadLatest = useCallback(async () => {
    if (!projectId) return;
    try {
      const latest = await ipc.getEditPlan(projectId, null);
      if (!latest) return;
      const incoming = { planId: latest.planId, revision: latest.revision };
      setKnownLatest((prev) => (prev === null ? incoming.revision : Math.max(prev, incoming.revision)));
      const decision = decideIncoming(baseRef.current, incoming, isHistoryDirty(histRef.current), 'reload');
      if (decision === 'rebase') {
        // Nunca reaproveita o snapshot antigo: reaplica só as operações do usuário sobre a revisão nova.
        const reapplied = reapplyOperations(latest, operationsSinceSaved(histRef.current));
        updateBase(incoming);
        setExternal(null);
        setHist(reapplied.history);
        setReapplyDropped(reapplied.dropped.length);
      } else if (decision === 'adopt') {
        updateBase(incoming);
        setExternal(null);
        setHist(createHistory(latest));
      } else if (decision === 'banner') {
        setExternal(incoming);
      }
      setSaveError(null);
    } catch (err) {
      const msg = err instanceof Error ? err.message : String(err);
      setSaveError({ code: null, message: `Não foi possível recarregar a revisão vigente: ${msg}`, reloadable: true });
    }
  }, [projectId, updateBase]);

  const dismissSaveError = useCallback(() => setSaveError(null), []);

  // "Manter minhas edições": preserva o histórico; a base continua antiga (marcada como desatualizada).
  const keepLocalEdits = useCallback(() => setExternal(null), []);

  // "Carregar a nova revisão": descarta as edições locais e adota a revisão recebida.
  const loadExternalRevision = useCallback(() => {
    const next = initialPlanRef.current;
    if (!next) return;
    updateBase({ planId: next.planId, revision: next.revision });
    setExternal(null);
    setHist(createHistory(next));
    setReapplyDropped(0);
  }, [updateBase]);

  const dismissReapplyNotice = useCallback(() => setReapplyDropped(0), []);

  const canUndo = canUndoHistory(hist);
  const canRedo = canRedoHistory(hist);
  const isDirty = isHistoryDirty(hist);
  // Edição só sobre a revisão vigente: a base precisa ser a mais nova conhecida.
  const externalRevision = external?.revision ?? null;
  // Maior revisão conhecida: editor, lista do host e revisão recebida de fora.
  const latestKnown = resolveLatestRevision(knownLatest, latestRevision, externalRevision);
  const baseOutdated = isBaseOutdated(base, latestKnown);
  // Edição só sobre a revisão vigente.
  const editable = base !== null && !baseOutdated;
  const editableRef = useRef(editable);
  editableRef.current = editable;

  const commitPlan = useCallback(
    (newPlan: EditPlanDto, op?: PlanEditorialOperationDto) => {
      if (!editableRef.current) return;
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

      setHist((h) => pushHistory(h, updatedPlan, op ?? null));
    },
    []
  );

  // Undo/redo só movem o ponteiro do histórico; a gravação continua explícita (Salvar).
  const undo = useCallback(() => {
    setHist((h) => undoHistory(h));
  }, []);

  const redo = useCallback(() => {
    setHist((h) => redoHistory(h));
  }, []);

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
    const snapshot = histRef.current;
    const sent = currentPlan(snapshot);
    const base = baseRef.current;
    if (!sent || !projectId || !base) return null;
    const idx = snapshot.index;
    setIsSaving(true);
    setSaveError(null);
    try {
      const saved = await ipc.savePlanRevision(
        projectId,
        sent,
        base.revision,
        'user',
        operationsSinceSaved(snapshot)
      );
      // Mantém o histórico: a entrada salva passa a ser a revisão devolvida pelo host.
      setHist((h) => markHistorySaved(h, idx, sent, saved));
      updateBase({ planId: saved.planId, revision: saved.revision });
      setKnownLatest((prev) => (prev === null ? saved.revision : Math.max(prev, saved.revision)));
      onPlanChanged?.(saved);
      return saved;
    } catch (err) {
      const msg = err instanceof Error ? err.message : String(err);
      setSaveError(classifySaveError(err instanceof IpcError ? err.code : null, msg));
      return null;
    } finally {
      setIsSaving(false);
    }
  }, [projectId, onPlanChanged, updateBase]);

  // Reconciliar plano manual com nova revisao automatica (F14.1)
  // Prévia de reconciliação: não entra no histórico, não é "salva" e não grava revisão.
  const previewReconcile = useCallback(async () => {
    if (!projectId) return null;
    try {
      const res = await ipc.reconcilePlan(projectId);
      if (res.reconciledPlan) {
        setReconcilePreview({ plan: res.reconciledPlan, conflicts: res.conflicts ?? [] });
      }
      return res;
    } catch (err) {
      const msg = err instanceof Error ? err.message : String(err);
      setSaveError({ code: null, message: `Falha ao reconciliar: ${msg}`, reloadable: false });
      return null;
    }
  }, [projectId]);

  const discardReconcile = useCallback(() => setReconcilePreview(null), []);

  // Aceitar: plan.generate com o perfil da base grava UMA revisão nova e o editor adota essa revisão.
  const acceptReconcile = useCallback(async () => {
    if (!projectId) return null;
    const decision = decideAcceptPayload({
      preview: reconcilePreview?.plan ?? null,
      base: baseRef.current,
      latestKnown: latestKnown,
      dirty: isHistoryDirty(histRef.current),
    });
    if (decision.kind === 'blocked') {
      if (decision.reason === 'dirty') {
        setSaveError({ code: null, message: 'Salve ou descarte as edições antes de aceitar a reconciliação.', reloadable: false });
      }
      return null;
    }
    // Grava o plano exibido na prévia (não um plano gerado de novo), com baseRevision = revisão vigente.
    setIsSaving(true);
    setSaveError(null);
    try {
      const saved = await ipc.savePlanRevision(projectId, decision.plan, decision.baseRevision, 'user', []);
      updateBase({ planId: saved.planId, revision: saved.revision });
      setKnownLatest((prev) => (prev === null ? saved.revision : Math.max(prev, saved.revision)));
      setExternal(null);
      setHist(createHistory(saved));
      setConflicts(reconcilePreview?.conflicts ?? []);
      setReconcilePreview(null);
      setReapplyDropped(0);
      onPlanChanged?.(saved);
      return saved;
    } catch (err) {
      const msg = err instanceof Error ? err.message : String(err);
      setSaveError(classifySaveError(err instanceof IpcError ? err.code : null, `Falha ao aceitar a reconciliação: ${msg}`));
      return null;
    } finally {
      setIsSaving(false);
    }
  }, [projectId, reconcilePreview, latestKnown, onPlanChanged, updateBase]);

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
    externalRevision,
    baseRevision: base?.revision ?? null,
    baseOutdated,
    latestKnownRevision: latestKnown,
    keepLocalEdits,
    loadExternalRevision,
    reloadLatest,
    dismissSaveError,
    saveError,
    reapplyDropped,
    dismissReapplyNotice,
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
    editable,
    reconcilePreview,
    previewReconcile,
    acceptReconcile,
    discardReconcile,
  };
}
