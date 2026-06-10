"""COMPONENTS C + D — Orchestrator. Receives call-ended, builds the Arabic MoM, posts
the adaptive card, and schedules follow-ups (plan §6)."""
from __future__ import annotations

import json
import logging
from pathlib import Path

from fastapi import FastAPI, Request
from fastapi.responses import JSONResponse
from jsonschema import Draft202012Validator

from app.adaptive_cards import build_mom_card
from app.config import Settings, get_settings
from app.graph_client import FakeGraph, GraphClient, build_event_payload, build_graph
from app.llm import LLM, build_llm
from app.mom_pipeline import generate_mom
from app.scheduler import route_meetings
from app.stt_client import SttClient

logging.basicConfig(level=logging.INFO)
log = logging.getLogger("orch.main")

_SCHEMA_PATH = Path(__file__).resolve().parents[2] / "shared" / "schemas" / "call_event.schema.json"
_CALL_EVENT_VALIDATOR = Draft202012Validator(json.loads(_SCHEMA_PATH.read_text(encoding="utf-8")))


def create_app(settings: Settings, *, llm: LLM | None = None,
               graph: GraphClient | None = None,
               stt_client: SttClient | None = None) -> FastAPI:
    app = FastAPI(title="mahdar orchestrator")
    _llm = llm or build_llm(settings)
    _graph = graph or build_graph(settings)
    _stt = stt_client or SttClient(settings.stt_base_url, settings.transcript_poll_timeout_s)
    processed: set[str] = set()      # idempotency (in-memory MVP)

    @app.get("/health")
    def health():
        return {"status": "ok", "llm": settings.llm_provider,
                "graph": "real" if settings.graph_enabled else "fake",
                "timezone": settings.default_timezone}

    @app.post("/webhooks/call-ended")
    async def call_ended(request: Request):
        ev = await request.json()
        errors = [e.message for e in _CALL_EVENT_VALIDATOR.iter_errors(ev)]
        if errors:
            return JSONResponse({"error": "invalid call_event", "detail": errors[0]}, status_code=422)

        meeting_id = ev["meetingId"]
        if meeting_id in processed:
            return {"status": "ignored_duplicate", "meetingId": meeting_id}
        processed.add(meeting_id)

        result = await _process(ev)
        return result

    @app.post("/actions/confirm-meeting")
    async def confirm_meeting(request: Request):
        body = await request.json()
        data = body.get("data", body)
        resolved = data.get("resolved_iso")
        if not resolved:
            from app import dates
            resolved = dates.resolve(data.get("datetime_text", ""),
                                     meeting_end_iso=data.get("meeting_end_iso", ""),
                                     tz_name=settings.default_timezone)
        if not resolved:
            return JSONResponse({"error": "could not resolve a datetime"}, status_code=400)
        payload = build_event_payload(subject=data.get("title_ar", "اجتماع متابعة"),
                                      start_iso=resolved, attendee_emails=data.get("emails", []),
                                      timezone=settings.default_timezone)
        ev = _graph.create_event(data.get("organizer_id", ""), payload)
        return {"status": "created", "eventId": ev.event_id, "webLink": ev.web_link}

    async def _process(ev: dict) -> dict:
        meeting_id = ev["meetingId"]
        participants = ev.get("participants", [])
        names = [p.get("displayName", "") for p in participants]
        emails = {p.get("displayName", ""): p.get("email") for p in participants}

        transcript = await _stt.wait_for_transcript(meeting_id)
        segments = transcript.get("segments", [])
        if not segments:
            log.warning("no transcript segments for %s", meeting_id)

        mom_res = generate_mom(_llm, subject=transcript.get("metadata", {}).get("subject", ""),
                               participants=names, started_at=ev["startedAt"],
                               ended_at=ev["endedAt"], segments=segments)

        decisions = route_meetings(mom_res.mom.get("proposed_meetings", []),
                                   meeting_end_iso=ev["endedAt"],
                                   tz_name=settings.default_timezone,
                                   threshold=settings.auto_create_confidence_threshold)

        created = []
        for d in decisions:
            if d.action != "create":
                continue
            payload = build_event_payload(
                subject=d.title_ar, start_iso=d.resolved_iso,
                attendee_emails=[emails.get(a) for a in d.attendees if emails.get(a)],
                timezone=settings.default_timezone)
            ce = _graph.create_event(ev["organizerId"], payload)
            created.append({"title": d.title_ar, "eventId": ce.event_id, "start": d.resolved_iso})

        date_label = ev["endedAt"][:10]
        card = build_mom_card(mom_res.mom, subject=transcript.get("metadata", {}).get("subject", "اجتماع"),
                              date_label=date_label, decisions=decisions)
        msg_id = _graph.post_chat_message(ev["chatThreadId"], card)

        return {
            "status": "ok", "meetingId": meeting_id, "degraded": mom_res.degraded,
            "segments": len(segments), "messageId": msg_id,
            "createdEvents": created,
            "suggested": [d.title_ar for d in decisions if d.action == "suggest"],
        }

    return app


app = create_app(get_settings())
