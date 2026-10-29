"""Integration tests for all 15 research scenarios (Section 9).

Validates:
- Speech recognition and segment parsing
- Voice Activity Detection (VAD) accuracy
- Silence classification and removable gap flagging
- Word alignment and hesitation detection
- Performance benchmarks and metrics
"""

import tempfile
from pathlib import Path
import pytest

from research.transcription.contracts import (
    AlignmentStatus,
    CutReason,
    SilenceCategory,
    TranscriptionStatus,
)
from research.transcription.engine import MockTranscriptionEngine
from research.transcription.sample_generator import generate_all_15_scenarios


@pytest.fixture(scope="module")
def scenario_suite():
    with tempfile.TemporaryDirectory() as tmpdir:
        scenarios = generate_all_15_scenarios(tmpdir)
        yield scenarios


def test_scenario_01_fala_clara(scenario_suite):
    scen = scenario_suite["scen01"]
    engine = MockTranscriptionEngine()
    doc = engine.transcribe(scen.audio_path, preset_transcript=scen.expected_text)

    assert doc.status == TranscriptionStatus.COMPLETED
    assert len(doc.segments) >= 1
    assert len(doc.words) > 0
    assert doc.duration_ms == scen.duration_ms
    assert doc.processing_metadata.real_time_factor < 1.0


def test_scenario_02_fala_rapida(scenario_suite):
    scen = scenario_suite["scen02"]
    engine = MockTranscriptionEngine()
    doc = engine.transcribe(scen.audio_path, preset_transcript=scen.expected_text)

    assert doc.status == TranscriptionStatus.COMPLETED
    assert doc.duration_ms == 1700
    assert len(doc.words) > 0


def test_scenario_03_pausas_longas(scenario_suite):
    scen = scenario_suite["scen03"]
    engine = MockTranscriptionEngine()
    doc = engine.transcribe(scen.audio_path, preset_transcript=scen.expected_text)

    # Should detect long silence interval as removable candidate
    removable_silences = [s for s in doc.silence_intervals if s.is_removable_candidate]
    assert len(removable_silences) >= 1
    assert removable_silences[0].duration_ms >= 1000
    assert removable_silences[0].category in (
        SilenceCategory.REMOVABLE_GAP,
        SilenceCategory.PAUSE_SENTENCE,
    )


def test_scenario_04_repeticoes(scenario_suite):
    scen = scenario_suite["scen04"]
    engine = MockTranscriptionEngine()
    doc = engine.transcribe(scen.audio_path, preset_transcript=scen.expected_text)

    assert "eu fui" in doc.full_text.lower()
    assert len(doc.words) >= 4


def test_scenario_05_hesitacoes(scenario_suite):
    scen = scenario_suite["scen05"]
    engine = MockTranscriptionEngine()
    doc = engine.transcribe(scen.audio_path, preset_transcript=scen.expected_text)

    # Should flag hesitation words as cut candidates
    hesitations = [w for w in doc.words if w.is_cut_candidate and w.cut_reason == CutReason.HESITATION]
    assert len(hesitations) >= 1
    assert any("ééé" in w.text.lower() or "humm" in w.text.lower() or "tipo" in w.text.lower() for w in hesitations)


def test_scenario_06_duas_pessoas(scenario_suite):
    scen = scenario_suite["scen06"]
    engine = MockTranscriptionEngine()
    doc = engine.transcribe(scen.audio_path, preset_transcript=scen.expected_text)

    assert len(doc.segments) >= 1
    assert len(doc.words) >= 5


def test_scenario_07_ruido_ambiente(scenario_suite):
    scen = scenario_suite["scen07"]
    engine = MockTranscriptionEngine()
    doc = engine.transcribe(scen.audio_path, preset_transcript=scen.expected_text)

    assert doc.status == TranscriptionStatus.COMPLETED
    assert len(doc.speech_intervals) >= 1


def test_scenario_08_vento_forte(scenario_suite):
    scen = scenario_suite["scen08"]
    engine = MockTranscriptionEngine()
    doc = engine.transcribe(scen.audio_path, preset_transcript=scen.expected_text)

    assert doc.status == TranscriptionStatus.COMPLETED


def test_scenario_09_musica_fundo(scenario_suite):
    scen = scenario_suite["scen09"]
    engine = MockTranscriptionEngine()
    doc = engine.transcribe(scen.audio_path, preset_transcript=scen.expected_text)

    assert doc.status == TranscriptionStatus.COMPLETED
    assert len(doc.words) > 0


def test_scenario_10_fala_nas_bordas(scenario_suite):
    scen = scenario_suite["scen10"]
    engine = MockTranscriptionEngine()
    doc = engine.transcribe(scen.audio_path, preset_transcript=scen.expected_text)

    assert len(doc.words) > 0
    # First word starts near 0ms
    assert doc.words[0].start_ms <= 100


def test_scenario_11_ausencia_total_fala(scenario_suite):
    scen = scenario_suite["scen11"]
    engine = MockTranscriptionEngine()
    doc = engine.transcribe(scen.audio_path, preset_transcript=scen.expected_text)

    assert doc.status == TranscriptionStatus.COMPLETED
    assert len(doc.words) == 0
    assert len(doc.segments) == 0
    # The entire audio is a silence interval
    assert len(doc.silence_intervals) >= 1
    assert doc.silence_intervals[0].duration_ms >= 3000


def test_scenario_12_audio_muito_curto(scenario_suite):
    scen = scenario_suite["scen12"]
    engine = MockTranscriptionEngine()
    doc = engine.transcribe(scen.audio_path, preset_transcript=scen.expected_text)

    assert doc.duration_ms == 400
    assert doc.status == TranscriptionStatus.COMPLETED


def test_scenario_13_audio_longo(scenario_suite):
    scen = scenario_suite["scen13"]
    engine = MockTranscriptionEngine()
    doc = engine.transcribe(scen.audio_path, preset_transcript=scen.expected_text)

    assert doc.duration_ms == 33000
    assert len(doc.segments) >= 3
    assert len(doc.words) >= 10
    # Should have multiple silence intervals
    assert len(doc.silence_intervals) >= 2


def test_scenario_14_descontinuidade(scenario_suite):
    scen = scenario_suite["scen14"]
    engine = MockTranscriptionEngine()
    # Provide words with simulated temporal discontinuity
    custom_words = [
        {"id": "w_1", "text": "Segmento", "start_ms": 200, "end_ms": 600, "confidence": 0.9},
        {"id": "w_2", "text": "inicial", "start_ms": 620, "end_ms": 950, "confidence": 0.9},
        # Discontinuity jump of 1000ms
        {"id": "w_3", "text": "segundo", "start_ms": 1950, "end_ms": 2300, "confidence": 0.9},
        {"id": "w_4", "text": "alinhado", "start_ms": 2320, "end_ms": 2800, "confidence": 0.9},
    ]
    doc = engine.transcribe(scen.audio_path, custom_words=custom_words)

    from research.transcription.cut_preparer import validate_word_continuity
    is_cont, warnings = validate_word_continuity(doc.words, max_allowed_gap_ms=600)
    assert is_cont is False
    assert any("Discontinuous gap" in w for w in warnings)


def test_scenario_15_baixa_confianca(scenario_suite):
    scen = scenario_suite["scen15"]
    engine = MockTranscriptionEngine()
    # Provide words with low acoustic confidence
    custom_words = [
        {
            "id": "w_low1",
            "text": "sussurrado",
            "start_ms": 400,
            "end_ms": 900,
            "confidence": 0.35,
            "alignment_status": AlignmentStatus.LOW_CONFIDENCE,
        },
        {
            "id": "w_low2",
            "text": "inaudível",
            "start_ms": 920,
            "end_ms": 1500,
            "confidence": 0.28,
            "alignment_status": AlignmentStatus.LOW_CONFIDENCE,
        },
    ]
    doc = engine.transcribe(scen.audio_path, custom_words=custom_words)

    assert len(doc.words) == 2
    assert all(w.confidence < 0.4 for w in doc.words)
    assert all(w.alignment_status == AlignmentStatus.LOW_CONFIDENCE for w in doc.words)
