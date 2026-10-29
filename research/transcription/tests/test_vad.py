"""Unit tests for Voice Activity Detection (VAD) module."""

import math
import pytest

from research.transcription.audio import AudioMetadata, AudioSignal
from research.transcription.contracts import SilenceCategory
from research.transcription.vad import VADConfig, VoiceActivityDetector


def create_signal(samples, sample_rate=16000):
    duration_ms = int((len(samples) / sample_rate) * 1000)
    return AudioSignal(
        metadata=AudioMetadata(
            path="test_signal.wav",
            duration_ms=duration_ms,
            sample_rate=sample_rate,
            channels=1,
            sample_width_bytes=2,
            total_frames=len(samples),
            file_size_bytes=len(samples) * 2 + 44,
        ),
        samples=samples,
    )


def test_vad_empty_signal():
    vad = VoiceActivityDetector()
    empty_sig = create_signal([])
    speech, silence = vad.process(empty_sig)
    assert speech == []
    assert silence == []


def test_vad_all_silence():
    sr = 16000
    # 2 seconds of pure zero silence
    samples = [0.0] * (sr * 2)
    sig = create_signal(samples, sr)

    vad = VoiceActivityDetector(config=VADConfig(energy_threshold_db=-40.0))
    speech, silence = vad.process(sig)

    assert speech == []
    assert len(silence) == 1
    assert silence[0].duration_ms == 2000
    assert silence[0].category == SilenceCategory.REMOVABLE_GAP


def test_vad_speech_burst_and_pauses():
    sr = 16000
    samples = []

    # 500ms silence
    samples.extend([0.0] * int(sr * 0.5))

    # 1.0s speech tone at 250Hz with amplitude 0.7
    for i in range(sr):
        samples.append(0.7 * math.sin(2.0 * math.pi * 250.0 * (i / sr)))

    # 1.5s trailing silence
    samples.extend([0.0] * int(sr * 1.5))

    sig = create_signal(samples, sr)
    vad = VoiceActivityDetector(
        config=VADConfig(
            energy_threshold_db=-30.0,
            frame_duration_ms=30,
            hop_duration_ms=15,
            min_speech_duration_ms=100,
            speech_hangover_ms=150,
        )
    )

    speech, silence = vad.process(sig, media_id="test_clip")

    assert len(speech) == 1
    sp = speech[0]
    assert 400 <= sp.start_ms <= 600
    assert 1400 <= sp.end_ms <= 1650

    # Ensure silence intervals are detected before and after speech
    assert len(silence) >= 2
    assert silence[0].start_ms == 0
    assert silence[-1].end_ms == sig.metadata.duration_ms


def test_vad_hangover_merging():
    sr = 16000
    samples = []

    # Tone 1: 300ms
    for i in range(int(sr * 0.3)):
        samples.append(0.6 * math.sin(2.0 * math.pi * 220.0 * (i / sr)))

    # Short gap of 80ms (below speech_hangover_ms of 200ms)
    samples.extend([0.0] * int(sr * 0.08))

    # Tone 2: 300ms
    for i in range(int(sr * 0.3)):
        samples.append(0.6 * math.sin(2.0 * math.pi * 220.0 * (i / sr)))

    sig = create_signal(samples, sr)
    vad = VoiceActivityDetector(
        config=VADConfig(
            energy_threshold_db=-30.0,
            speech_hangover_ms=200,
            min_speech_duration_ms=100,
        )
    )

    speech, _ = vad.process(sig)
    # The two segments should be merged into a single speech interval due to hangover
    assert len(speech) == 1
    assert speech[0].end_ms - speech[0].start_ms >= 600


def test_vad_classify_silence_gap():
    # Test initial gap > 800ms
    cat, removable, _ = VoiceActivityDetector._classify_silence_gap(
        duration_ms=1000, start_ms=0, end_ms=1000, total_duration_ms=5000
    )
    assert cat == SilenceCategory.REMOVABLE_GAP
    assert removable is True

    # Test word pause < 400ms
    cat, removable, _ = VoiceActivityDetector._classify_silence_gap(
        duration_ms=250, start_ms=1000, end_ms=1250, total_duration_ms=5000
    )
    assert cat == SilenceCategory.PAUSE_WORD
    assert removable is False

    # Test sentence pause 400ms <= d < 1200ms
    cat, removable, _ = VoiceActivityDetector._classify_silence_gap(
        duration_ms=600, start_ms=1000, end_ms=1600, total_duration_ms=5000
    )
    assert cat == SilenceCategory.PAUSE_SENTENCE
    assert removable is False

    # Test internal dead air >= 1200ms
    cat, removable, _ = VoiceActivityDetector._classify_silence_gap(
        duration_ms=1500, start_ms=1000, end_ms=2500, total_duration_ms=5000
    )
    assert cat == SilenceCategory.REMOVABLE_GAP
    assert removable is True
