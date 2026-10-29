"""Unit tests for the transcription CLI."""

import json
import tempfile
from pathlib import Path
import pytest

from research.transcription.audio import generate_synthetic_wav
from research.transcription.cli import run_cli


@pytest.fixture
def temp_wav():
    with tempfile.TemporaryDirectory() as tmpdir:
        wav_path = Path(tmpdir) / "cli_test.wav"
        generate_synthetic_wav(wav_path, total_duration_sec=1.0)
        yield wav_path


def test_cli_missing_input():
    # Non-existent input should return code 1
    code = run_cli(["--input", "non_existent_file.wav"])
    assert code == 1


def test_cli_json_output_to_file(temp_wav):
    with tempfile.TemporaryDirectory() as tmpdir:
        out_path = Path(tmpdir) / "output.json"
        code = run_cli([
            "--input", str(temp_wav),
            "--output", str(out_path),
            "--format", "json",
            "--engine", "mock",
        ])
        assert code == 0
        assert out_path.exists()

        data = json.loads(out_path.read_text(encoding="utf-8"))
        assert "schema_version" in data
        assert "words" in data
        assert "segments" in data


def test_cli_phase4_export_to_file(temp_wav):
    with tempfile.TemporaryDirectory() as tmpdir:
        out_path = Path(tmpdir) / "phase4.json"
        code = run_cli([
            "--input", str(temp_wav),
            "--output", str(out_path),
            "--format", "phase4",
            "--engine", "mock",
            "--bench",
        ])
        assert code == 0
        assert out_path.exists()

        data = json.loads(out_path.read_text(encoding="utf-8"))
        assert "mediaId" in data
        assert "silenceIntervals" in data
        assert "cutSuggestions" in data


def test_cli_srt_output_to_file(temp_wav):
    with tempfile.TemporaryDirectory() as tmpdir:
        out_path = Path(tmpdir) / "subtitles.srt"
        code = run_cli([
            "--input", str(temp_wav),
            "--output", str(out_path),
            "--format", "srt",
            "--engine", "mock",
        ])
        assert code == 0
        assert out_path.exists()
        content = out_path.read_text(encoding="utf-8")
        assert "-->" in content
