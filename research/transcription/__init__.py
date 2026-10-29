"""TravelCut Studio - Phase 7 Preparatory Research Package.

Speech transcription, word-level alignment, VAD analysis, and non-destructive
text-based cut preparation for travel video workflows.
"""

from research.transcription.alignment import WordAligner
from research.transcription.audio import AudioMetadata, AudioSignal, inspect_wav_header, load_wav_pcm
from research.transcription.contracts import (
    AlignmentStatus,
    CutDecisionState,
    CutReason,
    CutRequest,
    ModelInfo,
    ProcessingMetadata,
    SchemaVersion,
    SilenceCategory,
    SilenceInterval,
    SpeechInterval,
    TranscriptSegment,
    TranscriptWord,
    TranscriptionDocument,
    TranscriptionStatus,
)
from research.transcription.cut_preparer import (
    detect_invalid_selections,
    detect_overlapping_intervals,
    detect_words_without_timestamp,
    find_phrase,
    find_word_by_id,
    get_selection_interval,
    produce_cut_request,
    validate_word_continuity,
)
from research.transcription.engine import (
    BaseTranscriptionEngine,
    FasterWhisperEngine,
    MockTranscriptionEngine,
)
from research.transcription.silence_detector import SilenceAnalysisSummary, SilenceDetector
from research.transcription.vad import VADConfig, VoiceActivityDetector

__all__ = [
    "WordAligner",
    "AudioMetadata",
    "AudioSignal",
    "inspect_wav_header",
    "load_wav_pcm",
    "AlignmentStatus",
    "CutDecisionState",
    "CutReason",
    "CutRequest",
    "ModelInfo",
    "ProcessingMetadata",
    "SchemaVersion",
    "SilenceCategory",
    "SilenceInterval",
    "SpeechInterval",
    "TranscriptSegment",
    "TranscriptWord",
    "TranscriptionDocument",
    "TranscriptionStatus",
    "detect_invalid_selections",
    "detect_overlapping_intervals",
    "detect_words_without_timestamp",
    "find_phrase",
    "find_word_by_id",
    "get_selection_interval",
    "produce_cut_request",
    "validate_word_continuity",
    "BaseTranscriptionEngine",
    "FasterWhisperEngine",
    "MockTranscriptionEngine",
    "SilenceAnalysisSummary",
    "SilenceDetector",
    "VADConfig",
    "VoiceActivityDetector",
]
