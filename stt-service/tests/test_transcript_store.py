import json
from pathlib import Path

from app.transcript_store import JsonlTranscriptStore, Segment, SessionMeta

ROOT = Path(__file__).resolve().parents[2]
SCHEMA = ROOT / "shared" / "schemas" / "transcript_segment.schema.json"


def test_lifecycle_and_read(tmp_path):
    store = JsonlTranscriptStore(str(tmp_path))
    assert store.status("m1") == "unknown"

    store.start(SessionMeta(meeting_id="m1", organizer_id="org-1", participants=[]))
    assert store.status("m1") == "in_progress"

    store.append("m1", Segment(0.0, 1.2, "مرحبا", "ar", -0.2))
    store.append("m1", Segment(1.2, 3.0, "نبدأ الاجتماع", "ar", -0.3))
    store.complete("m1")

    out = store.read("m1")
    assert out["status"] == "complete"
    assert out["meeting_id"] == "m1"
    assert len(out["segments"]) == 2
    assert out["segments"][0]["text"] == "مرحبا"


def test_segments_validate_against_shared_schema(tmp_path):
    from jsonschema import Draft202012Validator
    schema = json.loads(SCHEMA.read_text(encoding="utf-8"))
    v = Draft202012Validator(schema)

    store = JsonlTranscriptStore(str(tmp_path))
    store.start(SessionMeta(meeting_id="m2"))
    store.append("m2", Segment(0.0, 2.5, "نقل الـ API Gateway", "ar", -0.25))

    for seg in store.read("m2")["segments"]:
        assert list(v.iter_errors(seg)) == []
