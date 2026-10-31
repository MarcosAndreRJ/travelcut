import React, { useState, useEffect, useMemo, useRef } from 'react';
import {
  MessageSquare,
  Search,
  Sparkles,
  Scissors,
  Check,
  X,
  Volume2,
  Film,
  Info,
  Loader2,
  RotateCcw,
  Download,
  AlertCircle,
  Clock,
  FileText
} from 'lucide-react';
import { LibraryClip, InspectorTab, TranscriptLine, TranscriptWord } from '../types';
import {
  runTranscription,
  extractAudioForTranscription,
  saveTranscription,
  loadTranscription,
  onTranscriptionProgress,
  TranscriptionProgressPayload
} from '../../../services/desktopBridge';
import { useProject } from '../../../contexts/ProjectContext';

interface TranscriptInspectorProps {
  clip: LibraryClip;
  currentTimeSec: number;
  onSeek: (timeSec: number) => void;
  onAcceptCut?: (cutLine: TranscriptLine) => void;
  onRejectCut?: (cutLine: TranscriptLine) => void;
}

export const TranscriptInspector: React.FC<TranscriptInspectorProps> = ({
  clip,
  currentTimeSec,
  onSeek,
  onAcceptCut,
  onRejectCut
}) => {
  const [activeTab, setActiveTab] = useState<InspectorTab>('transcription');
  const [searchQuery, setSearchQuery] = useState('');
  const [language, setLanguage] = useState<'pt' | 'en' | 'es' | 'auto'>('pt');
  const [modelSize, setModelSize] = useState<'tiny' | 'base' | 'small' | 'medium' | 'large-v3'>('small');
  const [acceptedCuts, setAcceptedCuts] = useState<Record<string, boolean>>({});

  // Whisper Transcription state
  const [isTranscribing, setIsTranscribing] = useState(false);
  const [transcriptionProgress, setTranscriptionProgress] = useState<TranscriptionProgressPayload | null>(null);
  const [transcriptionError, setTranscriptionError] = useState<string | null>(null);
  const [liveTranscriptLines, setLiveTranscriptLines] = useState<TranscriptLine[]>(clip.transcript || []);
  const [cutFilter, setCutFilter] = useState<'all' | 'cuts_only' | 'speech_only'>('all');

  const { project } = useProject();
  const transcriptContainerRef = useRef<HTMLDivElement>(null);

  // Sync internal transcript if clip changes
  useEffect(() => {
    setLiveTranscriptLines(clip.transcript || []);
    // Try to load any previously saved transcription for this asset
    if (project?.folderPath) {
      loadTranscription(project.folderPath, clip.id).then((saved) => {
        if (saved && saved.segments && saved.segments.length > 0) {
          const loadedLines: TranscriptLine[] = saved.segments.map((s, idx) => ({
            id: s.id || `seg_${idx}`,
            timecode: `${Math.floor(s.startSec / 60)}:${(Math.floor(s.startSec % 60)).toString().padStart(2, '0')}`,
            seconds: s.startSec,
            endSeconds: s.endSec,
            text: s.text,
            isCutCandidate: !!s.isCutCandidate,
            cutReason: s.cutReason,
            words: s.words?.map((w, wIdx) => ({
              id: w.id || `w_${idx}_${wIdx}`,
              text: w.text,
              startSec: w.startSec,
              endSec: w.endSec,
              confidence: w.confidence
            }))
          }));
          setLiveTranscriptLines(loadedLines);
        }
      }).catch(() => {
        // Ignore load error
      });
    }
  }, [clip.id, clip.transcript, project?.folderPath]);

  // Handle cut toggle
  const handleToggleCut = (lineId: string, line: TranscriptLine) => {
    const isNowAccepted = !acceptedCuts[lineId];
    setAcceptedCuts((prev) => ({ ...prev, [lineId]: isNowAccepted }));
    if (isNowAccepted) {
      onAcceptCut?.(line);
    } else {
      onRejectCut?.(line);
    }
  };

  // Batch accept all cuts
  const handleAcceptAllCuts = () => {
    const newAccepted: Record<string, boolean> = { ...acceptedCuts };
    liveTranscriptLines.forEach((line) => {
      if (line.isCutCandidate && !newAccepted[line.id]) {
        newAccepted[line.id] = true;
        onAcceptCut?.(line);
      }
    });
    setAcceptedCuts(newAccepted);
  };

  // Revert all cuts
  const handleRevertAllCuts = () => {
    liveTranscriptLines.forEach((line) => {
      if (acceptedCuts[line.id]) {
        onRejectCut?.(line);
      }
    });
    setAcceptedCuts({});
  };

  // Start Whisper Transcription using desktopBridge
  const handleRunTranscription = async () => {
    setIsTranscribing(true);
    setTranscriptionError(null);
    setTranscriptionProgress({ percent: 5, stage: 'extracting_audio', message: 'Extraindo áudio...' });

    // Listen for progress
    const unsubscribe = onTranscriptionProgress((payload) => {
      setTranscriptionProgress(payload);
    });

    try {
      const audioPath = await extractAudioForTranscription(clip.filename);
      setTranscriptionProgress({ percent: 25, stage: 'transcribing', message: 'Processando com Whisper AI...' });

      const result = await runTranscription(audioPath, language, modelSize);

      if (result.segments && result.segments.length > 0) {
        const newLines: TranscriptLine[] = result.segments.map((seg, idx) => ({
          id: seg.id || `seg_${idx}`,
          timecode: `${Math.floor(seg.startSec / 60)}:${(Math.floor(seg.startSec % 60)).toString().padStart(2, '0')}`,
          seconds: seg.startSec,
          endSeconds: seg.endSec,
          text: seg.text,
          isCutCandidate: !!seg.isCutCandidate,
          cutReason: seg.cutReason,
          words: seg.words?.map((w, wIdx) => ({
            id: w.id || `w_${idx}_${wIdx}`,
            text: w.text,
            startSec: w.startSec,
            endSec: w.endSec,
            confidence: w.confidence
          }))
        }));

        setLiveTranscriptLines(newLines);

        // Save to project disk if project is open
        if (project?.folderPath) {
          await saveTranscription(project.folderPath, clip.id, result);
        }
      }
    } catch (err: any) {
      console.error('Transcription error:', err);
      setTranscriptionError(err.message || 'Falha ao executar transcrição Whisper.');
    } finally {
      setIsTranscribing(false);
      setTranscriptionProgress(null);
      unsubscribe();
    }
  };

  // Export subtitle / transcript files (SRT, VTT, TXT, JSON)
  const handleExport = (format: 'srt' | 'vtt' | 'txt' | 'json') => {
    let content = '';
    const filename = `${clip.filename.replace(/\.[^/.]+$/, '')}_transcription.${format}`;

    if (format === 'txt') {
      content = liveTranscriptLines.map((l) => `[${l.timecode}] ${l.text}`).join('\n\n');
    } else if (format === 'json') {
      content = JSON.stringify(liveTranscriptLines, null, 2);
    } else if (format === 'srt') {
      content = liveTranscriptLines.map((l, index) => {
        const start = formatTimecodeSrt(l.seconds);
        const end = formatTimecodeSrt(l.endSeconds || l.seconds + 3);
        return `${index + 1}\n${start} --> ${end}\n${l.text}\n`;
      }).join('\n');
    } else if (format === 'vtt') {
      content = 'WEBVTT\n\n' + liveTranscriptLines.map((l, index) => {
        const start = formatTimecodeVtt(l.seconds);
        const end = formatTimecodeVtt(l.endSeconds || l.seconds + 3);
        return `${index + 1}\n${start} --> ${end}\n${l.text}\n`;
      }).join('\n');
    }

    const blob = new Blob([content], { type: 'text/plain;charset=utf-8' });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = filename;
    link.click();
    URL.revokeObjectURL(url);
  };

  const formatTimecodeSrt = (seconds: number) => {
    const hrs = Math.floor(seconds / 3600);
    const mins = Math.floor((seconds % 3600) / 60);
    const secs = Math.floor(seconds % 60);
    const ms = Math.floor((seconds % 1) * 1000);
    return `${hrs.toString().padStart(2, '0')}:${mins.toString().padStart(2, '0')}:${secs.toString().padStart(2, '0')},${ms.toString().padStart(3, '0')}`;
  };

  const formatTimecodeVtt = (seconds: number) => {
    const hrs = Math.floor(seconds / 3600);
    const mins = Math.floor((seconds % 3600) / 60);
    const secs = Math.floor(seconds % 60);
    const ms = Math.floor((seconds % 1) * 1000);
    return `${hrs.toString().padStart(2, '0')}:${mins.toString().padStart(2, '0')}:${secs.toString().padStart(2, '0')}.${ms.toString().padStart(3, '0')}`;
  };

  // Filter lines based on search query and cut filter
  const filteredTranscript = useMemo(() => {
    return liveTranscriptLines.filter((line) => {
      if (cutFilter === 'cuts_only' && !line.isCutCandidate) return false;
      if (cutFilter === 'speech_only' && (line.isCutCandidate || line.isAudioEvent)) return false;
      if (!searchQuery.trim()) return true;
      return line.text.toLowerCase().includes(searchQuery.toLowerCase());
    });
  }, [liveTranscriptLines, searchQuery, cutFilter]);

  // Count candidates for quick stats
  const totalCutsCount = useMemo(() => {
    return liveTranscriptLines.filter((l) => l.isCutCandidate).length;
  }, [liveTranscriptLines]);

  const acceptedCutsCount = useMemo(() => {
    return Object.values(acceptedCuts).filter(Boolean).length;
  }, [acceptedCuts]);

  return (
    <div className="bg-slate-900/90 border border-slate-800/90 rounded-xl overflow-hidden shadow-sm flex flex-col flex-1">
      {/* Top 4-Tab Navigation */}
      <div className="flex items-center border-b border-slate-800/80 bg-slate-950/70 px-2 overflow-x-auto scrollbar-none">
        <button
          onClick={() => setActiveTab('transcription')}
          className={`flex items-center space-x-1.5 px-3 py-2.5 text-xs font-semibold border-b-2 transition-all whitespace-nowrap ${
            activeTab === 'transcription'
              ? 'border-blue-500 text-blue-400 bg-blue-500/5'
              : 'border-transparent text-slate-400 hover:text-slate-200'
          }`}
        >
          <MessageSquare className="w-3.5 h-3.5" />
          <span>Transcrição</span>
        </button>

        <button
          onClick={() => setActiveTab('analysis')}
          className={`flex items-center space-x-1.5 px-3 py-2.5 text-xs font-semibold border-b-2 transition-all whitespace-nowrap ${
            activeTab === 'analysis'
              ? 'border-blue-500 text-blue-400 bg-blue-500/5'
              : 'border-transparent text-slate-400 hover:text-slate-200'
          }`}
        >
          <Sparkles className="w-3.5 h-3.5" />
          <span>Análise IA</span>
        </button>

        <button
          onClick={() => setActiveTab('metadata')}
          className={`flex items-center space-x-1.5 px-3 py-2.5 text-xs font-semibold border-b-2 transition-all whitespace-nowrap ${
            activeTab === 'metadata'
              ? 'border-blue-500 text-blue-400 bg-blue-500/5'
              : 'border-transparent text-slate-400 hover:text-slate-200'
          }`}
        >
          <Info className="w-3.5 h-3.5" />
          <span>Metadados</span>
        </button>

        <button
          onClick={() => setActiveTab('thumbnails')}
          className={`flex items-center space-x-1.5 px-3 py-2.5 text-xs font-semibold border-b-2 transition-all whitespace-nowrap ${
            activeTab === 'thumbnails'
              ? 'border-blue-500 text-blue-400 bg-blue-500/5'
              : 'border-transparent text-slate-400 hover:text-slate-200'
          }`}
        >
          <Film className="w-3.5 h-3.5" />
          <span>Miniaturas</span>
        </button>
      </div>

      {/* Tab 1: Transcrição (Whisper com word-level e cortes) */}
      {activeTab === 'transcription' && (
        <div className="flex flex-col flex-1 p-3 space-y-3 min-h-0">
          {/* Controls Bar: Model, Language, Transcribe CTA, Export */}
          <div className="bg-slate-950/60 p-2.5 rounded-lg border border-slate-800 space-y-2">
            <div className="flex items-center justify-between gap-2 flex-wrap">
              <div className="flex items-center gap-1.5">
                <span className="text-[11px] text-slate-400 font-medium">Modelo:</span>
                <select
                  value={modelSize}
                  onChange={(e) => setModelSize(e.target.value as any)}
                  disabled={isTranscribing}
                  className="bg-slate-900 border border-slate-700/80 rounded px-2 py-1 text-xs text-slate-200 focus:outline-none focus:border-blue-500 disabled:opacity-50"
                >
                  <option value="tiny">Tiny (Ultrarrápido)</option>
                  <option value="base">Base (Rápido)</option>
                  <option value="small">Small (Equilibrado)</option>
                  <option value="medium">Medium (Alta Precisão)</option>
                  <option value="large-v3">Large-v3 (Qualidade Máxima)</option>
                </select>
              </div>

              <div className="flex items-center gap-1.5">
                <span className="text-[11px] text-slate-400 font-medium">Idioma:</span>
                <select
                  value={language}
                  onChange={(e) => setLanguage(e.target.value as any)}
                  disabled={isTranscribing}
                  className="bg-slate-900 border border-slate-700/80 rounded px-2 py-1 text-xs text-slate-200 focus:outline-none focus:border-blue-500 disabled:opacity-50"
                >
                  <option value="pt">Português (BR)</option>
                  <option value="en">Inglês (EN)</option>
                  <option value="es">Espanhol (ES)</option>
                  <option value="auto">Auto-detectar</option>
                </select>
              </div>

              <div className="flex items-center gap-2 ml-auto">
                <button
                  onClick={handleRunTranscription}
                  disabled={isTranscribing}
                  className="px-3 py-1 bg-blue-600 hover:bg-blue-500 disabled:bg-slate-800 text-white rounded text-xs font-semibold flex items-center space-x-1.5 transition-colors shadow-sm"
                  title="Executar transcrição local via Whisper nativo"
                >
                  {isTranscribing ? (
                    <>
                      <Loader2 className="w-3.5 h-3.5 animate-spin" />
                      <span>Transcrevendo...</span>
                    </>
                  ) : (
                    <>
                      <Sparkles className="w-3.5 h-3.5" />
                      <span>Transcrever Clip</span>
                    </>
                  )}
                </button>

                <div className="relative group">
                  <button
                    className="p-1 text-slate-400 hover:text-white bg-slate-900 hover:bg-slate-800 border border-slate-800 rounded transition-colors"
                    title="Exportar legendas e transcrição"
                  >
                    <Download className="w-3.5 h-3.5" />
                  </button>
                  <div className="absolute right-0 top-full mt-1 w-28 bg-slate-950 border border-slate-800 rounded shadow-lg py-1 hidden group-hover:block z-20 text-[11px]">
                    <button
                      onClick={() => handleExport('srt')}
                      className="w-full text-left px-3 py-1 text-slate-300 hover:bg-blue-600 hover:text-white"
                    >
                      Exportar .SRT
                    </button>
                    <button
                      onClick={() => handleExport('vtt')}
                      className="w-full text-left px-3 py-1 text-slate-300 hover:bg-blue-600 hover:text-white"
                    >
                      Exportar .VTT
                    </button>
                    <button
                      onClick={() => handleExport('txt')}
                      className="w-full text-left px-3 py-1 text-slate-300 hover:bg-blue-600 hover:text-white"
                    >
                      Exportar .TXT
                    </button>
                    <button
                      onClick={() => handleExport('json')}
                      className="w-full text-left px-3 py-1 text-slate-300 hover:bg-blue-600 hover:text-white"
                    >
                      Exportar .JSON
                    </button>
                  </div>
                </div>
              </div>
            </div>

            {/* Transcription Progress Banner */}
            {isTranscribing && transcriptionProgress && (
              <div className="pt-2 border-t border-slate-800/80 space-y-1.5">
                <div className="flex justify-between items-center text-[11px]">
                  <span className="text-blue-400 flex items-center space-x-1">
                    <Loader2 className="w-3 h-3 animate-spin" />
                    <span>{transcriptionProgress.message || 'Processando áudio...'}</span>
                  </span>
                  <span className="text-slate-400 font-mono font-bold">
                    {Math.round(transcriptionProgress.percent)}%
                  </span>
                </div>
                <div className="w-full bg-slate-800 h-1.5 rounded-full overflow-hidden">
                  <div
                    className="bg-blue-500 h-full rounded-full transition-all duration-300"
                    style={{ width: `${transcriptionProgress.percent}%` }}
                  />
                </div>
              </div>
            )}

            {/* Error banner */}
            {transcriptionError && (
              <div className="pt-2 border-t border-red-500/30 flex items-center space-x-2 text-xs text-red-400 bg-red-950/20 p-2 rounded">
                <AlertCircle className="w-4 h-4 shrink-0" />
                <span className="flex-1">{transcriptionError}</span>
              </div>
            )}
          </div>

          {/* Search, Filter & Cut Batch Controls */}
          <div className="flex items-center justify-between gap-2">
            <div className="relative flex-1">
              <Search className="w-3.5 h-3.5 absolute left-2.5 top-1/2 -translate-y-1/2 text-slate-400" />
              <input
                type="text"
                value={searchQuery}
                onChange={(e) => setSearchQuery(e.target.value)}
                placeholder="Buscar palavra ou trecho..."
                className="w-full bg-slate-950 border border-slate-800 rounded-lg pl-7 pr-3 py-1 text-xs text-white placeholder-slate-500 focus:outline-none focus:border-blue-500"
              />
              {searchQuery && (
                <button
                  onClick={() => setSearchQuery('')}
                  className="absolute right-2 top-1/2 -translate-y-1/2 text-slate-500 hover:text-white text-xs"
                >
                  ✕
                </button>
              )}
            </div>

            <div className="flex items-center gap-1">
              <select
                value={cutFilter}
                onChange={(e) => setCutFilter(e.target.value as any)}
                className="bg-slate-950 border border-slate-800 rounded-lg px-2 py-1 text-xs text-slate-300 focus:outline-none focus:border-blue-500"
              >
                <option value="all">Todas as Linhas</option>
                <option value="cuts_only">Apenas Cortes ({totalCutsCount})</option>
                <option value="speech_only">Apenas Falas</option>
              </select>

              {totalCutsCount > 0 && (
                <button
                  onClick={handleAcceptAllCuts}
                  className="px-2 py-1 bg-red-500/20 hover:bg-red-500/30 text-red-300 border border-red-500/30 rounded text-xs font-semibold flex items-center space-x-1 transition-colors"
                  title="Aceitar todos os cortes sugeridos"
                >
                  <Scissors className="w-3 h-3" />
                  <span>Aplicar Todos ({totalCutsCount})</span>
                </button>
              )}

              {acceptedCutsCount > 0 && (
                <button
                  onClick={handleRevertAllCuts}
                  className="p-1 bg-slate-800 hover:bg-slate-700 text-slate-400 hover:text-white rounded text-xs transition-colors"
                  title="Reverter todos os cortes aplicados"
                >
                  <RotateCcw className="w-3 h-3" />
                </button>
              )}
            </div>
          </div>

          {/* Transcript Lines List with Word-Level Synced Highlighting */}
          <div
            ref={transcriptContainerRef}
            className="flex-1 overflow-y-auto space-y-2 max-h-[380px] pr-1 scrollbar-thin scrollbar-thumb-slate-800"
          >
            {filteredTranscript.length === 0 ? (
              <div className="text-center py-8 text-slate-500 text-xs flex flex-col items-center justify-center space-y-2">
                <FileText className="w-8 h-8 text-slate-600 stroke-1" />
                <span>Nenhuma fala ou transcrição encontrada.</span>
                <span className="text-[10px]">Clique em &quot;Transcrever Clip&quot; acima para processar o áudio.</span>
              </div>
            ) : (
              filteredTranscript.map((line) => {
                const isCut = line.isCutCandidate;
                const isAccepted = acceptedCuts[line.id];
                const endSec = line.endSeconds || line.seconds + 5;
                const isCurrent = currentTimeSec >= line.seconds && currentTimeSec < endSec;

                // Build or use words for micro-seeking and real-time word highlight
                const wordsList: TranscriptWord[] = line.words && line.words.length > 0
                  ? line.words
                  : line.text.split(' ').map((wordText, wIdx, arr) => {
                      const wordDuration = (endSec - line.seconds) / Math.max(arr.length, 1);
                      const wStart = line.seconds + wIdx * wordDuration;
                      const wEnd = wStart + wordDuration;
                      return {
                        id: `gen_${line.id}_${wIdx}`,
                        text: wordText,
                        startSec: wStart,
                        endSec: wEnd
                      };
                    });

                return (
                  <div
                    key={line.id}
                    className={`p-2.5 rounded-lg border text-xs transition-all ${
                      isCurrent
                        ? 'bg-blue-600/15 border-blue-500/50 shadow-sm'
                        : isCut
                        ? isAccepted
                          ? 'bg-red-950/20 border-red-500/40 opacity-70'
                          : 'bg-red-500/10 border-red-500/30'
                        : line.isAudioEvent
                        ? 'bg-slate-950/40 border-slate-800/60'
                        : 'bg-slate-950/60 border-slate-800/80 hover:border-slate-700'
                    }`}
                  >
                    <div className="flex items-start justify-between gap-2">
                      {/* Timestamp button (click to seek line) */}
                      <button
                        onClick={() => onSeek(line.seconds)}
                        className={`font-mono font-semibold text-[11px] px-1.5 py-0.5 rounded transition-colors shrink-0 flex items-center space-x-1 ${
                          isCurrent
                            ? 'bg-blue-500 text-white font-bold'
                            : 'bg-slate-800/80 text-blue-400 hover:bg-blue-500/20'
                        }`}
                        title="Pular vídeo para este momento"
                      >
                        <Clock className="w-2.5 h-2.5" />
                        <span>{line.timecode}</span>
                      </button>

                      {/* Sentence body with clickable word tokens */}
                      <div className="flex-1 min-w-0 leading-relaxed">
                        {line.speaker && (
                          <span className="font-semibold text-slate-300 mr-1.5">
                            {line.speaker}:
                          </span>
                        )}

                        {line.isAudioEvent ? (
                          <span className="italic text-slate-400 flex items-center space-x-1">
                            <Volume2 className="w-3 h-3 text-cyan-400 inline shrink-0" />
                            <span>{line.text}</span>
                          </span>
                        ) : (
                          <div className="inline flex-wrap gap-1">
                            {wordsList.map((word) => {
                              const isWordActive =
                                currentTimeSec >= word.startSec && currentTimeSec < word.endSec;
                              return (
                                <span
                                  key={word.id}
                                  onClick={() => onSeek(word.startSec)}
                                  className={`cursor-pointer px-0.5 py-0.2 rounded transition-colors mr-1 inline-block ${
                                    isWordActive
                                      ? 'bg-blue-500 text-white font-bold shadow-xs'
                                      : isAccepted
                                      ? 'line-through text-red-400/70 hover:text-red-300'
                                      : isCut
                                      ? 'text-red-300 hover:bg-red-500/20'
                                      : 'text-slate-200 hover:bg-slate-800 hover:text-blue-300'
                                  }`}
                                  title={`Pular para ${word.startSec.toFixed(2)}s`}
                                >
                                  {word.text}
                                </span>
                              );
                            })}
                          </div>
                        )}

                        {/* Cut Candidate Reason and Accept CTA */}
                        {isCut && (
                          <div className="mt-1.5 pt-1.5 border-t border-red-500/20 flex items-center justify-between">
                            <span className="text-[10px] text-red-400 font-medium flex items-center space-x-1">
                              <Scissors className="w-3 h-3" />
                              <span>{line.cutReason || 'Hesitação / Pausa detectada'}</span>
                            </span>

                            <button
                              onClick={() => handleToggleCut(line.id, line)}
                              className={`px-2 py-0.5 rounded text-[10px] font-bold flex items-center space-x-1 transition-all ${
                                isAccepted
                                  ? 'bg-red-600 text-white shadow-sm'
                                  : 'bg-red-500/20 hover:bg-red-500/30 text-red-300 border border-red-500/40'
                              }`}
                            >
                              {isAccepted ? (
                                <>
                                  <Check className="w-3 h-3" />
                                  <span>Corte Aplicado</span>
                                </>
                              ) : (
                                <>
                                  <Scissors className="w-3 h-3" />
                                  <span>Cortar Hesitação</span>
                                </>
                              )}
                            </button>
                          </div>
                        )}
                      </div>
                    </div>
                  </div>
                );
              })
            )}
          </div>
        </div>
      )}

      {/* Tab 2: Análise IA */}
      {activeTab === 'analysis' && (
        <div className="p-3 space-y-3 flex-1 overflow-y-auto text-xs">
          <div className="bg-slate-950/60 border border-slate-800 rounded-lg p-3 space-y-2">
            <div className="flex items-center justify-between">
              <span className="text-slate-400">Score de Qualidade IA</span>
              <span className="text-emerald-400 font-bold">{clip.aiScore}% (Excelente)</span>
            </div>
            <div className="w-full bg-slate-800 h-1.5 rounded-full overflow-hidden">
              <div
                className="bg-emerald-500 h-full rounded-full"
                style={{ width: `${clip.aiScore}%` }}
              />
            </div>
          </div>

          <div className="grid grid-cols-2 gap-2">
            <div className="bg-slate-950/60 border border-slate-800 p-2.5 rounded-lg flex flex-col">
              <span className="text-slate-400 text-[10px]">Estabilização</span>
              <span className="text-white font-semibold mt-0.5">Gimbal Suave</span>
              <span className="text-slate-500 text-[10px]">Gyro Sync Ok</span>
            </div>

            <div className="bg-slate-950/60 border border-slate-800 p-2.5 rounded-lg flex flex-col">
              <span className="text-slate-400 text-[10px]">Iluminação</span>
              <span className="text-white font-semibold mt-0.5">Luz Natural 5600K</span>
              <span className="text-slate-500 text-[10px]">Sem estouro de realces</span>
            </div>

            <div className="bg-slate-950/60 border border-slate-800 p-2.5 rounded-lg flex flex-col">
              <span className="text-slate-400 text-[10px]">Rostos Identificados</span>
              <span className="text-white font-semibold mt-0.5">1 Rosto em Foco</span>
              <span className="text-slate-500 text-[10px]">Rastreamento facial ativo</span>
            </div>

            <div className="bg-slate-950/60 border border-slate-800 p-2.5 rounded-lg flex flex-col">
              <span className="text-slate-400 text-[10px]">LUT Recomendada</span>
              <span className="text-white font-semibold mt-0.5 truncate">Rec.709 Neutral</span>
              <span className="text-slate-500 text-[10px]">DaVinci Color Managed</span>
            </div>
          </div>
        </div>
      )}

      {/* Tab 3: Metadados */}
      {activeTab === 'metadata' && (
        <div className="p-3 space-y-2 flex-1 overflow-y-auto text-xs divide-y divide-slate-800/60">
          <div className="flex justify-between py-1.5">
            <span className="text-slate-400">Nome do Arquivo</span>
            <span className="text-white font-mono">{clip.filename}</span>
          </div>
          <div className="flex justify-between py-1.5">
            <span className="text-slate-400">Resolução</span>
            <span className="text-white font-mono">{clip.resolution}</span>
          </div>
          <div className="flex justify-between py-1.5">
            <span className="text-slate-400">Câmera</span>
            <span className="text-white">{clip.camera}</span>
          </div>
          <div className="flex justify-between py-1.5">
            <span className="text-slate-400">Localização</span>
            <span className="text-white">{clip.location}</span>
          </div>
          <div className="flex justify-between py-1.5">
            <span className="text-slate-400">Data de Gravação</span>
            <span className="text-white">{clip.date}</span>
          </div>
          <div className="flex justify-between py-1.5">
            <span className="text-slate-400">Codec & Container</span>
            <span className="text-white font-mono">H.265 / HEVC 10-bit</span>
          </div>
          <div className="flex justify-between py-1.5">
            <span className="text-slate-400">Áudio</span>
            <span className="text-white font-mono">48kHz 24-bit Stereo</span>
          </div>
        </div>
      )}

      {/* Tab 4: Miniaturas */}
      {activeTab === 'thumbnails' && (
        <div className="p-3 flex-1 overflow-y-auto">
          <div className="grid grid-cols-3 gap-2">
            {[0, 15, 30, 45, 60, 75, 90, 105, 120].map((sec) => (
              <div
                key={sec}
                onClick={() => onSeek(sec)}
                className="group relative rounded-lg overflow-hidden border border-slate-800 hover:border-blue-500 cursor-pointer aspect-video bg-slate-950"
              >
                <img
                  src={clip.thumbnail}
                  alt={`Thumbnail ${sec}s`}
                  className="w-full h-full object-cover group-hover:scale-105 transition-transform"
                />
                <span className="absolute bottom-1 right-1 text-[9px] font-mono bg-slate-950/80 px-1 rounded text-slate-300">
                  {Math.floor(sec / 60)}:{(sec % 60).toString().padStart(2, '0')}
                </span>
              </div>
            ))}
          </div>
        </div>
      )}
    </div>
  );
};
