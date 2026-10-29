import React, { useState, useMemo, useRef, useEffect } from 'react';
import {
  FileText,
  Search,
  Scissors,
  Check,
  RotateCcw,
  Sparkles,
  Volume2,
  Clock,
  User,
  AlertCircle
} from 'lucide-react';
import { useEditingWorkspace } from '../context/EditingContext';
import { UnifiedWord, UnifiedTranscriptSegment } from '../types/editingTypes';
import { secondsToTimecode } from '../utils/davinciExporter';

export const IntegratedTranscript: React.FC = () => {
  const {
    selectedAsset,
    currentTimeSec,
    activeWordId,
    onSelectWord,
    acceptCut,
    rejectCut,
    allCutSuggestions,
    acceptAllPendingCuts
  } = useEditingWorkspace();

  const [searchQuery, setSearchQuery] = useState('');
  const [autoScroll, setAutoScroll] = useState(true);
  const activeWordRef = useRef<HTMLSpanElement | null>(null);

  // Filter segments based on search
  const filteredSegments = useMemo(() => {
    if (!searchQuery.trim()) return selectedAsset.transcription;
    const query = searchQuery.toLowerCase();
    return selectedAsset.transcription.filter(
      (seg) =>
        seg.text.toLowerCase().includes(query) ||
        seg.speaker.toLowerCase().includes(query)
    );
  }, [selectedAsset.transcription, searchQuery]);

  // Auto-scroll to active word during playback
  useEffect(() => {
    if (autoScroll && activeWordRef.current) {
      activeWordRef.current.scrollIntoView({
        behavior: 'smooth',
        block: 'nearest',
        inline: 'nearest'
      });
    }
  }, [activeWordId, autoScroll]);

  // Aggregate pending cuts for the current asset
  const pendingCutsInAsset = useMemo(() => {
    return selectedAsset.cutSuggestions.filter((c) => c.decisionState === 'pending');
  }, [selectedAsset.cutSuggestions]);

  return (
    <div className="w-80 md:w-96 border-l border-studio-border bg-studio-panel flex flex-col shrink-0 select-none h-full">
      {/* Transcript Header */}
      <div className="p-3 border-b border-studio-border bg-studio-card/80 space-y-2.5">
        <div className="flex items-center justify-between">
          <div className="flex items-center space-x-2">
            <FileText className="w-4 h-4 text-indigo-400" />
            <span className="text-xs font-bold text-studio-text tracking-wide uppercase">
              Transcrição Sincronizada
            </span>
          </div>

          <button
            onClick={() => setAutoScroll(!autoScroll)}
            className={`px-2 py-0.5 rounded text-[10px] font-mono transition-colors ${
              autoScroll
                ? 'bg-indigo-600/30 text-indigo-300 border border-indigo-500/50'
                : 'bg-studio-panel text-studio-muted border border-studio-border'
            }`}
          >
            Auto-scroll {autoScroll ? 'ON' : 'OFF'}
          </button>
        </div>

        {/* Search Input */}
        <div className="relative">
          <Search className="w-3.5 h-3.5 text-studio-subtle absolute left-2.5 top-2.5" />
          <input
            type="text"
            placeholder="Buscar fala, palavra ou falante..."
            value={searchQuery}
            onChange={(e) => setSearchQuery(e.target.value)}
            className="w-full bg-studio-panel border border-studio-border rounded-lg pl-8 pr-3 py-1.5 text-xs text-studio-text focus:outline-none focus:border-indigo-500"
          />
        </div>

        {/* Pending Cuts Batch Action Card */}
        {pendingCutsInAsset.length > 0 && (
          <div className="bg-amber-950/40 border border-amber-500/30 p-2 rounded-lg flex items-center justify-between">
            <div className="flex items-center space-x-2">
              <Sparkles className="w-3.5 h-3.5 text-amber-400 animate-pulse" />
              <span className="text-[11px] text-amber-200">
                {pendingCutsInAsset.length} corte{pendingCutsInAsset.length > 1 ? 's' : ''} sugerido{pendingCutsInAsset.length > 1 ? 's' : ''}
              </span>
            </div>
            <button
              onClick={acceptAllPendingCuts}
              className="px-2 py-1 bg-amber-600 hover:bg-amber-500 text-white rounded text-[10px] font-semibold flex items-center space-x-1"
            >
              <Check className="w-3 h-3" />
              <span>Aprovar Todos</span>
            </button>
          </div>
        )}
      </div>

      {/* Segments & Words List */}
      <div className="flex-1 overflow-y-auto p-3 space-y-4 custom-scrollbar">
        {filteredSegments.length === 0 ? (
          <div className="py-12 text-center text-studio-muted text-xs flex flex-col items-center justify-center space-y-2">
            <AlertCircle className="w-6 h-6 text-studio-subtle" />
            <span>Nenhuma transcrição encontrada para este clipe.</span>
          </div>
        ) : (
          filteredSegments.map((segment) => {
            const hasCutCandidate = segment.words.some((w) => w.isCutCandidate);
            const associatedCut = selectedAsset.cutSuggestions.find(
              (c) => c.startSec >= segment.startSec && c.endSec <= segment.endSec + 1
            );

            return (
              <div
                key={segment.id}
                className={`p-3 rounded-xl border transition-all ${
                  hasCutCandidate
                    ? 'bg-amber-950/20 border-amber-500/30'
                    : 'bg-studio-card/60 border-studio-border hover:border-studio-subtle'
                }`}
              >
                {/* Segment Meta */}
                <div className="flex items-center justify-between mb-2">
                  <div className="flex items-center space-x-2">
                    <span className="w-5 h-5 rounded-full bg-indigo-600/30 border border-indigo-500/50 flex items-center justify-center text-[10px] font-bold text-indigo-300">
                      {segment.speaker[0]}
                    </span>
                    <span className="text-xs font-semibold text-studio-text">
                      {segment.speaker}
                    </span>
                  </div>

                  <span className="text-[10px] font-mono text-studio-subtle flex items-center space-x-1">
                    <Clock className="w-3 h-3 text-studio-subtle" />
                    <span>{secondsToTimecode(segment.startSec, selectedAsset.frameRate)}</span>
                  </span>
                </div>

                {/* Inline Cut Card if associated with this segment */}
                {associatedCut && (
                  <div className={`mb-2.5 p-2 rounded-lg border text-xs flex items-center justify-between ${
                    associatedCut.decisionState === 'accepted'
                      ? 'bg-red-950/30 border-red-500/30 text-red-200'
                      : associatedCut.decisionState === 'rejected'
                      ? 'bg-zinc-900 border-zinc-700 text-zinc-400'
                      : 'bg-amber-900/30 border-amber-500/40 text-amber-200'
                  }`}>
                    <div className="flex items-center space-x-2">
                      <Scissors className="w-3.5 h-3.5 text-amber-400" />
                      <div>
                        <div className="font-semibold text-[11px]">{associatedCut.reasonLabel}</div>
                        <div className="text-[10px] opacity-75 font-mono">
                          {associatedCut.durationSec}s economizados
                        </div>
                      </div>
                    </div>

                    <div className="flex items-center space-x-1.5">
                      {associatedCut.decisionState === 'accepted' ? (
                        <button
                          onClick={() => rejectCut(associatedCut.id)}
                          className="px-2 py-0.5 bg-zinc-800 hover:bg-zinc-700 text-zinc-300 rounded text-[10px] flex items-center space-x-1 border border-zinc-600"
                        >
                          <RotateCcw className="w-2.5 h-2.5" />
                          <span>Reverter</span>
                        </button>
                      ) : (
                        <>
                          <button
                            onClick={() => acceptCut(associatedCut.id)}
                            className="px-2 py-0.5 bg-emerald-600 hover:bg-emerald-500 text-white rounded text-[10px] font-semibold"
                          >
                            Cortar
                          </button>
                          <button
                            onClick={() => rejectCut(associatedCut.id)}
                            className="px-1.5 py-0.5 bg-zinc-800 hover:bg-zinc-700 text-zinc-400 rounded text-[10px]"
                          >
                            Manter
                          </button>
                        </>
                      )}
                    </div>
                  </div>
                )}

                {/* Words Flow with Interactive Highlighting & Click-to-Seek */}
                <p className="text-xs leading-relaxed text-studio-muted">
                  {segment.words.map((word) => {
                    const isActive = activeWordId === word.id;
                    const isCutAccepted = word.isCutAccepted;

                    return (
                      <span
                        key={word.id}
                        ref={isActive ? activeWordRef : null}
                        onClick={() => onSelectWord(word, selectedAsset.id)}
                        className={`inline-block px-1 py-0.5 mx-0.5 rounded cursor-pointer transition-all ${
                          isCutAccepted
                            ? 'line-through text-red-400/80 bg-red-950/40 border border-red-500/30'
                            : isActive
                            ? 'bg-indigo-600 text-white font-bold scale-105 shadow-glow-indigo'
                            : word.isCutCandidate
                            ? 'bg-amber-400/20 text-amber-200 border-b border-amber-400 hover:bg-amber-400/30'
                            : 'hover:bg-studio-panel hover:text-white'
                        }`}
                        title={`${word.text} (${word.startSec}s - ${word.endSec}s)`}
                      >
                        {word.text}
                      </span>
                    );
                  })}
                </p>
              </div>
            );
          })
        )}
      </div>
    </div>
  );
};
