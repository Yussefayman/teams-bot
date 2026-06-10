import json

from fastapi.testclient import TestClient

from app.config import Settings
from app.llm import FakeLLM
from app.graph_client import FakeGraph
from app.main import create_app

MOM = {
    "summary_ar": "نقل الـ API Gateway إلى production.",
    "decisions_ar": ["النقل يوم الخميس"],
    "action_items": [{"description_ar": "تجهيز rollback", "owner_name": "سارة", "due_hint": None}],
    "proposed_meetings": [
        {"title_ar": "متابعة", "datetime_text": "الثلاثاء الجاي الساعة ٣ العصر",
         "datetime_iso": None, "confidence": "high", "attendees": ["يوسف"]},
        {"title_ar": "مراجعة", "datetime_text": "الأسبوع الجاي",
         "datetime_iso": None, "confidence": "low", "attendees": []},
    ],
    "language_note": None,
}

EVENT = {
    "meetingId": "mtg-orch-itest",
    "chatThreadId": "19:abc@thread.v2",
    "organizerId": "org-1",
    "participants": [
        {"displayName": "يوسف", "aadId": "1", "email": "y@x.com"},
        {"displayName": "سارة", "aadId": "2", "email": "s@x.com"},
    ],
    "startedAt": "2026-06-10T09:00:00Z",
    "endedAt": "2026-06-10T09:18:00Z",
}


class FakeStt:
    def __init__(self, segments):
        self._segments = segments

    async def wait_for_transcript(self, meeting_id, poll_interval_s=1.0):
        return {"meeting_id": meeting_id, "status": "complete",
                "metadata": {"subject": "إطلاق الـ API Gateway"}, "segments": self._segments}


def _client(graph):
    settings = Settings(llm_provider="fake", graph_enabled=False, default_timezone="Asia/Riyadh")
    app = create_app(settings, llm=FakeLLM(MOM), graph=graph,
                     stt_client=FakeStt([{"t_start": 1.0, "t_end": 5.0, "text": "نص"}]))
    return TestClient(app)


def test_call_ended_creates_event_posts_card_and_suggests():
    graph = FakeGraph()
    r = _client(graph).post("/webhooks/call-ended", json=EVENT)
    assert r.status_code == 200
    body = r.json()
    assert body["status"] == "ok"
    assert body["degraded"] is False
    # high+resolvable -> created; vague -> suggested
    assert len(body["createdEvents"]) == 1
    assert body["createdEvents"][0]["start"].startswith("2026-06-16T15:00:00")
    assert body["suggested"] == ["مراجعة"]
    # a card was posted to the chat thread, an event created on the organizer
    assert len(graph.posted_cards) == 1
    assert graph.posted_cards[0][0] == "19:abc@thread.v2"
    assert graph.posted_cards[0][1]["rtl"] is True
    assert len(graph.created_events) == 1


def test_invalid_payload_is_422():
    bad = dict(EVENT)
    del bad["organizerId"]
    r = _client(FakeGraph()).post("/webhooks/call-ended", json=bad)
    assert r.status_code == 422


def test_duplicate_is_ignored():
    client = _client(FakeGraph())
    client.post("/webhooks/call-ended", json=EVENT)
    r2 = client.post("/webhooks/call-ended", json=EVENT)
    assert r2.json()["status"] == "ignored_duplicate"
