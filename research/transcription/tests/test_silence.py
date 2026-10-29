"""Unit tests for VAD, silence detection taxonomy, travel scene preservation, and metrics."""

import pytest

from research.transcription.audio import AudioMetadata, AudioSignal
from research.transcription.contracts import (
    SilenceCategory,
    SilenceInterval,
    SpeechInterval,
)
from research.transcription.silence_detector import SilenceDetector
from research.transcription.vad import VADConfig, VoiceActivityDetector


def test_vad_processing_synthetic_speech_and_silence():
    # 3 seconds total: 0-1s silence, 1-2s speech, 2-3s silence at 16kHz
    sr = 16000
    samples = []
    # 1 sec silence (amplitude 0)
    samples.extend([0.0] * 16000)
    # 1 sec tone (amplitude 0.5)
    import math
    for i in range(16000):
        samples.append(0.5 * math.sin(2.0 * math.pi * 200.0 * (i / 16000.0)))
    # 1 sec silence
    samples.extend([0.0] * 16000)

    signal = AudioSignal(
        metadata=AudioMetadata(
            path="synthetic_vad_test.wav",
            duration_ms=3000,
            sample_rate=16000,
            channels=1,
            sample_width_bytes=2,
            total_frames=48000,
            file_size_bytes=96044,
        ),
        samples=samples,
    )

    vad = VoiceActivityDetector(
        config=VADConfig(
            energy_threshold_db=-35.0,
            frame_duration_ms=30,
            min_speech_duration_ms=200,
            min_silence_duration_ms=300,
        )
    )

    speech_intervals, silence_intervals = vad.process(signal, media_id="test_media")

    assert len(speech_intervals) >= 1
    # Speech should be approximately centered around 1000ms - 2000ms
    speech = speech_intervals[0]
    assert 800 <= speech.start_ms <= 1200
    assert 1800 <= speech.end_ms <= 2200

    assert len(silence_intervals) >= 2


def test_silence_taxonomy_and_travel_context():
    detector = SilenceDetector(
        min_word_pause_ms=150,
        min_sentence_pause_ms=400,
        min_removable_gap_ms=1200,
        edge_silence_threshold_ms=800,
    )

    intervals = [
        # Short word pause (100ms)
        SilenceInterval(
            id="s1",
            media_id="vid_1",
            start_ms=500,
            end_ms=600,
            duration_ms=100,
            category=SilenceCategory.PAUSE_WORD,
            is_removable_candidate=False,
        ),
        # Long dead air (1800ms)
        SilenceInterval(
            id="s2",
            media_id="vid_1",
            start_ms=2000,
            end_ms=3800,
            duration_ms=1800,
            category=SilenceCategory.REMOVABLE_GAP,
            is_removable_candidate=True,
            recommendation_note="Pausa excessiva",
        ),
    ]

    classified, summary = detector.analyze(
        silence_intervals=intervals,
        total_audio_ms=5000,
        media_id="vid_1",
        video_category_hint=None,
    )

    assert len(classified) == 2
    assert classified[1].is_removable_candidate is True
    assert summary.removable_silence_ms == 1800
    assert summary.potential_savings_percent == (1800 / 5000.0) * 100.0

    # Test travel drone contextual override
    drone_classified, drone_summary = detector.analyze(
        silence_intervals=intervals,
        total_audio_ms=5000,
        media_id="vid_1",
        video_category_hint="drone",
    )
    for sil in drone_classified:
        assert sil.category == SilenceCategory.DRONE
        assert sil.is_removable_candidate is False
    assert drone_summary.removable_silence_ms == 0


def test_generate_cut_suggestions_compatibility():
    intervals = [
        SilenceInterval(
            id="s_rem",
            media_id="m_cut",
            start_ms=3000,
            end_ms=4500,
            duration_ms=1500,
            category=SilenceCategory.REMOVABLE_GAP,
            is_removable_candidate=True,
            recommendation_note="Vácuo de 1.5s entre falas",
        )
    ]
    suggestions = SilenceDetector.generate_cut_suggestions(intervals, media_id="m_cut")

    assert len(suggestions) == 1
    s = suggestions[0]
    assert s["id"] == "cut_s_rem"
    assert s["startSec"] == 3.0
    assert s["endSec"] == 4.5
    assert s["durationSec"] == 1.5
    assert s["reason"] == "silence"
    assert s["decisionState"] == "pending"
