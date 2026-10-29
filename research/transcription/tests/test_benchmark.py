"""Unit tests for the transcription benchmarking suite."""

import json
from pathlib import Path
import pytest

from research.transcription.audio import generate_synthetic_wav
from research.transcription.benchmark import BenchmarkResult, TranscriptionBenchmarkSuite
from research.transcription.engine import MockTranscriptionEngine


def test_benchmark_run_single(tmp_path: Path):
    suite = TranscriptionBenchmarkSuite(engine=MockTranscriptionEngine())
    fake_audio = tmp_path / "audio.wav"
    generate_synthetic_wav(fake_audio, total_duration_sec=1.0)

    result = suite.run_benchmark(
        audio_path=fake_audio,
        scenario_id="scen_test",
        scenario_name="Test Scenario",
        media_id="media_bench_1",
    )

    assert isinstance(result, BenchmarkResult)
    assert result.scenario_id == "scen_test"
    assert result.scenario_name == "Test Scenario"
    assert result.audio_duration_ms > 0
    assert result.word_count > 0
    assert result.real_time_factor >= 0.0
    assert result.engine_name == "mock-engine"


def test_benchmark_run_suite_and_reports(tmp_path: Path):
    suite = TranscriptionBenchmarkSuite(engine=MockTranscriptionEngine())

    s1_path = tmp_path / "s1.wav"
    s2_path = tmp_path / "s2.wav"
    generate_synthetic_wav(s1_path, total_duration_sec=1.0)
    generate_synthetic_wav(s2_path, total_duration_sec=1.5)

    scenarios = {
        "s1": {"audio_path": s1_path, "name": "Scenario 1"},
        "s2": {"audio_path": s2_path, "name": "Scenario 2"},
    }

    json_report = tmp_path / "reports" / "report.json"
    md_report = tmp_path / "reports" / "report.md"

    results = suite.run_suite(
        scenarios=scenarios,
        output_report_json=json_report,
        output_report_md=md_report,
    )

    assert len(results) == 2
    assert json_report.exists()
    assert md_report.exists()

    # Verify JSON content
    data = json.loads(json_report.read_text(encoding="utf-8"))
    assert len(data) == 2
    assert data[0]["scenario_id"] == "s1"
    assert data[1]["scenario_id"] == "s2"

    # Verify Markdown table content
    md_text = md_report.read_text(encoding="utf-8")
    assert "# TravelCut Studio - Transcription & VAD Benchmark Report" in md_text
    assert "Scenario 1" in md_text
    assert "Scenario 2" in md_text
    assert "Aggregate Real-Time Factor (RTF)" in md_text

