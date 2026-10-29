"""Unit tests for word alignment, timestamp normalization, and interpolation."""

import pytest

from research.transcription.alignment import WordAligner
from research.transcription.contracts import (
    AlignmentStatus,
    TranscriptSegment,
    TranscriptWord,
)


def test_normalize_word_timestamps_order_and_clamping():
    raw_words = [
        TranscriptWord(
            id="w_1",
            segment_id="s_1",
            media_id="m_1",
            text="Trilha",
            start_ms=100,
            end_ms=400,
            confidence=0.95,
        ),
        # Overlapping slightly with previous word (380 < 400)
        TranscriptWord(
            id="w_2",
            segment_id="s_1",
            media_id="m_1",
            text="linda",
            start_ms=380,
            end_ms=750,
            confidence=0.92,
        ),
        # Low confidence word
        TranscriptWord(
            id="w_3",
            segment_id="s_1",
            media_id="m_1",
            text="demais",
            start_ms=760,
            end_ms=1200,
            confidence=0.35,
        ),
    ]

    normalized, warnings = WordAligner.normalize_word_timestamps(
        raw_words=raw_words,
        segment_start_ms=100,
        segment_end_ms=1200,
        audio_duration_ms=1500,
    )

    assert len(normalized) == 3
    # First word unmodified
    assert normalized[0].start_ms == 100
    assert normalized[0].end_ms == 400
    assert normalized[0].alignment_status == AlignmentStatus.ALIGNED

    # Second word clamped to prev_end (400)
    assert normalized[1].start_ms == 400
    assert normalized[1].end_ms == 750
    assert normalized[1].alignment_status == AlignmentStatus.INTERPOLATED

    # Third word marked as LOW_CONFIDENCE due to confidence < 0.4
    assert normalized[2].start_ms == 760
    assert normalized[2].alignment_status == AlignmentStatus.LOW_CONFIDENCE


def test_interpolate_words_in_segment():
    text = "Cachoeira espetacular no meio da serra"
    seg_start = 1000
    seg_end = 3500

    words = WordAligner.interpolate_words_in_segment(
        text=text,
        segment_id="seg_01",
        media_id="med_01",
        start_ms=seg_start,
        end_ms=seg_end,
    )

    tokens = text.split()
    assert len(words) == len(tokens)
    assert [w.text for w in words] == tokens

    # Verify all words have INTERPOLATED status
    for w in words:
        assert w.alignment_status == AlignmentStatus.INTERPOLATED
        assert w.start_ms >= seg_start
        assert w.end_ms <= seg_end
        assert w.start_ms < w.end_ms

    # Verify monotonicity
    for i in range(len(words) - 1):
        assert words[i].end_ms <= words[i + 1].start_ms

    # Last word reaches segment end
    assert words[-1].end_ms == seg_end


def test_detect_alignment_drift():
    words = [
        TranscriptWord(
            id="w_1",
            segment_id="s_1",
            media_id="m_1",
            text="Bom",
            start_ms=100,
            end_ms=500,
            confidence=0.9,
        ),
        TranscriptWord(
            id="w_2",
            segment_id="s_1",
            media_id="m_1",
            text="dia",
            start_ms=550,
            end_ms=900,
            confidence=0.9,
        ),
    ]

    # Normal segment with low drift
    normal_seg = TranscriptSegment(
        id="s_1",
        media_id="m_1",
        start_ms=80,
        end_ms=920,
        text="Bom dia",
        words=words,
    )
    has_drift, drift_ms, _ = WordAligner.detect_alignment_drift(words, normal_seg, max_allowed_drift_ms=300)
    assert has_drift is False
    assert drift_ms <= 300

    # Drifted segment
    drifted_seg = TranscriptSegment(
        id="s_1",
        media_id="m_1",
        start_ms=800,
        end_ms=1600,
        text="Bom dia",
        words=words,
    )
    has_drift, drift_ms, detail = WordAligner.detect_alignment_drift(words, drifted_seg, max_allowed_drift_ms=300)
    assert has_drift is True
    assert drift_ms > 300
    assert "Max boundary drift" in detail
