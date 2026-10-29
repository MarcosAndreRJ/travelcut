"""Silence detection and acoustic gap analysis tailored for travel video editing.

Implements taxonomy:
- Vocal silence vs ambient sound
- Inter-word pauses vs sentence boundaries vs awkward dead air
- Travel scene preservation heuristics (scenery, road, drone, waterfall, ambient, reaction)
- Generation of non-destructive CutSuggestion structures compatible with Phase 4.1
"""

from __future__ import annotations

from dataclasses import dataclass
from typing import Dict, List, Optional, Tuple

from research.transcription.contracts import (
    CutDecisionState,
    CutReason,
    SilenceCategory,
    SilenceInterval,
    SpeechInterval,
)


@dataclass
class SilenceAnalysisSummary:
    media_id: str
    total_audio_ms: int
    total_speech_ms: int
    total_silence_ms: int
    speech_ratio: float
    silence_ratio: float
    removable_silence_ms: int
    potential_savings_percent: float
    removable_candidates_count: int
    category_breakdown_ms: Dict[str, int]


class SilenceDetector:
    """Detects, classifies, and suggests editorial cuts for non-vocal audio intervals."""

    def __init__(
        self,
        min_word_pause_ms: int = 150,
        min_sentence_pause_ms: int = 400,
        min_removable_gap_ms: int = 1200,
        edge_silence_threshold_ms: int = 800,
    ):
        self.min_word_pause_ms = min_word_pause_ms
        self.min_sentence_pause_ms = min_sentence_pause_ms
        self.min_removable_gap_ms = min_removable_gap_ms
        self.edge_silence_threshold_ms = edge_silence_threshold_ms

    def analyze(
        self,
        silence_intervals: List[SilenceInterval],
        total_audio_ms: int,
        media_id: str,
        video_category_hint: Optional[str] = None,
    ) -> Tuple[List[SilenceInterval], SilenceAnalysisSummary]:
        """Refine silence intervals with travel context hints and produce summary metrics."""
        classified_intervals: List[SilenceInterval] = []
        category_breakdown: Dict[str, int] = {cat.value: 0 for cat in SilenceCategory}
        total_silence_ms = 0
        removable_ms = 0
        removable_count = 0

        for sil in silence_intervals:
            dur = sil.duration_ms
            total_silence_ms += dur
            cat = sil.category
            is_removable = sil.is_removable_candidate
            note = sil.recommendation_note

            # Apply TravelCut contextual overrides if video category hint is provided
            if video_category_hint:
                hint_lower = video_category_hint.lower()
                if "drone" in hint_lower or "aéreo" in hint_lower:
                    cat = SilenceCategory.DRONE
                    is_removable = False
                    note = "Trecho aéreo de drone sem fala. Preservar áudio ambiente / trilha para contemplação."
                elif "b-roll" in hint_lower or "paisagem" in hint_lower:
                    cat = SilenceCategory.SCENERY
                    is_removable = False
                    note = "B-roll contemplativo de paisagem natural. Recomendado manter para ambientação."
                elif "cachoeira" in hint_lower or "água" in hint_lower:
                    cat = SilenceCategory.WATERFALL
                    is_removable = False
                    note = "Som acústico natural de cachoeira/água corrente. Preservar textura sonora."
                elif "deslocamento" in hint_lower or "estrada" in hint_lower:
                    cat = SilenceCategory.ROAD
                    is_removable = False
                    note = "Áudio de deslocamento / viagem em estrada. Preservar para montagem de transição."

            category_breakdown[cat.value] = category_breakdown.get(cat.value, 0) + dur
            if is_removable:
                removable_ms += dur
                removable_count += 1

            classified_intervals.append(
                SilenceInterval(
                    id=sil.id,
                    media_id=media_id,
                    start_ms=sil.start_ms,
                    end_ms=sil.end_ms,
                    duration_ms=dur,
                    category=cat,
                    is_removable_candidate=is_removable,
                    recommendation_note=note,
                    ambient_noise_level_db=sil.ambient_noise_level_db,
                )
            )

        total_speech_ms = max(0, total_audio_ms - total_silence_ms)
        speech_ratio = (total_speech_ms / float(total_audio_ms)) if total_audio_ms > 0 else 0.0
        silence_ratio = (total_silence_ms / float(total_audio_ms)) if total_audio_ms > 0 else 0.0
        potential_savings = (removable_ms / float(total_audio_ms) * 100.0) if total_audio_ms > 0 else 0.0

        summary = SilenceAnalysisSummary(
            media_id=media_id,
            total_audio_ms=total_audio_ms,
            total_speech_ms=total_speech_ms,
            total_silence_ms=total_silence_ms,
            speech_ratio=round(speech_ratio, 3),
            silence_ratio=round(silence_ratio, 3),
            removable_silence_ms=removable_ms,
            potential_savings_percent=round(potential_savings, 2),
            removable_candidates_count=removable_count,
            category_breakdown_ms=category_breakdown,
        )

        return classified_intervals, summary

    @staticmethod
    def generate_cut_suggestions(
        silence_intervals: List[SilenceInterval],
        media_id: str,
    ) -> List[Dict]:
        """Convert removable silence intervals into Phase 4.1 CutSuggestion dictionaries."""
        suggestions = []
        for sil in silence_intervals:
            if not sil.is_removable_candidate:
                continue

            dur_sec = sil.duration_ms / 1000.0
            suggestions.append(
                {
                    "id": f"cut_{sil.id}",
                    "mediaId": media_id,
                    "clipTitle": f"Silêncio {sil.category.value}",
                    "startSec": round(sil.start_ms / 1000.0, 3),
                    "endSec": round(sil.end_ms / 1000.0, 3),
                    "durationSec": round(dur_sec, 3),
                    "reason": "silence",
                    "reasonLabel": f"Silêncio Inútil ({dur_sec:.1f}s)",
                    "confidence": 0.95,
                    "decisionState": "pending",
                    "previewText": f"[{sil.recommendation_note}]",
                    "timecodeFormatted": f"{sil.start_ms}ms -> {sil.end_ms}ms",
                    "sourceRange": {
                        "start": sil.start_ms,
                        "end": sil.end_ms,
                    },
                }
            )
        return suggestions
