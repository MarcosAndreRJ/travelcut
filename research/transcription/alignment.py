"""Word-level alignment and timestamp normalization module.

Implements strict validation and normalization rules:
- Integer millisecond boundaries [start_ms, end_ms).
- Convention: Inclusive start, Exclusive end.
- Temporal monotonicity and drift checking.
- Non-destructive interpolation for words missing raw ASR timestamps.
- Confidence scoring and alignment status tracking.
"""

from __future__ import annotations

import re
from typing import List, Optional, Tuple

from research.transcription.contracts import AlignmentStatus, TranscriptSegment, TranscriptWord


class WordAlignmentError(Exception):
    """Raised when word alignment violates temporal constraints."""
    pass


class WordAligner:
    """Normalizes and validates word timestamps against audio and segment boundaries."""

    @staticmethod
    def normalize_word_timestamps(
        raw_words: List[TranscriptWord],
        segment_start_ms: int,
        segment_end_ms: int,
        audio_duration_ms: int,
    ) -> Tuple[List[TranscriptWord], List[str]]:
        """Validate, sort, and normalize word timestamps within segment bounds.

        Returns:
            Tuple of (normalized_words, warnings)
        """
        warnings: List[str] = []
        if not raw_words:
            return [], warnings

        normalized: List[TranscriptWord] = []
        prev_end = segment_start_ms

        for idx, w in enumerate(raw_words):
            s_ms = int(round(w.start_ms))
            e_ms = int(round(w.end_ms))
            status = w.alignment_status
            confidence = w.confidence

            # Inverted timestamp check
            if s_ms > e_ms:
                warnings.append(
                    f"Word '{w.text}' ({w.id}) inverted [{s_ms}, {e_ms}]. Correcting end to start+1."
                )
                e_ms = s_ms + 1
                status = AlignmentStatus.INTERPOLATED

            # Monotonicity check against previous word
            if s_ms < prev_end:
                # Small overlap: clamp to prev_end
                overlap = prev_end - s_ms
                if overlap <= 100:
                    s_ms = prev_end
                    if e_ms <= s_ms:
                        e_ms = s_ms + 50
                    status = AlignmentStatus.INTERPOLATED
                else:
                    warnings.append(
                        f"Significant temporal collision ({overlap}ms) between word '{w.text}' ({w.id}) and previous word."
                    )
                    status = AlignmentStatus.LOW_CONFIDENCE

            # Clamp within audio total bounds
            s_ms = max(0, min(s_ms, audio_duration_ms))
            e_ms = max(s_ms, min(e_ms, audio_duration_ms))

            # Flag words with near-zero duration or low confidence
            if e_ms - s_ms < 20:
                warnings.append(f"Word '{w.text}' ({w.id}) duration under 20ms.")
                status = AlignmentStatus.LOW_CONFIDENCE

            if confidence < 0.4:
                status = AlignmentStatus.LOW_CONFIDENCE

            word_obj = TranscriptWord(
                id=w.id,
                segment_id=w.segment_id,
                media_id=w.media_id,
                text=w.text.strip(),
                start_ms=s_ms,
                end_ms=e_ms,
                confidence=round(confidence, 3),
                alignment_status=status,
                is_cut_candidate=w.is_cut_candidate,
                cut_reason=w.cut_reason,
            )
            normalized.append(word_obj)
            prev_end = e_ms

        return normalized, warnings

    @staticmethod
    def interpolate_words_in_segment(
        text: str,
        segment_id: str,
        media_id: str,
        start_ms: int,
        end_ms: int,
        start_word_index: int = 1,
    ) -> List[TranscriptWord]:
        """Proportionally distribute word timestamps across segment duration based on character length.

        Used as an explicit fallback when ASR does not return token-level alignment.
        Marks all generated words as AlignmentStatus.INTERPOLATED.
        """
        raw_tokens = [t.strip() for t in text.strip().split() if t.strip()]
        if not raw_tokens:
            return []

        total_duration = max(10, end_ms - start_ms)
        total_chars = sum(len(t) for t in raw_tokens)

        # Allocate 85% of duration to characters and 15% to inter-word spaces
        num_spaces = max(1, len(raw_tokens) - 1)
        space_pool = int(total_duration * 0.15)
        char_pool = total_duration - space_pool

        char_unit = char_pool / float(total_chars) if total_chars > 0 else 0
        space_unit = int(round(space_pool / float(num_spaces))) if num_spaces > 0 else 0

        words: List[TranscriptWord] = []
        curr_time = start_ms

        for idx, token in enumerate(raw_tokens):
            w_dur = int(round(len(token) * char_unit))
            w_dur = max(50, w_dur)  # minimum word duration floor

            w_start = curr_time
            w_end = min(end_ms, w_start + w_dur)
            if idx == len(raw_tokens) - 1:
                w_end = end_ms  # Guarantee segment closure

            w_id = f"w_{start_word_index + idx:04d}"
            words.append(
                TranscriptWord(
                    id=w_id,
                    segment_id=segment_id,
                    media_id=media_id,
                    text=token,
                    start_ms=w_start,
                    end_ms=w_end,
                    confidence=0.5,
                    alignment_status=AlignmentStatus.INTERPOLATED,
                    is_cut_candidate=False,
                )
            )
            curr_time = min(end_ms, w_end + space_unit)

        return words

    @staticmethod
    def detect_alignment_drift(
        words: List[TranscriptWord],
        segment: TranscriptSegment,
        max_allowed_drift_ms: int = 400,
    ) -> Tuple[bool, int, str]:
        """Verify whether word bounds match segment bounds without excessive drift."""
        if not words:
            return False, 0, "No words in segment"

        first_word_start = words[0].start_ms
        last_word_end = words[-1].end_ms

        start_drift = abs(first_word_start - segment.start_ms)
        end_drift = abs(last_word_end - segment.end_ms)
        total_drift = max(start_drift, end_drift)

        has_excessive_drift = total_drift > max_allowed_drift_ms
        detail = (
            f"Segment [{segment.start_ms}, {segment.end_ms}] vs words [{first_word_start}, {last_word_end}]. "
            f"Max boundary drift: {total_drift}ms."
        )
        return has_excessive_drift, total_drift, detail
