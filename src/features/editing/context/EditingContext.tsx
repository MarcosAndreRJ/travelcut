import React, { createContext, useContext, useState, useMemo, useCallback } from 'react';
import {
  UnifiedMediaAsset,
  UnifiedCutSuggestion,
  UnifiedTimelineClip,
  UnifiedWord,
  ViewModeAB,
  EditHistoryEntry,
  DaVinciExportData
} from '../types/editingTypes';
import {
  INITIAL_UNIFIED_ASSETS,
  INITIAL_TIMELINE_SEQUENCE
} from '../data/unifiedProjectData';
import { computeExportData } from '../utils/davinciExporter';

interface EditingContextValue {
  // Project & Assets
  projectName: string;
  setProjectName: (name: string) => void;
  mediaAssets: UnifiedMediaAsset[];
  selectedAssetId: string;
  selectedAsset: UnifiedMediaAsset;
  selectAssetById: (id: string) => void;

  // Global NLE Playhead & Transport
  currentTimeSec: number;
  seekToTime: (timeSec: number, triggerAutoScroll?: boolean) => void;
  isPlaying: boolean;
  togglePlay: () => void;
  setIsPlaying: (playing: boolean) => void;
  playbackSpeed: number;
  setPlaybackSpeed: (speed: number) => void;

  // Bidirectional Synchronization State
  activeWordId: string | null;
  activeCutId: string | null;
  selectedTimelineClipId: string | null;
  onSelectWord: (word: UnifiedWord, mediaId: string) => void;
  onSelectTimelineClip: (clipId: string) => void;

  // Cut Suggestions & Decisions (Single Source of Truth)
  allCutSuggestions: UnifiedCutSuggestion[];
  acceptCut: (cutId: string) => void;
  rejectCut: (cutId: string) => void;
  resetAllCuts: () => void;
  acceptAllPendingCuts: () => void;

  // Timeline Sequence
  timelineClips: UnifiedTimelineClip[];
  totalSequenceDurationSec: number;

  // View Modes (A/B Comparison)
  viewModeAB: ViewModeAB;
  setViewModeAB: (mode: ViewModeAB) => void;

  // Edit History & Audit Log
  history: EditHistoryEntry[];

  // DaVinci Export
  exportData: DaVinciExportData;
}

const EditingContext = createContext<EditingContextValue | null>(null);

export const EditingProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const [projectName, setProjectName] = useState<string>('Expedição Monte Roraima 2026');
  const [mediaAssets, setMediaAssets] = useState<UnifiedMediaAsset[]>(INITIAL_UNIFIED_ASSETS);
  const [selectedAssetId, setSelectedAssetId] = useState<string>(INITIAL_UNIFIED_ASSETS[0].id);

  // Playback state
  const [currentTimeSec, setCurrentTimeSec] = useState<number>(18.0);
  const [isPlaying, setIsPlaying] = useState<boolean>(false);
  const [playbackSpeed, setPlaybackSpeed] = useState<number>(1);

  // Active sync targets
  const [activeWordId, setActiveWordId] = useState<string | null>('w-10');
  const [activeCutId, setActiveCutId] = useState<string | null>('cut-1');
  const [selectedTimelineClipId, setSelectedTimelineClipId] = useState<string | null>('tl-1');

  // View Mode
  const [viewModeAB, setViewModeAB] = useState<ViewModeAB>('after_cuts');

  // History log
  const [history, setHistory] = useState<EditHistoryEntry[]>([
    {
      id: 'hist-init',
      timestamp: new Date().toLocaleTimeString(),
      actionType: 'select_clip',
      description: 'Projeto inicializado com dados unificados do Monte Roraima',
    }
  ]);

  const selectedAsset = useMemo(() => {
    return mediaAssets.find((a) => a.id === selectedAssetId) || mediaAssets[0];
  }, [mediaAssets, selectedAssetId]);

  // Aggregate all cuts across all media assets
  const allCutSuggestions = useMemo(() => {
    return mediaAssets.flatMap((asset) => asset.cutSuggestions);
  }, [mediaAssets]);

  // Derived timeline clips reflecting accepted cuts
  const timelineClips = useMemo(() => {
    if (viewModeAB === 'before_cuts') {
      // Raw view: original timeline clips without cut truncation
      return INITIAL_TIMELINE_SEQUENCE.map((c) => ({
        ...c,
        isExcludedFromCut: false
      }));
    }

    // Process accepted cuts: adjust durations and mark cut slices
    return INITIAL_TIMELINE_SEQUENCE.map((clip) => {
      const asset = mediaAssets.find((a) => a.id === clip.mediaClipId);
      if (!asset) return clip;

      const acceptedCutsForClip = asset.cutSuggestions.filter(
        (c) => c.decisionState === 'accepted' && c.startSec >= clip.sourceInSec && c.endSec <= clip.sourceOutSec
      );

      const totalCutSec = acceptedCutsForClip.reduce((acc, c) => acc + c.durationSec, 0);

      if (totalCutSec > 0) {
        return {
          ...clip,
          durationSec: Math.max(1, clip.durationSec - totalCutSec),
          isCutSlice: true,
          cutReason: acceptedCutsForClip[0]?.reason,
        };
      }

      return clip;
    });
  }, [mediaAssets, viewModeAB]);

  // Total sequence duration in seconds
  const totalSequenceDurationSec = useMemo(() => {
    return timelineClips.reduce(
      (max, c) => Math.max(max, c.timelineStartSec + c.durationSec),
      61.6
    );
  }, [timelineClips]);

  // DaVinci export bundle
  const exportData = useMemo(() => {
    return computeExportData(projectName, timelineClips, mediaAssets, allCutSuggestions, 60);
  }, [projectName, timelineClips, mediaAssets, allCutSuggestions]);

  // Action: Seek playhead
  const seekToTime = useCallback((timeSec: number) => {
    const clamped = Math.max(0, timeSec);
    setCurrentTimeSec(clamped);

    // Auto-identify active word based on currentTimeSec
    const currentSegment = selectedAsset.transcription.find(
      (seg) => clamped >= seg.startSec && clamped <= seg.endSec
    );
    if (currentSegment) {
      const currentWord = currentSegment.words.find(
        (w) => clamped >= w.startSec && clamped <= w.endSec
      );
      if (currentWord) {
        setActiveWordId(currentWord.id);
      }
    }
  }, [selectedAsset]);

  // Action: Toggle Play/Pause
  const togglePlay = useCallback(() => {
    setIsPlaying((prev) => !prev);
  }, []);

  // Action: Select Asset
  const selectAssetById = useCallback((id: string) => {
    setSelectedAssetId(id);
    const targetAsset = mediaAssets.find((a) => a.id === id);
    if (targetAsset) {
      // Find matching timeline clip
      const matchingTlClip = timelineClips.find((c) => c.mediaClipId === id);
      if (matchingTlClip) {
        setSelectedTimelineClipId(matchingTlClip.id);
        setCurrentTimeSec(matchingTlClip.sourceInSec);
      } else {
        setCurrentTimeSec(0);
      }
    }
  }, [mediaAssets, timelineClips]);

  // Action: Select Word from Transcript
  const onSelectWord = useCallback((word: UnifiedWord, mediaId: string) => {
    setActiveWordId(word.id);
    if (mediaId !== selectedAssetId) {
      setSelectedAssetId(mediaId);
    }
    setCurrentTimeSec(word.startSec);
    setIsPlaying(false);

    // Focus corresponding timeline clip
    const matchingTlClip = timelineClips.find((c) => c.mediaClipId === mediaId);
    if (matchingTlClip) {
      setSelectedTimelineClipId(matchingTlClip.id);
    }

    if (word.cutId) {
      setActiveCutId(word.cutId);
    }
  }, [selectedAssetId, timelineClips]);

  // Action: Select Timeline Clip
  const onSelectTimelineClip = useCallback((clipId: string) => {
    setSelectedTimelineClipId(clipId);
    const clip = timelineClips.find((c) => c.id === clipId);
    if (clip) {
      if (clip.mediaClipId !== selectedAssetId) {
        setSelectedAssetId(clip.mediaClipId);
      }
      setCurrentTimeSec(clip.sourceInSec);
    }
  }, [timelineClips, selectedAssetId]);

  // Action: Accept Cut
  const acceptCut = useCallback((cutId: string) => {
    setMediaAssets((prevAssets) =>
      prevAssets.map((asset) => {
        const hasCut = asset.cutSuggestions.some((c) => c.id === cutId);
        if (!hasCut) return asset;

        const updatedCuts = asset.cutSuggestions.map((c) =>
          c.id === cutId ? { ...c, decisionState: 'accepted' as const, appliedAtTimestamp: new Date().toLocaleTimeString() } : c
        );

        // Update words in transcription to mark as accepted cut
        const updatedTranscription = asset.transcription.map((seg) => ({
          ...seg,
          words: seg.words.map((w) =>
            w.cutId === cutId ? { ...w, isCutAccepted: true } : w
          )
        }));

        return {
          ...asset,
          cutSuggestions: updatedCuts,
          transcription: updatedTranscription,
        };
      })
    );

    const cut = allCutSuggestions.find((c) => c.id === cutId);
    setHistory((prev) => [
      {
        id: `hist-${Date.now()}`,
        timestamp: new Date().toLocaleTimeString(),
        actionType: 'accept_cut',
        description: `Corte aceito: "${cut?.previewText || cutId}" (${cut?.reasonLabel || 'IA'})`,
        affectedCutId: cutId,
      },
      ...prev
    ]);
  }, [allCutSuggestions]);

  // Action: Reject Cut
  const rejectCut = useCallback((cutId: string) => {
    setMediaAssets((prevAssets) =>
      prevAssets.map((asset) => {
        const hasCut = asset.cutSuggestions.some((c) => c.id === cutId);
        if (!hasCut) return asset;

        const updatedCuts = asset.cutSuggestions.map((c) =>
          c.id === cutId ? { ...c, decisionState: 'rejected' as const } : c
        );

        // Update words in transcription to unmark cut
        const updatedTranscription = asset.transcription.map((seg) => ({
          ...seg,
          words: seg.words.map((w) =>
            w.cutId === cutId ? { ...w, isCutAccepted: false } : w
          )
        }));

        return {
          ...asset,
          cutSuggestions: updatedCuts,
          transcription: updatedTranscription,
        };
      })
    );

    const cut = allCutSuggestions.find((c) => c.id === cutId);
    setHistory((prev) => [
      {
        id: `hist-${Date.now()}`,
        timestamp: new Date().toLocaleTimeString(),
        actionType: 'reject_cut',
        description: `Corte descartado/revertido: "${cut?.previewText || cutId}"`,
        affectedCutId: cutId,
      },
      ...prev
    ]);
  }, [allCutSuggestions]);

  // Action: Accept All Pending Cuts
  const acceptAllPendingCuts = useCallback(() => {
    setMediaAssets((prevAssets) =>
      prevAssets.map((asset) => ({
        ...asset,
        cutSuggestions: asset.cutSuggestions.map((c) =>
          c.decisionState === 'pending' ? { ...c, decisionState: 'accepted' as const, appliedAtTimestamp: new Date().toLocaleTimeString() } : c
        ),
        transcription: asset.transcription.map((seg) => ({
          ...seg,
          words: seg.words.map((w) =>
            w.isCutCandidate ? { ...w, isCutAccepted: true } : w
          )
        }))
      }))
    );

    setHistory((prev) => [
      {
        id: `hist-${Date.now()}`,
        timestamp: new Date().toLocaleTimeString(),
        actionType: 'accept_cut',
        description: 'Todos os cortes pendentes de IA foram aceitos em lote',
      },
      ...prev
    ]);
  }, []);

  // Action: Reset All Cuts
  const resetAllCuts = useCallback(() => {
    setMediaAssets((prevAssets) =>
      prevAssets.map((asset) => ({
        ...asset,
        cutSuggestions: asset.cutSuggestions.map((c) => ({
          ...c,
          decisionState: 'pending' as const,
        })),
        transcription: asset.transcription.map((seg) => ({
          ...seg,
          words: seg.words.map((w) => ({
            ...w,
            isCutAccepted: false,
          }))
        }))
      }))
    );

    setHistory((prev) => [
      {
        id: `hist-${Date.now()}`,
        timestamp: new Date().toLocaleTimeString(),
        actionType: 'reset_cuts',
        description: 'Decisões de corte resetadas para estado pendente',
      },
      ...prev
    ]);
  }, []);

  return (
    <EditingContext.Provider
      value={{
        projectName,
        setProjectName,
        mediaAssets,
        selectedAssetId,
        selectedAsset,
        selectAssetById,
        currentTimeSec,
        seekToTime,
        isPlaying,
        togglePlay,
        setIsPlaying,
        playbackSpeed,
        setPlaybackSpeed,
        activeWordId,
        activeCutId,
        selectedTimelineClipId,
        onSelectWord,
        onSelectTimelineClip,
        allCutSuggestions,
        acceptCut,
        rejectCut,
        resetAllCuts,
        acceptAllPendingCuts,
        timelineClips,
        totalSequenceDurationSec,
        viewModeAB,
        setViewModeAB,
        history,
        exportData
      }}
    >
      {children}
    </EditingContext.Provider>
  );
};

export const useEditingWorkspace = (): EditingContextValue => {
  const context = useContext(EditingContext);
  if (!context) {
    throw new Error('useEditingWorkspace must be used within an EditingProvider');
  }
  return context;
};
