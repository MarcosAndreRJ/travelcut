"""Audio loading, validation, and signal feature extraction for speech research.

Operates purely via Python standard library (wave, struct, math) without requiring
external dependencies such as FFmpeg or native binary bindings. Supports PCM 16-bit
mono/stereo WAV files and generates deterministic synthetic waveforms for test suites.
"""

from __future__ import annotations

import math
import struct
import wave
from dataclasses import dataclass
from pathlib import Path
from typing import List, Optional, Tuple


@dataclass
class AudioMetadata:
    path: str
    duration_ms: int
    sample_rate: int
    channels: int
    sample_width_bytes: int
    total_frames: int
    file_size_bytes: int

    @property
    def is_standard_speech_rate(self) -> bool:
        return self.sample_rate in (16000, 22050, 24000, 44100, 48000)


@dataclass
class AudioSignal:
    metadata: AudioMetadata
    samples: List[float]  # Normalized [-1.0, 1.0] mono samples

    def rms_energy(self, start_ms: int = 0, end_ms: Optional[int] = None) -> float:
        """Calculate Root Mean Square (RMS) energy in a millisecond window."""
        sr = self.metadata.sample_rate
        start_idx = max(0, int(start_ms * sr / 1000))
        if end_ms is not None:
            end_idx = min(len(self.samples), int(end_ms * sr / 1000))
        else:
            end_idx = len(self.samples)
        if start_idx >= end_idx or not self.samples:
            return 0.0
        window = self.samples[start_idx:end_idx]
        return math.sqrt(sum(x * x for x in window) / len(window))


class AudioCorruptedError(Exception):
    """Raised when audio file cannot be parsed or has invalid header."""
    pass


class AudioUnsupportedFormatError(Exception):
    """Raised when audio encoding is not PCM 16-bit or 8-bit."""
    pass


def inspect_wav_header(file_path: str | Path) -> AudioMetadata:
    """Inspect WAV header and return AudioMetadata without reading entire sample buffer."""
    path = Path(file_path)
    if not path.exists():
        raise FileNotFoundError(f"Audio file does not exist: {path}")

    file_size = path.stat().st_size
    if file_size < 44:
        raise AudioCorruptedError(f"File too small ({file_size} bytes) to be a valid WAV file")

    try:
        with wave.open(str(path), "rb") as wf:
            channels = wf.getnchannels()
            sample_width = wf.getsampwidth()
            sample_rate = wf.getframerate()
            num_frames = wf.getnframes()

            if sample_width not in (1, 2, 3, 4):
                raise AudioUnsupportedFormatError(
                    f"Unsupported sample width: {sample_width * 8}-bit. Supported: 8, 16, 24, 32-bit PCM."
                )

            duration_ms = int(round((num_frames / float(sample_rate)) * 1000.0)) if sample_rate > 0 else 0

            return AudioMetadata(
                path=str(path.resolve()),
                duration_ms=duration_ms,
                sample_rate=sample_rate,
                channels=channels,
                sample_width_bytes=sample_width,
                total_frames=num_frames,
                file_size_bytes=file_size,
            )
    except wave.Error as e:
        raise AudioCorruptedError(f"Failed to read WAV file header: {e}") from e


def load_wav_pcm(file_path: str | Path, target_sample_rate: Optional[int] = None) -> AudioSignal:
    """Read WAV file and convert all channels down to mono normalized float array [-1.0, 1.0]."""
    meta = inspect_wav_header(file_path)

    with wave.open(str(file_path), "rb") as wf:
        raw_bytes = wf.readframes(meta.total_frames)

    samples: List[float] = []
    width = meta.sample_width_bytes
    channels = meta.channels

    if width == 2:  # 16-bit signed PCM
        total_samples = len(raw_bytes) // 2
        fmt = f"<{total_samples}h"
        raw_shorts = struct.unpack(fmt, raw_bytes)
        scale = 1.0 / 32768.0

        if channels == 1:
            samples = [val * scale for val in raw_shorts]
        else:
            # Average multi-channel into mono
            samples = []
            for i in range(0, len(raw_shorts), channels):
                frame_avg = sum(raw_shorts[i: i + channels]) / channels
                samples.append(frame_avg * scale)

    elif width == 1:  # 8-bit unsigned PCM
        raw_bytes_list = list(raw_bytes)
        scale = 1.0 / 128.0
        if channels == 1:
            samples = [(val - 128) * scale for val in raw_bytes_list]
        else:
            samples = []
            for i in range(0, len(raw_bytes_list), channels):
                frame_avg = (sum(raw_bytes_list[i: i + channels]) / channels) - 128
                samples.append(frame_avg * scale)

    elif width == 3:  # 24-bit PCM
        samples = []
        bytes_per_frame = 3 * channels
        scale = 1.0 / 8388608.0
        for i in range(0, len(raw_bytes), bytes_per_frame):
            chan_sum = 0
            for c in range(channels):
                offset = i + (c * 3)
                # Little-endian 24-bit sign extension
                b0, b1, b2 = raw_bytes[offset: offset + 3]
                val = b0 | (b1 << 8) | (b2 << 16)
                if val & 0x800000:
                    val -= 0x1000000
                chan_sum += val
            samples.append((chan_sum / channels) * scale)

    else:
        raise AudioUnsupportedFormatError(f"Unsupported sample width: {width * 8} bits")

    return AudioSignal(metadata=meta, samples=samples)


def compute_rms_energy(samples: List[float], frame_length: int, hop_length: int) -> List[float]:
    """Calculate Root Mean Square (RMS) energy envelope over frames."""
    if not samples or frame_length <= 0 or hop_length <= 0:
        return []

    energy: List[float] = []
    n = len(samples)

    for start in range(0, n, hop_length):
        frame = samples[start: min(start + frame_length, n)]
        if not frame:
            break
        sum_sq = sum(x * x for x in frame)
        rms = math.sqrt(sum_sq / len(frame))
        energy.append(rms)

    return energy


def compute_zero_crossing_rate(samples: List[float], frame_length: int, hop_length: int) -> List[float]:
    """Calculate Zero Crossing Rate (ZCR) for speech/fricative discrimination."""
    if not samples or frame_length <= 0 or hop_length <= 0:
        return []

    zcr_list: List[float] = []
    n = len(samples)

    for start in range(0, n, hop_length):
        frame = samples[start: min(start + frame_length, n)]
        if len(frame) < 2:
            break
        crossings = 0
        for i in range(1, len(frame)):
            if (frame[i] >= 0 and frame[i - 1] < 0) or (frame[i] < 0 and frame[i - 1] >= 0):
                crossings += 1
        zcr_list.append(crossings / (len(frame) - 1))

    return zcr_list


# ==========================================
# Deterministic Audio Synthesis for Testing
# ==========================================

@dataclass
class SyntheticSpeechSegment:
    start_ms: int
    end_ms: int
    frequency_hz: float = 220.0  # Simulated fundamental voice pitch
    amplitude: float = 0.5
    harmonic: bool = True
    noise_level: float = 0.01


def generate_synthetic_wav(
    output_path: str | Path,
    total_duration_sec: float,
    speech_segments: Optional[List[SyntheticSpeechSegment]] = None,
    sample_rate: int = 16000,
    background_noise_level: float = 0.005,
) -> Path:
    """Generate a valid 16-bit mono WAV file with deterministic synthetic speech/silence segments.

    Provides reproducible testing without external MP3/WAV download requirements.
    """
    path = Path(output_path)
    path.parent.mkdir(parents=True, exist_ok=True)

    if speech_segments is None:
        speech_segments = []

    total_frames = int(round(total_duration_sec * sample_rate))
    samples = [0.0] * total_frames

    # Add gentle ambient white noise / pink noise simulation
    for i in range(total_frames):
        # Pseudo-random deterministic noise using hash
        h = ((i * 1103515245 + 12345) & 0x7FFFFFFF) / 0x7FFFFFFF
        noise = (h * 2.0 - 1.0) * background_noise_level
        samples[i] = noise

    # Overlay synthetic speech bursts
    for seg in speech_segments:
        start_frame = int(round((seg.start_ms / 1000.0) * sample_rate))
        end_frame = int(round((seg.end_ms / 1000.0) * sample_rate))
        start_frame = max(0, min(start_frame, total_frames))
        end_frame = max(start_frame, min(end_frame, total_frames))

        f0 = seg.frequency_hz
        seg_len = end_frame - start_frame

        for idx, f in enumerate(range(start_frame, end_frame)):
            t = f / float(sample_rate)
            # Smooth attack and decay envelope to prevent click artifacts
            env = 1.0
            ramp_len = min(int(0.02 * sample_rate), seg_len // 4)
            if ramp_len > 0:
                if idx < ramp_len:
                    env = idx / ramp_len
                elif idx > seg_len - ramp_len:
                    env = (seg_len - idx) / ramp_len

            # Formant / harmonic mixture simulating vocal cord resonance
            val = math.sin(2.0 * math.pi * f0 * t)
            if seg.harmonic:
                val += 0.5 * math.sin(2.0 * math.pi * (f0 * 2.0) * t)
                val += 0.25 * math.sin(2.0 * math.pi * (f0 * 3.0) * t)
                val += 0.12 * math.sin(2.0 * math.pi * (f0 * 4.0) * t)
                val /= 1.87

            # Add segment localized jitter/noise
            h = (((f + idx) * 1103515245 + 54321) & 0x7FFFFFFF) / 0x7FFFFFFF
            fricative = (h * 2.0 - 1.0) * seg.noise_level

            sample_val = (val * seg.amplitude * env) + fricative
            samples[f] = max(-1.0, min(1.0, samples[f] + sample_val))

    # Convert to 16-bit PCM and write WAV
    with wave.open(str(path), "wb") as wf:
        wf.setnchannels(1)
        wf.setsampwidth(2)
        wf.setframerate(sample_rate)
        packed_frames = bytearray()
        for s in samples:
            int_val = int(round(max(-1.0, min(1.0, s)) * 32767.0))
            packed_frames.extend(struct.pack("<h", int_val))
        wf.writeframes(packed_frames)

    return path
