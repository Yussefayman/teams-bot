import json

from app.llm import FakeLLM
from app.mom_pipeline import generate_mom, transcript_to_text

GOOD_MOM = {
    "summary_ar": "ناقش الفريق نقل الـ API Gateway إلى production.",
    "decisions_ar": ["نقل الـ API Gateway يوم الخميس"],
    "action_items": [{"description_ar": "تجهيز rollback", "owner_name": "Sara", "due_hint": None}],
    "proposed_meetings": [{
        "title_ar": "متابعة", "datetime_iso": "2026-06-16T15:00:00+03:00",
        "datetime_text": "الثلاثاء الجاي الساعة ٣", "confidence": "high", "attendees": ["Sara"],
    }],
    "language_note": None,
}

SEGMENTS = [
    {"t_start": 1.0, "t_end": 4.0, "text": "نبدأ الاجتماع"},
    {"t_start": 4.0, "t_end": 9.0, "text": "نقل الـ API Gateway إلى production"},
]


def _gen(llm):
    return generate_mom(llm, subject="إطلاق", participants=["Sara", "يوسف"],
                        started_at="2026-06-10T09:00:00Z", ended_at="2026-06-10T09:34:00Z",
                        segments=SEGMENTS)


def test_valid_output_is_not_degraded():
    res = _gen(FakeLLM(GOOD_MOM))
    assert res.degraded is False
    assert res.mom["proposed_meetings"][0]["confidence"] == "high"


def test_code_fenced_json_is_parsed():
    fenced = "```json\n" + json.dumps(GOOD_MOM, ensure_ascii=False) + "\n```"
    res = _gen(FakeLLM(fenced))
    assert res.degraded is False


def test_retry_recovers_from_first_bad_response():
    calls = {"n": 0}

    def responder(system, user):
        calls["n"] += 1
        if calls["n"] == 1:
            return "{ not valid json"
        return json.dumps(GOOD_MOM, ensure_ascii=False)

    res = _gen(FakeLLM(responder=responder))
    assert calls["n"] == 2
    assert res.degraded is False


def test_double_failure_yields_valid_degraded_mom():
    res = _gen(FakeLLM(responder=lambda s, u: "garbage not json"))
    assert res.degraded is True
    # the degraded fallback must itself satisfy the schema
    from app.mom_pipeline import _validate
    assert _validate(res.mom) == []


def test_transcript_to_text_includes_timestamps():
    txt = transcript_to_text(SEGMENTS)
    assert "[1s]" in txt and "API Gateway" in txt
