"""MoM generation pipeline (plan §6.2): assemble transcript -> LLM JSON -> validate
against mom.schema.json -> one retry on failure -> degraded fallback."""
from __future__ import annotations

import json
import logging
from dataclasses import dataclass
from pathlib import Path

from jsonschema import Draft202012Validator

from app.llm import LLM
from app.prompts import MOM_SYSTEM_PROMPT, build_user_prompt

log = logging.getLogger("orch.mom")

_SCHEMA_PATH = Path(__file__).resolve().parents[2] / "shared" / "schemas" / "mom.schema.json"
_VALIDATOR = Draft202012Validator(json.loads(_SCHEMA_PATH.read_text(encoding="utf-8")))


@dataclass
class MomResult:
    mom: dict
    degraded: bool        # True if validation failed twice and we fell back


def _strip_code_fence(s: str) -> str:
    s = s.strip()
    if s.startswith("```"):
        s = s.split("\n", 1)[1] if "\n" in s else s
        if s.endswith("```"):
            s = s[: -3]
        if s.startswith("json"):
            s = s[4:]
    return s.strip()


def _validate(mom: dict) -> list[str]:
    return [e.message for e in sorted(_VALIDATOR.iter_errors(mom), key=lambda e: list(e.path))]


def transcript_to_text(segments: list[dict]) -> str:
    lines = []
    for s in segments:
        ts = f"[{s.get('t_start', 0):.0f}s]"
        lines.append(f"{ts} {s.get('text', '')}".strip())
    return "\n".join(lines)


def generate_mom(llm: LLM, *, subject: str, participants: list[str],
                 started_at: str, ended_at: str, segments: list[dict]) -> MomResult:
    user = build_user_prompt(
        subject=subject, participants=participants,
        started_at=started_at, ended_at=ended_at,
        transcript_text=transcript_to_text(segments))

    raw = llm.complete_json(MOM_SYSTEM_PROMPT, user)
    mom, errors = _parse_and_validate(raw)
    if mom is not None and not errors:
        return MomResult(mom=mom, degraded=False)

    # one retry with the validation error appended
    detail = errors[0] if errors else "invalid JSON"
    log.warning("MoM validation failed, retrying once: %s", detail)
    retry_user = user + f"\n\nالمخرجات السابقة كانت غير صالحة بسبب: {detail}\nأعد JSON صالحًا فقط."
    raw2 = llm.complete_json(MOM_SYSTEM_PROMPT, retry_user)
    mom2, errors2 = _parse_and_validate(raw2)
    if mom2 is not None and not errors2:
        return MomResult(mom=mom2, degraded=False)

    log.error("MoM validation failed twice; using degraded fallback. raw=%s", raw2[:500])
    return MomResult(mom=_degraded_mom(raw2 or raw), degraded=True)


def _parse_and_validate(raw: str) -> tuple[dict | None, list[str]]:
    try:
        mom = json.loads(_strip_code_fence(raw))
    except (json.JSONDecodeError, TypeError):
        return None, ["response was not valid JSON"]
    if not isinstance(mom, dict):
        return None, ["response JSON was not an object"]
    return mom, _validate(mom)


def _degraded_mom(raw: str) -> dict:
    return {
        "summary_ar": "تعذّر إنشاء محضر منظّم؛ هذا ملخص مبدئي. يرجى المراجعة.",
        "decisions_ar": [],
        "action_items": [],
        "proposed_meetings": [],
        "language_note": "degraded: raw model output preserved in logs",
    }
