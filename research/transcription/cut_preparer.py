"""Pure, deterministic functions for preparing non-destructive text-based cuts.

Implements all editing operations mandated by Section 8:
- Word lookup and phrase search within transcripts
- Selection interval extraction and continuity validation
- Overlap, invalid selection, and missing timestamp detection
- Production of structured CutRequests compatible with Phase 4.1 contracts
"""

from __future__ import annotations

import re
import uuid
from typing import Any, Dict, List, Optional, Tuple

from research.transcription.contracts import (
    AlignmentStatus,
    CutDecisionState,
    CutReason,
    CutRequest,
    TranscriptSegment,
    TranscriptWord,
    TranscriptionDocument,
)


class SelectionValidationError(Exception):
    """Raised when word selection is invalid or inconsistent."""
    pass


def find_word_by_id(
    document: TranscriptionDocument,
    word_id: str,
) -> Optional[TranscriptWord]:
    """Find a word by its unique ID in the transcript document."""
    for word in document.words:
        if word.id == word_id:
            return word
    return None


def find_phrase(
    document: TranscriptionDocument,
    phrase: str,
    case_sensitive: bool = False,
) -> List[List[TranscriptWord]]:
    """Search for occurrences of a contiguous multi-word phrase across the transcript.

    Returns:
        List of matching word sequences (each sequence is a List[TranscriptWord]).
    """
    cleaned_phrase = phrase.strip()
    if not cleaned_phrase or not document.words:
        return []

    query_tokens = cleaned_phrase.split()
    if not query_tokens:
        return []

    matches: List[List[TranscriptWord]] = []
    n_tokens = len(query_tokens)
    doc_words = document.words

    # Normalize tokens for matching (strip punctuation)
    def clean_text(t: str) -> str:
        s = re.sub(r"[^\w\s]", "", t)
        return s if case_sensitive else s.lower()

    target_tokens = [clean_text(t) for t in query_tokens]

    for i in range(len(doc_words) - n_tokens + 1):
        window = doc_words[i: i + n_tokens]
        window_tokens = [clean_text(w.text) for w in window]
        if window_tokens == target_tokens:
            matches.append(list(window))

    return matches


def get_selection_interval(words: List[TranscriptWord]) -> Tuple[int, int]:
    """Extract inclusive start_ms and exclusive end_ms from an arbitrary set of words.

    Returns:
        (min(start_ms), max(end_ms))
    Raises:
        SelectionValidationError if words list is empty.
    """
    if not words:
        raise SelectionValidationError("Cannot get interval for empty word selection")

    start_ms = min(w.start_ms for w in words)
    end_ms = max(w.end_ms for w in words)

    if start_ms >= end_ms:
        raise SelectionValidationError(
            f"Selection yields invalid interval: start_ms={start_ms} >= end_ms={end_ms}"
        )

    return start_ms, end_ms


def validate_word_continuity(
    words: List[TranscriptWord],
    max_allowed_gap_ms: int = 600,
) -> Tuple[bool, List[str]]:
    """Check whether a sequence of words is strictly contiguous in time.

    Verifies:
    1. Words are sorted monotonically by start_ms.
    2. No internal gap between word[i].end_ms and word[i+1].start_ms exceeds max_allowed_gap_ms.

    Returns:
        (is_continuous, gap_warnings)
    """
    if len(words) <= 1:
        return True, []

    warnings: List[str] = []
    is_continuous = True

    for i in range(len(words) - 1):
        curr_word = words[i]
        next_word = words[i + 1]

        # Check temporal order
        if curr_word.start_ms > next_word.start_ms:
            is_continuous = False
            warnings.append(
                f"Words are out of temporal order: '{curr_word.text}' ({curr_word.start_ms}ms) "
                f"appears before '{next_word.text}' ({next_word.start_ms}ms)"
            )

        gap = next_word.start_ms - curr_word.end_ms
        if gap < 0:
            # Overlap
            warnings.append(
                f"Temporal overlap of {-gap}ms between '{curr_word.text}' and '{next_word.text}'"
            )
        elif gap > max_allowed_gap_ms:
            is_continuous = False
            warnings.append(
                f"Discontinuous gap of {gap}ms between '{curr_word.text}' and '{next_word.text}'"
            )

    return is_continuous, warnings


def detect_overlapping_intervals(
    intervals: List[Tuple[int, int]],
) -> List[Tuple[Tuple[int, int], Tuple[int, int], int]]:
    """Detect overlapping ranges in a collection of [start_ms, end_ms) intervals.

    Returns:
        List of tuples: (interval_a, interval_b, overlap_duration_ms)
    """
    overlaps: List[Tuple[Tuple[int, int], Tuple[int, int], int]] = []
    sorted_intervals = sorted(intervals, key=lambda x: (x[0], x[1]))

    for i in range(len(sorted_intervals)):
        for j in range(i + 1, len(sorted_intervals)):
            a_start, a_end = sorted_intervals[i]
            b_start, b_end = sorted_intervals[j]

            # If b starts after a ends, no further overlaps with a
            if b_start >= a_end:
                break

            overlap_start = max(a_start, b_start)
            overlap_end = min(a_end, b_end)
            overlap_dur = overlap_end - overlap_start

            if overlap_dur > 0:
                overlaps.append(((a_start, a_end), (b_start, b_end), overlap_dur))

    return overlaps


def detect_invalid_selections(
    selected_words: List[TranscriptWord],
    document: TranscriptionDocument,
) -> List[str]:
    """Validate a word selection against the parent transcript document.

    Checks:
    - Empty selection.
    - Word IDs not present in document.
    - Negative or inverted intervals.
    - Duplicate word selection.
    """
    errors: List[str] = []
    if not selected_words:
        errors.append("Selection is empty")
        return errors

    doc_word_ids = {w.id for w in document.words}
    seen_ids = set()

    for w in selected_words:
        if w.id not in doc_word_ids:
            errors.append(f"Word ID '{w.id}' does not exist in media transcript")
        if w.id in seen_ids:
            errors.append(f"Duplicate word in selection: '{w.id}'")
        seen_ids.add(w.id)

        if w.start_ms < 0 or w.end_ms < 0:
            errors.append(f"Word '{w.id}' has negative timestamp: [{w.start_ms}, {w.end_ms}]")
        if w.start_ms >= w.end_ms:
            errors.append(f"Word '{w.id}' has inverted or zero bounds: [{w.start_ms}, {w.end_ms}]")

    return errors


def detect_words_without_timestamp(
    words: List[TranscriptWord],
) -> List[TranscriptWord]:
    """Identify words missing valid timestamps (start_ms is None or invalid)."""
    missing: List[TranscriptWord] = []
    for w in words:
        if (
            w.start_ms is None
            or w.end_ms is None
            or w.alignment_status == AlignmentStatus.FAILED
        ):
            missing.append(w)
    return missing


def produce_cut_request(
    media_id: str,
    selected_words: List[TranscriptWord],
    reason: CutReason = CutReason.HESITATION,
    confidence: float = 1.0,
    request_metadata: Optional[Dict[str, Any]] = None,
) -> CutRequest:
    """Generate a fully validated, non-destructive CutRequest for the editing workspace.

    Args:
        media_id: Media asset identifier
        selected_words: Words marked for removal
        reason: Justification for cut (hesitation, silence, repetition, filler_word, etc.)
        confidence: Algorithmic or heuristic confidence in this cut suggestion
        request_metadata: Optional contextual metadata

    Returns:
        Structured CutRequest object
    """
    if not selected_words:
        raise SelectionValidationError("Cannot produce cut request from empty selection")

    # Sort words by start_ms to ensure proper order
    sorted_words = sorted(selected_words, key=lambda w: w.start_ms)
    start_ms, end_ms = get_selection_interval(sorted_words)
    word_ids = [w.id for w in sorted_words]
    selected_text = " ".join(w.text for w in sorted_words)

    # Check continuity and alignment warnings
    is_continuous, gap_warnings = validate_word_continuity(sorted_words)
    alignment_warnings: List[str] = list(gap_warnings)

    for w in sorted_words:
        if w.alignment_status in (AlignmentStatus.INTERPOLATED, AlignmentStatus.LOW_CONFIDENCE):
            alignment_warnings.append(
                f"Word '{w.text}' ({w.id}) has alignment status '{w.alignment_status.value}'"
            )
        if w.confidence < 0.6:
            alignment_warnings.append(
                f"Word '{w.text}' ({w.id}) has low acoustic confidence ({w.confidence})"
            )

    req_id = f"cut_{uuid.uuid4().hex[:8]}"

    return CutRequest(
        request_id=req_id,
        media_id=media_id,
        reason=reason,
        start_ms=start_ms,
        end_ms=end_ms,
        duration_ms=end_ms - start_ms,
        word_ids=word_ids,
        selected_text=selected_text,
        alignment_warnings=alignment_warnings,
        decision_state=CutDecisionState.PENDING,
        confidence=confidence,
    )
