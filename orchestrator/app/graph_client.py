"""Microsoft Graph actions behind one interface (plan §6.3).

- FakeGraph: records calls, returns synthetic ids — used on Mac/dev and in tests.
- MsalGraphClient: real app-only (client-credentials) client. The MSAL token + HTTP
  wiring is completed on deployment; methods raise until GRAPH_ENABLED with creds.

Wrappers mirror plan §6.3: post_chat_message, create_event, get_user.
"""
from __future__ import annotations

from dataclasses import dataclass, field
from typing import Protocol


@dataclass
class CreatedEvent:
    event_id: str
    web_link: str


class GraphClient(Protocol):
    def post_chat_message(self, chat_id: str, card: dict) -> str: ...
    def create_event(self, organizer_id: str, event: dict) -> CreatedEvent: ...
    def get_user(self, aad_id: str) -> dict: ...


@dataclass
class FakeGraph:
    out_dir: str = ""        # when set, persist posted cards + events to disk (demo/dev)
    posted_cards: list[tuple[str, dict]] = field(default_factory=list)
    created_events: list[tuple[str, dict]] = field(default_factory=list)

    def post_chat_message(self, chat_id: str, card: dict) -> str:
        self.posted_cards.append((chat_id, card))
        msg_id = f"msg-{len(self.posted_cards)}"
        self._dump(f"card_{msg_id}.json", {"chatId": chat_id, "card": card})
        return msg_id

    def create_event(self, organizer_id: str, event: dict) -> CreatedEvent:
        self.created_events.append((organizer_id, event))
        n = len(self.created_events)
        self._dump(f"event_{n}.json", {"organizerId": organizer_id, "event": event})
        return CreatedEvent(event_id=f"evt-{n}", web_link=f"https://teams.example/evt/{n}")

    def _dump(self, name: str, payload: dict) -> None:
        if not self.out_dir:
            return
        import json
        from pathlib import Path
        d = Path(self.out_dir)
        d.mkdir(parents=True, exist_ok=True)
        (d / name).write_text(json.dumps(payload, ensure_ascii=False, indent=2), encoding="utf-8")

    def get_user(self, aad_id: str) -> dict:
        return {"id": aad_id, "displayName": aad_id, "mail": None}


def build_event_payload(*, subject: str, start_iso: str, attendee_emails: list[str],
                        duration_minutes: int = 30, timezone: str = "Asia/Riyadh") -> dict:
    """Graph event body with an online Teams meeting (plan §6.4)."""
    import datetime as dt
    start = dt.datetime.fromisoformat(start_iso)
    end = (start + dt.timedelta(minutes=duration_minutes)).isoformat()
    return {
        "subject": subject,
        "start": {"dateTime": start_iso, "timeZone": timezone},
        "end": {"dateTime": end, "timeZone": timezone},
        "isOnlineMeeting": True,
        "onlineMeetingProvider": "teamsForBusiness",
        "attendees": [
            {"emailAddress": {"address": e}, "type": "required"}
            for e in attendee_emails if e
        ],
    }


class MsalGraphClient:
    """Real Graph client (plan §6.3). Completed on deployment with tenant creds."""

    def __init__(self, *args, **kwargs):
        raise NotImplementedError(
            "MsalGraphClient is wired in deployment; use GRAPH_ENABLED=false (FakeGraph) for dev.")

    def post_chat_message(self, chat_id: str, card: dict) -> str:  # pragma: no cover
        raise NotImplementedError

    def create_event(self, organizer_id: str, event: dict) -> CreatedEvent:  # pragma: no cover
        raise NotImplementedError

    def get_user(self, aad_id: str) -> dict:  # pragma: no cover
        raise NotImplementedError


def build_graph(settings) -> GraphClient:  # noqa: ANN001
    if settings.graph_enabled:
        return MsalGraphClient(settings)
    return FakeGraph(out_dir=getattr(settings, "card_out_dir", ""))
