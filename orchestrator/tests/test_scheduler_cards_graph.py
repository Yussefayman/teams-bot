from app.scheduler import route_meetings
from app.adaptive_cards import build_mom_card
from app.graph_client import FakeGraph, build_event_payload

END = "2026-06-10T09:18:00Z"
TZ = "Asia/Riyadh"


def test_concrete_meeting_is_routed_to_create():
    proposed = [{
        "title_ar": "متابعة", "datetime_text": "الثلاثاء الجاي الساعة ٣ العصر",
        "confidence": "high", "attendees": ["يوسف"],
    }]
    d = route_meetings(proposed, meeting_end_iso=END, tz_name=TZ)[0]
    assert d.action == "create"
    assert d.resolved_iso.startswith("2026-06-16T15:00:00")


def test_vague_meeting_is_routed_to_suggest():
    proposed = [{"title_ar": "متابعة", "datetime_text": "الأسبوع الجاي",
                 "confidence": "medium", "attendees": []}]
    d = route_meetings(proposed, meeting_end_iso=END, tz_name=TZ)[0]
    assert d.action == "suggest"
    assert d.resolved_iso is None


def test_high_text_but_low_llm_confidence_is_not_auto_created():
    # resolver succeeds but the LLM flagged low -> with threshold "high" we suggest
    proposed = [{"title_ar": "x", "datetime_text": "بكرة الساعة ١٠ الصباح",
                 "confidence": "low", "attendees": []}]
    d = route_meetings(proposed, meeting_end_iso=END, tz_name=TZ, threshold="high")[0]
    assert d.action == "suggest"


def test_card_has_rtl_and_sections_and_confirm_action():
    mom = {
        "summary_ar": "ملخص", "decisions_ar": ["قرار"],
        "action_items": [{"description_ar": "مهمة", "owner_name": "سارة", "due_hint": None}],
        "proposed_meetings": [], "language_note": None,
    }
    decisions = route_meetings(
        [{"title_ar": "متابعة", "datetime_text": "الأسبوع الجاي", "confidence": "low", "attendees": []}],
        meeting_end_iso=END, tz_name=TZ)
    card = build_mom_card(mom, subject="إطلاق", date_label="2026-06-10", decisions=decisions)

    assert card["type"] == "AdaptiveCard"
    assert card["rtl"] is True
    assert card["version"] == "1.5"
    texts = [b.get("text", "") for b in card["body"]]
    assert any("محضر الاجتماع" in t for t in texts)
    assert any("اجتماعات مقترحة" in t for t in texts)
    assert card["actions"][0]["data"]["action"] == "confirm_meeting"


def test_fake_graph_records_event_and_card():
    g = FakeGraph()
    payload = build_event_payload(subject="متابعة", start_iso="2026-06-16T15:00:00+03:00",
                                  attendee_emails=["a@x.com"], timezone=TZ)
    assert payload["isOnlineMeeting"] is True
    ev = g.create_event("org-1", payload)
    assert ev.event_id == "evt-1"
    g.post_chat_message("19:abc@thread.v2", {"type": "AdaptiveCard"})
    assert len(g.posted_cards) == 1
