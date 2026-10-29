# Phase 7: Audio Transcription, VAD & Word Alignment Research Report

This document specifies the research findings, algorithmic architecture, data contracts, and integration mechanisms for **Phase 7 (Transcription, VAD, and Word Alignment)** of **TravelCut Studio**.

---

## 1. Architectural Overview

The Phase 7 pipeline provides production-grade speech recognition, voice activity detection, and word-level alignment optimized for travel vlogs and outdoor multicam footage:

```
[Raw Audio / Video]
        │
        ▼
[Audio Loader & Normalizer] ──> 16kHz Mono PCM Floating-Point Vector
        │
        ├──> [Voice Activity Detector (VAD)]
        │         ├── Adaptive Noise Floor Estimation
        │         ├── Frame Energy & Spectral Flux Analysis
        │         ├── Hangover Smoothing & Micro-spike Rejection
        │         └── Silence Taxonomy Classification
        │
        ├──> [ASR Engine (faster-whisper / Mock)]
        │         ├── Whisper small/base int8/float16
        │         ├── Temperature Fallback & Prompt Conditioning
        │         └── Segment & Word Generation
        │
        ├──> [Word Timestamp Aligner]
        │         ├── Dynamic Time Warping (DTW) Refinement
        │         ├── Phonetic / Acoustic Duration Modeling
        │         └── Hesitation & Filler Word Flagging
        │
        └──> [Silence & Cut Preparer]
                  ├── Travel Scene Context Preservation (Drone/Scenery)
                  └── Phase 4 Editorial Cut Suggestions (JSON / EDL)
```

---

## 2. Core Modules

| Module | Location | Purpose |
| :--- | :--- | :--- |
| `contracts.py` | `research/transcription/contracts.py` | Strongly typed dataclasses for documents, segments, words, silences, and cut suggestions. |
| `audio.py` | `research/transcription/audio.py` | WAV header inspection, PCM reading, normalization, and synthetic test signal generator. |
| `vad.py` | `research/transcription/vad.py` | Dual-feature energy/spectral flux detector with adaptive hangover smoothing. |
| `silence_detector.py` | `research/transcription/silence_detector.py` | Editorial classification of acoustic voids and context-aware drone/waterfall preservation. |
| `alignment.py` | `research/transcription/alignment.py` | Sub-millisecond boundary refinement and hesitation pattern tagging. |
| `engine.py` | `research/transcription/engine.py` | Abstract engine interface, mock engine, and `faster-whisper` integration. |
| `sample_generator.py` | `research/transcription/sample_generator.py` | Deterministic generation of all 15 audio evaluation scenarios. |
| `benchmark.py` | `research/transcription/benchmark.py` | Automated benchmarking suite for RTF, latency, word throughput, and memory. |
| `cli.py` | `research/transcription/cli.py` | Standalone CLI supporting JSON, Phase 4 format, TXT, and SRT outputs. |

---

## 3. Silence Taxonomy for Travel Editing

Audio pauses in travel footage differ fundamentally from studio podcasts or audiobooks. Ambient gaps in scenic or drone footage carry emotional weight and must not be cut blindly:

1. **`PAUSE_WORD` (< 400ms)**: Natural cadence between spoken words. Preserved automatically to maintain vocal naturalness.
2. **`PAUSE_SENTENCE` (400ms - 1200ms)**: Breathing or rhythmic pause between clauses or sentences. Preserved by default.
3. **`REMOVABLE_GAP` (>= 1200ms)**: Dead air, camera handling delay, or hesitation void. Flagged as a primary candidate for deletion to increase vlog pacing.
4. **`DRONE` / `SCENERY`**: When footage is tagged with drone or environmental hints, long pauses are protected from automatic removal to allow scenic immersion.

---

## 4. The 15 Research Scenarios (Section 9)

All 15 scenarios are generated deterministically and verified with automated test suites:

1. **`scen01_fala_clara`**: Clear Portuguese vocal take with normal cadence.
2. **`scen02_pausa_longa`**: Speech interrupted by an extended 2.2-second pause (removable candidate).
3. **`scen03_drone_sem_fala`**: Pure drone motor rumble without speech (zero false positives, no cuts).
4. **`scen04_ruido_vento`**: High-wind environmental noise with vocal presence.
5. **`scen05_ruido_agua`**: Waterfall/ocean ambient background with narration.
6. **`scen06_hesitacoes`**: Speech filled with filler tokens (*"eh"*, *"hum"*, *"tipo assim"*).
7. **`scen07_alternancia_falantes`**: Two alternating travel vlog hosts.
8. **`scen08_fala_sussurrada`**: Low-amplitude whisper / quiet museum segment.
9. **`scen09_fala_rapida`**: High-tempo vocalization (> 4 words/sec).
10. **`scen10_musica_fundo`**: Low-amplitude acoustic guitar behind speech.
11. **`scen11_silencio_puro`**: 100% digital zero silence (no speech detected, 100% removable).
12. **`scen12_sotaque_regional`**: Pronunciation variations and colloquial speech patterns.
13. **`scen13_palavras_estrangeiras`**: Foreign location names and multilingual travel terms.
14. **`scen14_fala_sobreposta`**: Overlapping laughter and speech between hosts.
15. **`scen15_clipe_curto`**: Micro-clip (< 1 second) testing edge boundary conditions.

---

## 5. Benchmark Metrics & Performance Targets

Target performance thresholds on commodity CPU / GPU hardware:

- **Real-Time Factor (RTF)**: < 0.35 on modern multi-core CPUs (int8 quantized `small` or `base` models).
- **GPU Acceleration**: RTF < 0.08 on NVIDIA RTX (CUDA float16).
- **Word Alignment Accuracy**: < 50ms variance compared to manual phoneme boundaries.
- **VAD False Positive Rate on Scenic Audio**: < 2% when scenic context hint is active.

---

## 6. Verification and Test Execution

Run the complete Python test suite:
```bash
python -m pytest research/transcription/tests
```

Execute single-file transcription via CLI:
```bash
python -m research.transcription.cli --input audio.wav --format phase4 --video-category drone
```
