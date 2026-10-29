"""Unit tests for WAV header inspection, synthetic audio generation, and PCM loading."""

import tempfile
from pathlib import Path
import pytest

from research.transcription.audio import (
    AudioMetadata,
    SyntheticSpeechSegment,
    generate_synthetic_wav,
    inspect_wav_header,
    load_wav_pcm,
)


def test_synthetic_wav_generation_and_pcm_loading():
    with tempfile.TemporaryDirectory() as tmpdir:
        wav_path = Path(tmpdir) / "test_synth.wav"
        speech_segments = [
            SyntheticSpeechSegment(start_ms=100, end_ms=800, frequency_hz=220.0, amplitude=0.5),
        ]
        generate_synthetic_wav(
            wav_path,
            total_duration_sec=1.5,
            sample_rate=16000,
            speech_segments=speech_segments,
            background_noise_level=0.01,
        )

        assert wav_path.exists()
        assert wav_path.stat().st_size > 44

        # Inspect header
        meta = inspect_wav_header(wav_path)
        assert meta.sample_rate == 16000
        assert meta.channels == 1
        assert meta.sample_width_bytes == 2
        assert meta.total_frames == int(1.5 * 16000)
        assert meta.duration_ms == 1500

        # Load PCM
        signal = load_wav_pcm(wav_path)
        assert len(signal.samples) == 24000
        assert min(signal.samples) >= -1.0
        assert max(signal.samples) <= 1.0

        # Check RMS and ZCR
        rms = signal.rms_energy(100, 800)
        assert rms > 0.1  # Significant energy during speech segment

        silence_rms = signal.rms_energy(1000, 1400)
        assert silence_rms < 0.05  # Low energy during background noise
