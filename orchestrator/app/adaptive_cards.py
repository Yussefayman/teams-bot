"""Adaptive Card builders for the Arabic MoM (plan §6.4). RTL via "rtl": true on the
card; falls back gracefully since every TextBlock is right-context Arabic."""
from __future__ import annotations

from app.scheduler import ScheduleDecision

_VERSION = "1.5"
_SCHEMA = "http://adaptivecards.io/schemas/adaptive-card.json"


def _text(text: str, *, weight: str | None = None, size: str | None = None,
          wrap: bool = True, spacing: str | None = None) -> dict:
    block = {"type": "TextBlock", "text": text, "wrap": wrap}
    if weight:
        block["weight"] = weight
    if size:
        block["size"] = size
    if spacing:
        block["spacing"] = spacing
    return block


def _heading(text: str) -> dict:
    return _text(text, weight="Bolder", size="Medium", spacing="Medium")


def build_mom_card(mom: dict, *, subject: str, date_label: str,
                   decisions: list[ScheduleDecision]) -> dict:
    body: list[dict] = [
        _text("📋 محضر الاجتماع", weight="Bolder", size="Large"),
        _text(f"{subject} — {date_label}", size="Small"),
        _heading("الملخص"),
        _text(mom.get("summary_ar", "")),
    ]

    if mom.get("decisions_ar"):
        body.append(_heading("القرارات"))
        for d in mom["decisions_ar"]:
            body.append(_text(f"• {d}"))

    if mom.get("action_items"):
        body.append(_heading("بنود العمل"))
        for a in mom["action_items"]:
            due = f" — {a['due_hint']}" if a.get("due_hint") else ""
            body.append(_text(f"• {a.get('description_ar','')} (المسؤول: {a.get('owner_name','غير محدد')}){due}"))

    created = [d for d in decisions if d.action == "create"]
    suggested = [d for d in decisions if d.action == "suggest"]

    actions: list[dict] = []
    if created:
        body.append(_heading("✅ اجتماعات تم جدولتها"))
        for d in created:
            body.append(_text(f"• {d.title_ar} — {d.resolved_iso}"))

    if suggested:
        body.append(_heading("🕒 اجتماعات مقترحة (تحتاج تأكيد)"))
        for d in suggested:
            body.append(_text(f"• {d.title_ar} — {d.datetime_text or 'موعد غير محدد'}"))
            actions.append({
                "type": "Action.Submit",
                "title": f"تأكيد: {d.title_ar}",
                "data": {"action": "confirm_meeting", "title_ar": d.title_ar,
                         "datetime_text": d.datetime_text, "attendees": d.attendees},
            })

    if mom.get("language_note"):
        body.append(_text(mom["language_note"], size="Small", spacing="Medium"))

    card = {
        "type": "AdaptiveCard",
        "$schema": _SCHEMA,
        "version": _VERSION,
        "rtl": True,
        "body": body,
    }
    if actions:
        card["actions"] = actions
    return card
