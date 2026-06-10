"""Confidence-based follow-up routing (plan §6.4).

The deterministic resolver (dates.resolve) is authoritative: a proposed meeting is
auto-created ONLY when we can pin it to a concrete day+time in code. Everything else
becomes a "suggested" meeting requiring one human click. This protects user trust —
we never create a calendar event from a vague phrase."""
from __future__ import annotations

from dataclasses import dataclass

from app import dates


@dataclass
class ScheduleDecision:
    title_ar: str
    attendees: list[str]
    datetime_text: str
    resolved_iso: str | None
    action: str          # "create" | "suggest"
    llm_confidence: str


def route_meetings(proposed: list[dict], *, meeting_end_iso: str, tz_name: str,
                   threshold: str = "high") -> list[ScheduleDecision]:
    decisions: list[ScheduleDecision] = []
    for m in proposed:
        text = m.get("datetime_text", "") or ""
        resolved = dates.resolve(text, meeting_end_iso=meeting_end_iso, tz_name=tz_name)
        llm_conf = (m.get("confidence") or "low").lower()

        # auto-create only when the resolver pinned a concrete datetime AND the LLM also
        # judged it high (both signals must agree when threshold == "high").
        can_create = resolved is not None and (threshold != "high" or llm_conf == "high")
        decisions.append(ScheduleDecision(
            title_ar=m.get("title_ar", "اجتماع متابعة"),
            attendees=list(m.get("attendees", []) or []),
            datetime_text=text,
            resolved_iso=resolved,
            action="create" if can_create else "suggest",
            llm_confidence=llm_conf,
        ))
    return decisions
