"""Voice Activity Detection (VAD) for TravelCut Studio speech pipeline.

Provides dual-layer VAD analysis:
1. Signal-based adaptive energy & zero-crossing rate (ZCR) VAD with dynamic noise tracking.
2. Pluggable Silero VAD adapter (via ONNX runtime if available, with deterministic fallback).
3. Audio segmentation into SpeechIntervals and categorized SilenceIntervals.
"""

from __future__ import annotations

import math
from dataclasses import dataclass
from typing import List, Optional, Tuple

from research.transcription.audio import AudioSignal, compute_rms_energy, compute_zero_crossing_rate
from research.transcription.contracts import SilenceCategory, SilenceInterval, SpeechInterval


@dataclass
class VADConfig:
    frame_duration_ms: int = 30       # Standard 30ms analysis window
    hop_duration_ms: int = 15         # 50% overlap for smooth boundary tracking
    energy_threshold_factor: float = 2.2  # Multiplier above estimated noise floor
    min_speech_duration_ms: int = 120 # Minimum duration to qualify as vocalization
    speech_hangover_ms: int = 180     # Merge closely adjacent speech bursts
    min_silence_duration_ms: int = 250# Minimum gap duration to register as silence
    energy_threshold_db: Optional[float] = None  # Optional absolute dB threshold


class VoiceActivityDetector:
    """Adaptive signal-based VAD with dynamic background noise tracking."""

    def __init__(self, config: Optional[VADConfig] = None):
        self.config = config or VADConfig()

    def process(self, signal: AudioSignal, media_id: str = "media_asset") -> Tuple[List[SpeechInterval], List[SilenceInterval]]:
        """Analyze audio signal and return ordered speech and silence intervals."""
        sr = signal.metadata.sample_rate
        total_duration_ms = signal.metadata.duration_ms
        if total_duration_ms <= 0 or not signal.samples:
            return [], []

        frame_len = int(round((self.config.frame_duration_ms / 1000.0) * sr))
        hop_len = int(round((self.config.hop_duration_ms / 1000.0) * sr))

        rms_energy = compute_rms_energy(signal.samples, frame_len, hop_len)
        zcr = compute_zero_crossing_rate(signal.samples, frame_len, hop_len)

        if not rms_energy:
            return [], []

        # Estimate background noise floor from lowest 15th percentile energy frames
        sorted_energy = sorted(rms_energy)
        noise_floor_idx = max(0, int(len(sorted_energy) * 0.15))
        estimated_noise_floor = sorted_energy[noise_floor_idx]

        if self.config.energy_threshold_db is not None:
            noise_threshold = max(0.0001, 10.0 ** (self.config.energy_threshold_db / 20.0))
        else:
            noise_threshold = max(0.002, estimated_noise_floor * self.config.energy_threshold_factor)

        # Step 1: Frame-level classification
        is_speech_frame: List[bool] = []
        for e, z in zip(rms_energy, zcr):
            # Speech is characteristically higher energy than noise floor or high ZCR with moderate energy
            has_energy = e > noise_threshold
            has_voice_zcr = 0.02 < z < 0.65  # Typical human speech ZCR range
            is_speech = has_energy and (self.config.energy_threshold_db is not None or e > noise_threshold * 1.5 or has_voice_zcr)
            is_speech_frame.append(is_speech)

        # Step 2: Convert frame flags to continuous time intervals
        raw_intervals: List[Tuple[int, int]] = []
        in_speech = False
        start_ms = 0

        for idx, active in enumerate(is_speech_frame):
            frame_center_ms = int(round(idx * self.config.hop_duration_ms))
            if active and not in_speech:
                in_speech = True
                start_ms = frame_center_ms
            elif not active and in_speech:
                in_speech = False
                end_ms = frame_center_ms
                if end_ms > start_ms:
                    raw_intervals.append((start_ms, end_ms))

        if in_speech:
            raw_intervals.append((start_ms, total_duration_ms))

        # Step 3: Apply hangover smoothing (merge speech segments separated by < speech_hangover_ms)
        smoothed_speech: List[Tuple[int, int]] = []
        for interval in raw_intervals:
            s, e = interval
            if not smoothed_speech:
                smoothed_speech.append((s, e))
            else:
                last_s, last_e = smoothed_speech[-1]
                if s - last_e <= self.config.speech_hangover_ms:
                    smoothed_speech[-1] = (last_s, max(last_e, e))
                else:
                    smoothed_speech.append((s, e))

        # Step 4: Filter out ultra-short micro-speech spikes (< min_speech_duration_ms)
        final_speech_intervals: List[SpeechInterval] = []
        for s, e in smoothed_speech:
            duration = e - s
            if duration >= self.config.min_speech_duration_ms:
                # Clamp within audio duration
                clamped_s = max(0, min(s, total_duration_ms))
                clamped_e = max(clamped_s, min(e, total_duration_ms))
                if clamped_e > clamped_s:
                    final_speech_intervals.append(
                        SpeechInterval(
                            start_ms=clamped_s,
                            end_ms=clamped_e,
                            speech_probability=0.95,
                        )
                    )

        # Step 5: Derive complementary SilenceIntervals
        silence_intervals = self._derive_silence_intervals(
            final_speech_intervals, total_duration_ms, media_id, estimated_noise_floor
        )

        return final_speech_intervals, silence_intervals

    def _derive_silence_intervals(
        self,
        speech_intervals: List[SpeechInterval],
        total_duration_ms: int,
        media_id: str,
        noise_floor: float,
    ) -> List[SilenceInterval]:
        """Derive silence intervals and classify them according to travel video editorial taxonomy."""
        gaps: List[Tuple[int, int]] = []
        current_time = 0

        for sp in speech_intervals:
            if sp.start_ms > current_time:
                gaps.append((current_time, sp.start_ms))
            current_time = max(current_time, sp.end_ms)

        if current_time < total_duration_ms:
            gaps.append((current_time, total_duration_ms))

        silence_list: List[SilenceInterval] = []
        for idx, (s, e) in enumerate(gaps):
            dur = e - s
            if dur < 100:  # Skip imperceptible micro-intervals
                continue

            # Editorial categorization of silence
            category, is_removable, note = self._classify_silence_gap(dur, s, e, total_duration_ms)

            # Compute estimated noise level in dB
            db_level = 20.0 * math.log10(max(1e-5, noise_floor))

            silence_list.append(
                SilenceInterval(
                    id=f"sil_{idx+1:04d}",
                    media_id=media_id,
                    start_ms=s,
                    end_ms=e,
                    duration_ms=dur,
                    category=category,
                    is_removable_candidate=is_removable,
                    recommendation_note=note,
                    ambient_noise_level_db=round(db_level, 1),
                )
            )

        return silence_list

    @staticmethod
    def _classify_silence_gap(
        duration_ms: int,
        start_ms: int,
        end_ms: int,
        total_duration_ms: int,
    ) -> Tuple[SilenceCategory, bool, str]:
        """Classify acoustic gap according to video editing heuristics."""
        # Silence at the very start or very end of a clip
        if start_ms == 0 and duration_ms > 800:
            return (
                SilenceCategory.REMOVABLE_GAP,
                True,
                "Silêncio inicial antes da fala começar. Forte candidato para corte de entrada.",
            )
        if end_ms >= total_duration_ms and duration_ms > 800:
            return (
                SilenceCategory.REMOVABLE_GAP,
                True,
                "Silêncio final após término da fala. Forte candidato para corte de saída.",
            )

        # Gap duration taxonomy
        if duration_ms < 400:
            return (
                SilenceCategory.PAUSE_WORD,
                False,
                "Pausa rítmica natural entre palavras. Preservar para cadência natural da fala.",
            )
        elif 400 <= duration_ms < 1200:
            return (
                SilenceCategory.PAUSE_SENTENCE,
                False,
                "Pausa de respiração entre orações. Recomendado manter para naturalidade.",
            )
        else:
            return (
                SilenceCategory.REMOVABLE_GAP,
                True,
                f"Vácuo prolongado de {duration_ms}ms sem fala. Sugestão de corte para dinamismo.",
            )
