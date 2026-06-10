"""End-to-end STT WebSocket pipeline with a FakeTranscriber (offline).

Drives /ingest exactly as the media bot does: session_start (text) -> binary PCM frames
-> session_end, then reads the transcript over REST. No network, no Groq key needed.
"""
import json
import math
import struct

from fastapi.testclient import TestClient

from app.config import Settings
from app.main import create_app
from app.transcriber import FakeTranscriber


def _tone(ms, amp=8000, rate=16000, freq=220):
    n = rate * ms // 1000
    return b"".join(struct.pack("<h", int(amp * math.sin(2 * math.pi * freq * i / rate)))
                     for i in range(n))


def _silence(ms, rate=16000):
    return b"\x00\x00" * (rate * ms // 1000)


def test_full_ingest_to_transcript(tmp_path):
    settings = Settings(transcriber="fake", data_dir=str(tmp_path),
                        vad_silence_ms=600, vad_min_segment_ms=200)
    app = create_app(settings, transcriber=FakeTranscriber(fixed_text="نقل الـ API Gateway"))
    client = TestClient(app)

    mid = "mtg-stt-itest"
    with client.websocket_connect(f"/ingest/{mid}") as ws:
        ws.send_text(json.dumps({
            "type": "session_start", "meetingId": mid,
            "joinUrl": "https://teams.example/x", "chatThreadId": "19:abc@thread.v2",
            "organizerId": "org-1", "participants": [{"displayName": "A", "aadId": "1"}],
        }))
        # two utterances separated by a silence gap
        ws.send_bytes(_tone(800))
        ws.send_bytes(_silence(700))
        ws.send_bytes(_tone(800))
        ws.send_text(json.dumps({"type": "session_end", "meetingId": mid}))

    out = client.get(f"/transcript/{mid}").json()
    assert out["status"] == "complete"
    assert out["metadata"]["organizer_id"] == "org-1"
    assert len(out["segments"]) == 2
    assert out["segments"][0]["text"] == "نقل الـ API Gateway"
    assert out["segments"][0]["t_start"] < out["segments"][1]["t_start"]


def test_health_reports_backend(tmp_path):
    app = create_app(Settings(transcriber="fake", data_dir=str(tmp_path)),
                     transcriber=FakeTranscriber())
    client = TestClient(app)
    h = client.get("/health").json()
    assert h["status"] == "ok"
    assert h["transcriber"] == "fake"
