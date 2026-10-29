"""Unit tests for Section 8 cut preparation functions."""

import pytest

from research.transcription.contracts import (
    AlignmentStatus,
    CutDecisionState,
    CutReason,
    TranscriptSegment,
    TranscriptWord,
    TranscriptionDocument,
)
from research.transcription.cut_preparer import (
    SelectionValidationError,
    detect_invalid_selections,
    detect_overlapping_intervals,
    detect_words_without_timestamp,
    find_phrase,
    find_word_by_id,
    get_selection_interval,
    produce_cut_request,
    validate_word_continuity,
)


@pytest.fixture
def sample_document():
    words = [
        TranscriptWord(id="w1", segment_id="s1", media_id="m1", text="Hoje", start_ms=100, end_ms=300),
        TranscriptWord(id="w2", segment_id="s1", media_id="m1", text="vamos", start_ms=320, end_ms=600),
        TranscriptWord(id="w3", segment_id="s1", media_id="m1", text="visitar", start_ms=620, end_ms=900),
        TranscriptWord(id="w4", segment_id="s1", media_id="m1", text="a", start_ms=920, end_ms=1000),
        TranscriptWord(id="w5", segment_id="s1", media_id="m1", text="praia", start_ms=1020, end_ms=1300),
        TranscriptWord(id="w6", segment_id="s1", media_id="m1", text="do", start_ms=1320, end_ms=1400),
        TranscriptWord(id="w7", segment_id="s1", media_id="m1", text="sono", start_ms=1420, end_ms=1700),
    ]
    seg = TranscriptSegment(id="s1", media_id="m1", start_ms=100, end_ms=1700, text="Hoje vamos visitar a praia do sono", words=words)
    return TranscriptionDocument(
        schema_version="1.0.0",
        media_id="m1",
        status="completed",
        language="pt",
        duration_ms=2000,
        full_text="Hoje vamos visitar a praia do sono",
        segments=[seg],
        words=words,
    )


def test_find_word_by_id(sample_document):
    w = find_word_by_id(sample_document, "w3")
    assert w is not None
    assert w.text == "visitar"

    missing = find_word_by_id(sample_document, "w999")
    assert missing is None


def test_find_phrase(sample_document):
    # Contiguous matching
    matches = find_phrase(sample_document, "praia do sono")
    assert len(matches) == 1
    assert [w.id for w in matches[0]] == ["w5", "w6", "w7"]

    # Case insensitive
    matches_upper = find_phrase(sample_document, "PRAIA DO SONO", case_sensitive=False)
    assert len(matches_upper) == 1

    # Non-existent phrase
    no_matches = find_phrase(sample_document, "praia grande")
    assert len(no_matches) == 0


def test_get_selection_interval(sample_document):
    selection = [sample_document.words[1], sample_document.words[3]]  # "vamos", "a"
    start_ms, end_ms = get_selection_interval(selection)
    assert start_ms == 320
    assert end_ms == 1000

    with pytest.raises(SelectionValidationError):
        get_selection_interval([])


def test_validate_word_continuity():
    w1 = TranscriptWord(id="w1", segment_id="s1", media_id="m1", text="A", start_ms=100, end_ms=300)
    w2 = TranscriptWord(id="w2", segment_id="s1", media_id="m1", text="B", start_ms=350, end_ms=500)
    w3_gap = TranscriptWord(id="w3", segment_id="s1", media_id="m1", text="C", start_ms=1500, end_ms=1800)

    # Continuous sequence
    is_cont, warnings = validate_word_continuity([w1, w2], max_allowed_gap_ms=500)
    assert is_cont is True
    assert len(warnings) == 0

    # Large gap between w2 and w3
    is_cont_gap, warnings_gap = validate_word_continuity([w1, w2, w3_gap], max_allowed_gap_ms=500)
    assert is_cont_gap is False
    assert any("Discontinuous gap" in w for w in warnings_gap)


def test_detect_overlapping_intervals():
    intervals = [
        (100, 500),
        (400, 800),  # overlaps with (100, 500) by 100ms
        (900, 1200),
    ]
    overlaps = detect_overlapping_intervals(intervals)
    assert len(overlaps) == 1
    int_a, int_b, dur = overlaps[0]
    assert int_a == (100, 500)
    assert int_b == (400, 800)
    assert dur == 100


def test_detect_invalid_selections(sample_document):
    # Valid selection
    assert detect_invalid_selections([sample_document.words[0]], sample_document) == []

    # Word not in document
    foreign_word = TranscriptWord(id="fake_id", segment_id="s1", media_id="m1", text="x", start_ms=10, end_ms=50)
    errors = detect_invalid_selections([foreign_word], sample_document)
    assert any("does not exist" in e for e in errors)

    # Inverted timestamps
    bad_time_word = TranscriptWord(id="w1", segment_id="s1", media_id="m1", text="x", start_ms=50, end_ms=50)
    errors_bad_time = detect_invalid_selections([bad_time_word], sample_document)
    assert any("inverted or zero bounds" in e for e in errors_bad_time)


def test_produce_cut_request(sample_document):
    selection = [sample_document.words[1], sample_document.words[2]]  # "vamos visitar"
    req = produce_cut_request(
        media_id="m1",
        selected_words=selection,
        reason=CutReason.REPETITION,
        confidence=0.92,
    )

    assert req.media_id == "m1"
    assert req.reason == CutReason.REPETITION
    assert req.start_ms == 320
    assert req.end_ms == 900
    assert req.duration_ms == 580
    assert req.word_ids == ["w2", "w3"]
    assert req.selected_text == "vamos visitar"
    assert req.decision_state == CutDecisionState.PENDING
    assert req.confidence == 0.92
