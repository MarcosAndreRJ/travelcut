"""Data contracts and schemas for TravelCut Studio - Phase 7 Preparatory Research.

Provides versioned, strictly validated data models for word-level speech
transcription, voice activity intervals, silence classification, and
non-destructive text-based cut preparation. Fully compatible with Phase 4.1 contracts.
"""

from __future__ import annotations

import datetime
from enum import Enum
from typing import Any, Dict, List, Literal, Optional
from pydantic import BaseModel, Field, field_validator, model_validator


class SchemaVersion(str, Enum):
    V1_0_0 = "1.0.0"


class TranscriptionStatus(str, Enum):
    COMPLETED = "completed"
    PARTIAL = "partial"
    FAILED = "failed"


class AlignmentStatus(str, Enum):
    ALIGNED = "aligned"
    INTERPOLATED = "interpolated"
    LOW_CONFIDENCE = "low_confidence"
    FAILED = "failed"


class CutReason(str, Enum):
    HESITATION = "hesitation"
    SILENCE = "silence"
    REPETITION = "repetition"
    UNSTABLE = "unstable"
    FILLER_WORD = "filler_word"
    LOW_QUALITY = "low_quality"
    EDITORIAL_REACTION = "editorial_reaction"


class SilenceCategory(str, Enum):
    """Categorization of acoustic gaps in travel videos."""
    PAUSE_WORD = "pause_word"           # Short pause between words (<400ms)
    PAUSE_SENTENCE = "pause_sentence"   # Natural speech breath/boundary (400ms - 1200ms)
    REMOVABLE_GAP = "removable_gap"     # Long awkward vocal silence (>1200ms), primary cut candidate
    SCENERY = "scenery"                 # Scenery/landscape contemplative b-roll
    ROAD = "road"                       # Road trip, moving transport sound
    DRONE = "drone"                     # Aerial flight footage
    WATERFALL = "waterfall"             # Cascading water / natural rushing water
    AMBIENT = "ambient"                 # General environmental soundscape
    INTENTIONAL_PAUSE = "intentional_pause"  # Dramatic or comedic pause


class CutDecisionState(str, Enum):
    PENDING = "pending"
    ACCEPTED = "accepted"
    REJECTED = "rejected"


# ==========================================
# Core Models
# ==========================================

class ModelInfo(BaseModel):
    """Metadata regarding the automatic speech recognition (ASR) engine."""
    engine_name: str = Field(..., description="ASR engine: faster-whisper, whisperx, openai-whisper, mock")
    model_name: str = Field(..., description="Model identifier, e.g. tiny, base, small, medium, large-v3")
    quantization: str = Field(default="int8", description="Quantization format: int8, float16, float32")
    device: str = Field(default="cpu", description="Execution device: cpu or cuda")
    compute_type: str = Field(default="int8", description="Compute type used by inference backend")
    parameters_count: Optional[str] = Field(default=None, description="Model parameter count, e.g. 39M, 244M")


class TranscriptWord(BaseModel):
    """Individual word with exact millisecond bounds [start_ms, end_ms).

    Convention:
    - start_ms: Inclusive start boundary in integer milliseconds.
    - end_ms: Exclusive end boundary in integer milliseconds.
    """
    id: str = Field(..., description="Unique word identifier, e.g. w_0001")
    segment_id: str = Field(..., description="Parent segment identifier, e.g. seg_0001")
    media_id: str = Field(..., description="Associated media asset identifier")
    text: str = Field(..., description="Transcribed lexical word")
    start_ms: int = Field(..., ge=0, description="Inclusive start time in milliseconds")
    end_ms: int = Field(..., ge=0, description="Exclusive end time in milliseconds")
    confidence: float = Field(default=1.0, ge=0.0, le=1.0, description="Confidence score [0.0, 1.0]")
    alignment_status: AlignmentStatus = Field(default=AlignmentStatus.ALIGNED, description="Status of word alignment")
    is_cut_candidate: bool = Field(default=False, description="Flagged as candidate for text-based cut")
    cut_reason: Optional[CutReason] = Field(default=None, description="Reason if marked as cut candidate")

    @model_validator(mode="after")
    def validate_temporal_order(self) -> TranscriptWord:
        if self.start_ms > self.end_ms:
            raise ValueError(
                f"Word {self.id} has start_ms ({self.start_ms}) greater than end_ms ({self.end_ms})"
            )
        return self

    def to_phase4_dict(self) -> Dict[str, Any]:
        """Convert to Phase 4.1 TranscriptWord format."""
        return {
            "id": self.id,
            "text": self.text,
            "startTime": self.start_ms,
            "endTime": self.end_ms,
            "segmentId": self.segment_id,
            "mediaId": self.media_id,
            "confidence": self.confidence,
            "isCutCandidate": self.is_cut_candidate,
            "cutReason": self.cut_reason.value if self.cut_reason else None,
        }


class TranscriptSegment(BaseModel):
    """Speech segment containing continuous utterance and constituent words."""
    id: str = Field(..., description="Segment identifier, e.g. seg_0001")
    media_id: str = Field(..., description="Associated media asset identifier")
    start_ms: int = Field(..., ge=0, description="Inclusive start in milliseconds")
    end_ms: int = Field(..., ge=0, description="Exclusive end in milliseconds")
    text: str = Field(..., description="Full text for this utterance")
    speaker_id: Optional[str] = Field(default=None, description="Speaker identifier if diarized")
    words: List[TranscriptWord] = Field(default_factory=list, description="Ordered constituent words")
    avg_logprob: Optional[float] = Field(default=None, description="Average token log probability")
    no_speech_prob: Optional[float] = Field(default=None, description="Probability of segment being non-speech")

    @model_validator(mode="after")
    def validate_segment_bounds(self) -> TranscriptSegment:
        if self.start_ms > self.end_ms:
            raise ValueError(
                f"Segment {self.id} start_ms ({self.start_ms}) > end_ms ({self.end_ms})"
            )
        return self

    def to_phase4_dict(self) -> Dict[str, Any]:
        """Convert to Phase 4.1 TranscriptSegment format."""
        return {
            "id": self.id,
            "mediaId": self.media_id,
            "startTime": self.start_ms,
            "endTime": self.end_ms,
            "text": self.text,
            "speakerId": self.speaker_id,
            "words": [w.to_phase4_dict() for w in self.words],
        }


class SpeechInterval(BaseModel):
    """Interval with detected voice activity (VAD)."""
    start_ms: int = Field(..., ge=0)
    end_ms: int = Field(..., ge=0)
    speech_probability: float = Field(default=1.0, ge=0.0, le=1.0)
    energy_rms: Optional[float] = Field(default=None)

    @model_validator(mode="after")
    def validate_interval(self) -> SpeechInterval:
        if self.start_ms >= self.end_ms:
            raise ValueError(f"SpeechInterval start_ms ({self.start_ms}) must be < end_ms ({self.end_ms})")
        return self


class SilenceInterval(BaseModel):
    """Interval without detected voice activity, classified for editorial decisions."""
    id: str = Field(...)
    media_id: str = Field(...)
    start_ms: int = Field(..., ge=0)
    end_ms: int = Field(..., ge=0)
    duration_ms: int = Field(..., ge=0)
    category: SilenceCategory = Field(...)
    is_removable_candidate: bool = Field(default=False)
    recommendation_note: str = Field(default="")
    ambient_noise_level_db: Optional[float] = Field(default=None)

    @model_validator(mode="after")
    def validate_silence(self) -> SilenceInterval:
        if self.start_ms >= self.end_ms:
            raise ValueError(f"SilenceInterval start_ms ({self.start_ms}) must be < end_ms ({self.end_ms})")
        computed_duration = self.end_ms - self.start_ms
        if self.duration_ms != computed_duration:
            self.duration_ms = computed_duration
        return self


class ProcessingMetadata(BaseModel):
    """Diagnostic and performance metrics for the transcription run."""
    processing_time_ms: int = Field(..., ge=0, description="Wall clock runtime in ms")
    audio_duration_ms: int = Field(..., ge=0, description="Audio length in ms")
    real_time_factor: float = Field(..., description="RTF = processing_time / audio_duration (< 1.0 is faster than real time)")
    peak_memory_mb: Optional[float] = Field(default=None, description="Peak memory consumption in MB")
    sample_rate_hz: int = Field(default=16000, description="Audio sample rate")
    channels: int = Field(default=1, description="Audio channel count")
    timestamp_utc: str = Field(
        default_factory=lambda: datetime.datetime.now(datetime.timezone.utc).isoformat(),
        description="ISO 8601 execution timestamp"
    )


class CutRequest(BaseModel):
    """Structured non-destructive cut request prepared for the editing engine."""
    request_id: str = Field(...)
    media_id: str = Field(...)
    reason: CutReason = Field(...)
    start_ms: int = Field(..., ge=0)
    end_ms: int = Field(..., ge=0)
    duration_ms: int = Field(..., ge=0)
    word_ids: List[str] = Field(default_factory=list)
    selected_text: str = Field(...)
    alignment_warnings: List[str] = Field(default_factory=list)
    decision_state: CutDecisionState = Field(default=CutDecisionState.PENDING)
    confidence: float = Field(default=1.0, ge=0.0, le=1.0)

    @model_validator(mode="after")
    def validate_cut_bounds(self) -> CutRequest:
        if self.start_ms >= self.end_ms:
            raise ValueError(f"CutRequest start_ms ({self.start_ms}) must be < end_ms ({self.end_ms})")
        self.duration_ms = self.end_ms - self.start_ms
        return self

    def to_phase4_cut_suggestion(self) -> Dict[str, Any]:
        """Convert to Phase 4.1 CutSuggestion contract."""
        return {
            "id": self.request_id,
            "mediaId": self.media_id,
            "sourceRange": {
                "start": self.start_ms,
                "end": self.end_ms,
            },
            "reason": self.reason.value,
            "confidence": self.confidence,
            "decisionState": self.decision_state.value,
            "displayText": self.selected_text,
            "originalText": self.selected_text,
        }

    def to_phase4_event(self) -> Dict[str, Any]:
        """Convert to Phase 4.1 TranscriptCutRequestedEvent."""
        return {
            "type": "transcript.cut.requested",
            "payload": {
                "mediaId": self.media_id,
                "sourceRange": {
                    "start": self.start_ms,
                    "end": self.end_ms,
                },
                "reason": self.reason.value,
                "cutSuggestionId": self.request_id,
                "suggestedText": self.selected_text,
            },
        }


class TranscriptionDocument(BaseModel):
    """Root transcription contract and normalized JSON export schema."""
    schema_version: SchemaVersion = Field(default=SchemaVersion.V1_0_0)
    media_id: str = Field(...)
    status: TranscriptionStatus = Field(default=TranscriptionStatus.COMPLETED)
    language: str = Field(default="pt")
    language_probability: float = Field(default=1.0, ge=0.0, le=1.0)
    duration_ms: int = Field(..., ge=0)
    full_text: str = Field(...)
    model_info: ModelInfo = Field(
        default_factory=lambda: ModelInfo(
            engine_name="unknown",
            model_name="default",
            quantization="int8",
            device="cpu",
        )
    )
    segments: List[TranscriptSegment] = Field(default_factory=list)
    words: List[TranscriptWord] = Field(default_factory=list)
    speech_intervals: List[SpeechInterval] = Field(default_factory=list)
    silence_intervals: List[SilenceInterval] = Field(default_factory=list)
    processing_metadata: ProcessingMetadata = Field(
        default_factory=lambda: ProcessingMetadata(
            processing_time_ms=0,
            audio_duration_ms=0,
            real_time_factor=0.0,
            sample_rate_hz=16000,
            channels=1,
        )
    )
    warnings: List[str] = Field(default_factory=list)

    @model_validator(mode="after")
    def validate_integrity(self) -> TranscriptionDocument:
        # Check duplicate word IDs
        word_ids = set()
        for w in self.words:
            if w.id in word_ids:
                raise ValueError(f"Duplicate word ID detected: {w.id}")
            word_ids.add(w.id)
            if w.end_ms > self.duration_ms:
                self.warnings.append(
                    f"Word {w.id} end_ms ({w.end_ms}) exceeds audio duration ({self.duration_ms})"
                )

        # Check duplicate segment IDs
        segment_ids = set()
        for s in self.segments:
            if s.id in segment_ids:
                raise ValueError(f"Duplicate segment ID detected: {s.id}")
            segment_ids.add(s.id)

        return self

    def to_phase4_workspace_export(self) -> Dict[str, Any]:
        """Produce Phase 4.1 compliant transcription payload."""
        words_export = [
            {
                "id": w.id,
                "text": w.text,
                "startSec": round(w.start_ms / 1000.0, 3),
                "endSec": round(w.end_ms / 1000.0, 3),
                "confidence": w.confidence,
                "isCutCandidate": w.is_cut_candidate,
            }
            for w in self.words
        ]
        silence_export = [
            {
                "id": s.id,
                "startSec": round(s.start_ms / 1000.0, 3),
                "endSec": round(s.end_ms / 1000.0, 3),
                "durationSec": round(s.duration_ms / 1000.0, 3),
                "category": s.category.value,
                "isRemovable": s.is_removable_candidate,
            }
            for s in self.silence_intervals
        ]
        return {
            "mediaId": self.media_id,
            "durationMs": self.duration_ms,
            "durationSec": round(self.duration_ms / 1000.0, 3),
            "language": self.language,
            "fullText": self.full_text,
            "segments": [s.to_phase4_dict() for s in self.segments],
            "transcription": [s.to_phase4_dict() for s in self.segments],
            "words": words_export,
            "silenceIntervals": silence_export,
            "cutSuggestions": [
                {
                    "id": f"cut_sil_{idx:04d}",
                    "mediaId": self.media_id,
                    "sourceRange": {
                        "start": sil.start_ms,
                        "end": sil.end_ms,
                    },
                    "startSec": round(sil.start_ms / 1000.0, 3),
                    "endSec": round(sil.end_ms / 1000.0, 3),
                    "durationSec": round(sil.duration_ms / 1000.0, 3),
                    "reason": "silence",
                    "confidence": 0.95,
                    "decisionState": "pending",
                    "displayText": f"[Silêncio {sil.category.value}: {sil.duration_ms}ms]",
                    "originalText": "",
                }
                for idx, sil in enumerate(self.silence_intervals)
                if sil.is_removable_candidate
            ],
        }
