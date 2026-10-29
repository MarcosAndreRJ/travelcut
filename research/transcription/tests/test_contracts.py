"""Unit tests for transcription contracts, Pydantic validation, serialization, and Phase 4.1 export."""

import json
import pytest

from research.transcription.contracts import (
    AlignmentStatus,
    CutDecisionState,
    CutReason,
    CutRequest,
    ModelInfo,
    ProcessingMetadata,
    SchemaVersion,
    SilenceCategory,
    SilenceInterval,
    SpeechInterval,
    TranscriptSegment,
    TranscriptWord,
    TranscriptionDocument,
    TranscriptionStatus,
)


def test_schema_version_and_enums():
    assert SchemaVersion.V1_0_0.value == "1.0.0"
    assert AlignmentStatus.ALIGNED.value == "aligned"
    assert AlignmentStatus.INTERPOLATED.value == "interpolated"
    assert SilenceCategory.REMOVABLE_GAP.value == "removable_gap"
    assert CutReason.HESITATION.value == "hesitation"


def test_word_timestamp_validation():
    # Valid word
    w = TranscriptWord(
        id="w_001",
        segment_id="seg_001",
        media_id="med_001",
        text="Paraty",
        start_ms=100,
        end_ms=450,
        confidence=0.98,
    )
    assert w.start_ms == 100
    assert w.end_ms == 450
    assert w.confidence == 0.98
    assert w.alignment_status == AlignmentStatus.ALIGNED

    # Inverted timestamp should raise ValueError / ValidationError
    with pytest.raises(Exception, match="greater than end_ms"):
        TranscriptWord(
            id="w_002",
            segment_id="seg_001",
            media_id="med_001",
            text="erro",
            start_ms=500,
            end_ms=400,
        )

    # Negative timestamp should raise ValueError / ValidationError
    with pytest.raises(Exception, match="greater than or equal to 0"):
        TranscriptWord(
            id="w_003",
            segment_id="seg_001",
            media_id="med_001",
            text="negativo",
            start_ms=-10,
            end_ms=100,
        )


def test_transcription_document_roundtrip_and_phase4_export():
    w1 = TranscriptWord(
        id="w_0001",
        segment_id="seg_0001",
        media_id="vid_100",
        text="Olá",
        start_ms=200,
        end_ms=500,
        confidence=0.99,
    )
    w2 = TranscriptWord(
        id="w_0002",
        segment_id="seg_0001",
        media_id="vid_100",
        text="viajantes",
        start_ms=520,
        end_ms=1100,
        confidence=0.95,
    )
    seg1 = TranscriptSegment(
        id="seg_0001",
        media_id="vid_100",
        start_ms=200,
        end_ms=1100,
        text="Olá viajantes",
        words=[w1, w2],
    )
    sil1 = SilenceInterval(
        id="sil_0001",
        media_id="vid_100",
        start_ms=1100,
        end_ms=2500,
        duration_ms=1400,
        category=SilenceCategory.REMOVABLE_GAP,
        is_removable_candidate=True,
        recommendation_note="Pausa longa removível",
    )
    doc = TranscriptionDocument(
        schema_version=SchemaVersion.V1_0_0,
        media_id="vid_100",
        status=TranscriptionStatus.COMPLETED,
        language="pt",
        language_probability=0.99,
        duration_ms=3000,
        full_text="Olá viajantes",
        model_info=ModelInfo(
            engine_name="mock-engine",
            model_name="small",
            quantization="int8",
            device="cpu",
        ),
        segments=[seg1],
        words=[w1, w2],
        speech_intervals=[SpeechInterval(start_ms=200, end_ms=1100, speech_probability=0.99)],
        silence_intervals=[sil1],
        processing_metadata=ProcessingMetadata(
            processing_time_ms=600,
            audio_duration_ms=3000,
            real_time_factor=0.2,
            sample_rate_hz=16000,
            channels=1,
            timestamp_utc="2026-10-29T12:00:00Z",
        ),
    )

    # JSON round-trip serialization
    json_str = doc.model_dump_json()
    reloaded = TranscriptionDocument.model_validate_json(json_str)
    assert reloaded.media_id == "vid_100"
    assert len(reloaded.words) == 2
    assert reloaded.words[1].text == "viajantes"
    assert reloaded.segments[0].start_ms == 200

    # Phase 4.1 Export compatibility verification
    phase4_dict = doc.to_phase4_workspace_export()
    assert phase4_dict["mediaId"] == "vid_100"
    assert phase4_dict["fullText"] == "Olá viajantes"
    assert len(phase4_dict["segments"]) == 1
    assert len(phase4_dict["words"]) == 2
    assert phase4_dict["words"][0]["text"] == "Olá"
    assert phase4_dict["words"][0]["startSec"] == 0.2
    assert phase4_dict["words"][0]["endSec"] == 0.5
    assert len(phase4_dict["silenceIntervals"]) == 1
    assert phase4_dict["silenceIntervals"][0]["isRemovable"] is True
    assert len(phase4_dict["cutSuggestions"]) == 1
    assert phase4_dict["cutSuggestions"][0]["reason"] == "silence"
    assert phase4_dict["cutSuggestions"][0]["durationSec"] == 1.4
