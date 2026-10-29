"""Transcription engine interfaces, Mock engine, and Faster-Whisper adapter.

Provides:
- BaseTranscriptionEngine (abstract interface)
- MockTranscriptionEngine (deterministic, offline, zero-dependency test engine)
- FasterWhisperEngine (adapter for production faster-whisper/CTranslate2 when installed)
"""

from __future__ import annotations

import abc
import datetime
import time
from pathlib import Path
from typing import Any, Dict, List, Optional, Tuple

from research.transcription.alignment import WordAligner
from research.transcription.audio import AudioSignal, inspect_wav_header, load_wav_pcm
from research.transcription.contracts import (
    AlignmentStatus,
    CutReason,
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
from research.transcription.silence_detector import SilenceDetector
from research.transcription.vad import VADConfig, VoiceActivityDetector


class BaseTranscriptionEngine(abc.ABC):
    """Abstract interface for speech transcription engines."""

    @abc.abstractmethod
    def transcribe(
        self,
        audio_path: str | Path,
        media_id: str = "media_asset",
        language: str = "pt",
        word_timestamps: bool = True,
        vad_filter: bool = True,
        **kwargs: Any,
    ) -> TranscriptionDocument:
        """Transcribe audio file and produce normalized TranscriptionDocument."""
        pass


class MockTranscriptionEngine(BaseTranscriptionEngine):
    """Deterministic, offline transcription engine for CI and unit test suites.

    Simulates realistic Portuguese speech recognition, VAD analysis, silence classification,
    and word-level timestamp alignment with zero external dependencies.
    """

    def __init__(
        self,
        model_name: str = "small",
        device: str = "cpu",
        quantization: str = "int8",
        simulated_rtf: float = 0.25,
    ):
        self.model_info = ModelInfo(
            engine_name="mock-engine",
            model_name=model_name,
            quantization=quantization,
            device=device,
            compute_type=f"{quantization}_{device}",
            parameters_count="244M",
        )
        self.simulated_rtf = simulated_rtf
        self.vad_detector = VoiceActivityDetector()
        self.silence_detector = SilenceDetector()

    def transcribe(
        self,
        audio_path: str | Path,
        media_id: str = "media_asset",
        language: str = "pt",
        word_timestamps: bool = True,
        vad_filter: bool = True,
        preset_transcript: Optional[str] = None,
        custom_words: Optional[List[Dict[str, Any]]] = None,
        video_category_hint: Optional[str] = None,
        **kwargs: Any,
    ) -> TranscriptionDocument:
        """Execute deterministic simulated transcription."""
        start_time = time.perf_counter()

        # Load audio signal or inspect header
        try:
            signal = load_wav_pcm(audio_path)
            duration_ms = signal.metadata.duration_ms
            sr = signal.metadata.sample_rate
            channels = signal.metadata.channels
        except Exception:
            # Fallback if raw PCM read fails or dummy file
            meta = inspect_wav_header(audio_path)
            duration_ms = meta.duration_ms
            sr = meta.sample_rate
            channels = meta.channels
            signal = AudioSignal(metadata=meta, samples=[])

        # Step 1: Detect VAD and silence intervals
        if signal.samples and vad_filter:
            speech_intervals, silence_intervals = self.vad_detector.process(signal, media_id=media_id)
        else:
            # Fallback estimation based on duration
            if duration_ms > 0:
                speech_intervals = [
                    SpeechInterval(start_ms=100, end_ms=max(200, duration_ms - 100), speech_probability=0.98)
                ]
                silence_intervals = [
                    SilenceInterval(
                        id="sil_0001",
                        media_id=media_id,
                        start_ms=0,
                        end_ms=100,
                        duration_ms=100,
                        category=SilenceCategory.PAUSE_WORD,
                        is_removable_candidate=False,
                    )
                ]
            else:
                speech_intervals = []
                silence_intervals = []

        silence_intervals, _ = self.silence_detector.analyze(
            silence_intervals, duration_ms, media_id=media_id, video_category_hint=video_category_hint
        )

        # Step 2: Generate segments and words
        segments: List[TranscriptSegment] = []
        all_words: List[TranscriptWord] = []
        warnings: List[str] = []

        if custom_words:
            # Build directly from provided custom words dictionary
            seg_words = []
            for idx, cw in enumerate(custom_words):
                w_obj = TranscriptWord(
                    id=cw.get("id", f"w_{idx+1:04d}"),
                    segment_id=cw.get("segment_id", "seg_0001"),
                    media_id=media_id,
                    text=cw["text"],
                    start_ms=cw["start_ms"],
                    end_ms=cw["end_ms"],
                    confidence=cw.get("confidence", 1.0),
                    alignment_status=cw.get("alignment_status", AlignmentStatus.ALIGNED),
                    is_cut_candidate=cw.get("is_cut_candidate", False),
                    cut_reason=cw.get("cut_reason", None),
                )
                seg_words.append(w_obj)
                all_words.append(w_obj)

            if seg_words:
                seg_start = seg_words[0].start_ms
                seg_end = seg_words[-1].end_ms
                seg_text = " ".join(w.text for w in seg_words)
                segments.append(
                    TranscriptSegment(
                        id="seg_0001",
                        media_id=media_id,
                        start_ms=seg_start,
                        end_ms=seg_end,
                        text=seg_text,
                        words=seg_words,
                    )
                )
        else:
            # Generate realistic speech from default preset or synthetic text
            text_to_use = preset_transcript if preset_transcript is not None else (
                "Olá pessoal, bem-vindos ao TravelCut Studio! Hoje vamos explorar as cachoeiras mais incríveis da serra."
            )

            if duration_ms == 0:
                text_to_use = ""

            if text_to_use:
                # Distribute words across speech intervals
                tokens = text_to_use.split()
                if speech_intervals:
                    active_speech_ms = sum(sp.end_ms - sp.start_ms for sp in speech_intervals)
                    word_dur = max(80, int(active_speech_ms / len(tokens))) if tokens else 100
                else:
                    word_dur = max(80, int(duration_ms / max(1, len(tokens))))

                word_counter = 1
                curr_token_idx = 0

                intervals_to_use = speech_intervals or [
                    SpeechInterval(start_ms=0, end_ms=duration_ms, speech_probability=1.0)
                ]

                for seg_idx, sp in enumerate(intervals_to_use):
                    seg_id = f"seg_{seg_idx+1:04d}"
                    seg_tokens: List[str] = []
                    seg_words: List[TranscriptWord] = []

                    curr_ms = sp.start_ms
                    while curr_token_idx < len(tokens) and curr_ms < sp.end_ms:
                        token = tokens[curr_token_idx]
                        token_end = min(sp.end_ms, curr_ms + word_dur)

                        # Detect hesitation or filler words
                        is_hesitation = token.lower() in ("ééé...", "humm...", "tipo", "né", "humm")
                        cut_reason = CutReason.HESITATION if is_hesitation else None

                        w_obj = TranscriptWord(
                            id=f"w_{word_counter:04d}",
                            segment_id=seg_id,
                            media_id=media_id,
                            text=token,
                            start_ms=curr_ms,
                            end_ms=token_end,
                            confidence=0.96 if not is_hesitation else 0.72,
                            alignment_status=AlignmentStatus.ALIGNED if word_timestamps else AlignmentStatus.INTERPOLATED,
                            is_cut_candidate=is_hesitation,
                            cut_reason=cut_reason,
                        )
                        seg_words.append(w_obj)
                        all_words.append(w_obj)
                        seg_tokens.append(token)

                        curr_ms = token_end + 30  # small inter-word space
                        curr_token_idx += 1
                        word_counter += 1

                    if seg_words:
                        segments.append(
                            TranscriptSegment(
                                id=seg_id,
                                media_id=media_id,
                                start_ms=seg_words[0].start_ms,
                                end_ms=seg_words[-1].end_ms,
                                text=" ".join(seg_tokens),
                                words=seg_words,
                                avg_logprob=-0.18,
                                no_speech_prob=0.01,
                            )
                        )

        full_text = " ".join(s.text for s in segments)

        # Calculate processing metrics
        elapsed_sec = time.perf_counter() - start_time
        simulated_runtime_ms = max(5, int((duration_ms * self.simulated_rtf)))
        rtf = simulated_runtime_ms / float(max(1, duration_ms))

        metadata = ProcessingMetadata(
            processing_time_ms=simulated_runtime_ms,
            audio_duration_ms=duration_ms,
            real_time_factor=round(rtf, 3),
            peak_memory_mb=145.0,  # Simulated lightweight footprint
            sample_rate_hz=sr,
            channels=channels,
            timestamp_utc=datetime.datetime.now(datetime.timezone.utc).isoformat(),
        )

        return TranscriptionDocument(
            schema_version=SchemaVersion.V1_0_0,
            media_id=media_id,
            status=TranscriptionStatus.COMPLETED,
            language=language,
            language_probability=0.99,
            duration_ms=duration_ms,
            full_text=full_text,
            model_info=self.model_info,
            segments=segments,
            words=all_words,
            speech_intervals=speech_intervals,
            silence_intervals=silence_intervals,
            processing_metadata=metadata,
            warnings=warnings,
        )


class FasterWhisperEngine(BaseTranscriptionEngine):
    """Adapter for production faster-whisper / CTranslate2 model execution.

    Requires `faster_whisper` package to be installed.
    """

    def __init__(
        self,
        model_size_or_path: str = "small",
        device: str = "cpu",
        compute_type: str = "int8",
        cpu_threads: int = 4,
    ):
        self.model_size = model_size_or_path
        self.device = device
        self.compute_type = compute_type
        self.cpu_threads = cpu_threads
        self._model = None
        self._is_available = False

        try:
            import faster_whisper  # type: ignore
            self._is_available = True
        except ImportError:
            self._is_available = False

    @property
    def is_available(self) -> bool:
        return self._is_available

    def _ensure_model_loaded(self) -> Any:
        if not self._is_available:
            raise RuntimeError(
                "faster-whisper is not installed in the active Python environment. "
                "Install it using: pip install faster-whisper"
            )
        if self._model is None:
            from faster_whisper import WhisperModel  # type: ignore
            self._model = WhisperModel(
                self.model_size,
                device=self.device,
                compute_type=self.compute_type,
                cpu_threads=self.cpu_threads,
            )
        return self._model

    def transcribe(
        self,
        audio_path: str | Path,
        media_id: str = "media_asset",
        language: str = "pt",
        word_timestamps: bool = True,
        vad_filter: bool = True,
        **kwargs: Any,
    ) -> TranscriptionDocument:
        """Transcribe audio using faster-whisper and map to Phase 4.1 contract."""
        start_time = time.perf_counter()
        meta = inspect_wav_header(audio_path)
        model = self._ensure_model_loaded()

        segments_gen, info = model.transcribe(
            str(audio_path),
            language=language,
            word_timestamps=word_timestamps,
            vad_filter=vad_filter,
            vad_parameters=dict(min_silence_duration_ms=400),
        )

        all_segments: List[TranscriptSegment] = []
        all_words: List[TranscriptWord] = []
        word_counter = 1

        for seg_idx, s in enumerate(segments_gen):
            seg_id = f"seg_{seg_idx+1:04d}"
            s_start_ms = int(round(s.start * 1000.0))
            s_end_ms = int(round(s.end * 1000.0))

            seg_words: List[TranscriptWord] = []
            if s.words and word_timestamps:
                for w in s.words:
                    w_start_ms = int(round(w.start * 1000.0))
                    w_end_ms = int(round(w.end * 1000.0))
                    w_obj = TranscriptWord(
                        id=f"w_{word_counter:04d}",
                        segment_id=seg_id,
                        media_id=media_id,
                        text=w.word.strip(),
                        start_ms=w_start_ms,
                        end_ms=w_end_ms,
                        confidence=round(float(w.probability), 3),
                        alignment_status=AlignmentStatus.ALIGNED,
                    )
                    seg_words.append(w_obj)
                    all_words.append(w_obj)
                    word_counter += 1
            else:
                # Interpolate if word timestamps are missing
                seg_words = WordAligner.interpolate_words_in_segment(
                    text=s.text,
                    segment_id=seg_id,
                    media_id=media_id,
                    start_ms=s_start_ms,
                    end_ms=s_end_ms,
                    start_word_index=word_counter,
                )
                all_words.extend(seg_words)
                word_counter += len(seg_words)

            all_segments.append(
                TranscriptSegment(
                    id=seg_id,
                    media_id=media_id,
                    start_ms=s_start_ms,
                    end_ms=s_end_ms,
                    text=s.text.strip(),
                    words=seg_words,
                    avg_logprob=getattr(s, "avg_logprob", None),
                    no_speech_prob=getattr(s, "no_speech_prob", None),
                )
            )

        elapsed_ms = int(round((time.perf_counter() - start_time) * 1000.0))
        rtf = elapsed_ms / float(max(1, meta.duration_ms))

        # Detect silence and speech intervals via VAD
        signal = load_wav_pcm(audio_path)
        vad = VoiceActivityDetector()
        speech_intervals, silence_intervals = vad.process(signal, media_id=media_id)

        model_info = ModelInfo(
            engine_name="faster-whisper",
            model_name=self.model_size,
            quantization=self.compute_type,
            device=self.device,
            compute_type=self.compute_type,
        )

        metadata = ProcessingMetadata(
            processing_time_ms=elapsed_ms,
            audio_duration_ms=meta.duration_ms,
            real_time_factor=round(rtf, 3),
            peak_memory_mb=None,
            sample_rate_hz=meta.sample_rate,
            channels=meta.channels,
            timestamp_utc=datetime.datetime.now(datetime.timezone.utc).isoformat(),
        )

        return TranscriptionDocument(
            schema_version=SchemaVersion.V1_0_0,
            media_id=media_id,
            status=TranscriptionStatus.COMPLETED,
            language=info.language or language,
            language_probability=round(float(info.language_probability), 3),
            duration_ms=meta.duration_ms,
            full_text=" ".join(s.text for s in all_segments),
            model_info=model_info,
            segments=all_segments,
            words=all_words,
            speech_intervals=speech_intervals,
            silence_intervals=silence_intervals,
            processing_metadata=metadata,
        )
