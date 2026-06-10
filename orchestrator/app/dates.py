"""Deterministic Arabic relative-date resolution (plan §6.2).

The LLM is unreliable at turning phrases like "الثلاثاء الجاي الساعة ٣" into a correct
absolute datetime (it miscounts weekdays and drops the timezone). So we resolve dates
here, in code, anchored to the meeting end time + tenant timezone. If a phrase is
ambiguous we return None and the scheduler keeps confidence medium/low (suggest only).
"""
from __future__ import annotations

import datetime as dt
import re
from zoneinfo import ZoneInfo

# Arabic-Indic digits -> ASCII
_AR_DIGITS = str.maketrans("٠١٢٣٤٥٦٧٨٩", "0123456789")

# weekday name -> Monday=0..Sunday=6
_WEEKDAYS = {
    "الاثنين": 0, "الإثنين": 0, "الاتنين": 0,
    "الثلاثاء": 1, "الثلاثا": 1, "الثلات": 1,
    "الأربعاء": 2, "الاربعاء": 2, "الأربعا": 2, "الاربع": 2,
    "الخميس": 3,
    "الجمعة": 4, "الجمعه": 4,
    "السبت": 5,
    "الأحد": 6, "الاحد": 6,
}

# part-of-day -> default hour (24h) when no explicit hour is given
_DAYPART_DEFAULT = {"العصر": 15, "الظهر": 12, "المغرب": 18, "المساء": 19, "الصباح": 9}
# part-of-day that disambiguates a bare "الساعة N"
_PM_HINTS = ("العصر", "المغرب", "المساء", "بعد الظهر", "مساء", "مساءً")
_AM_HINTS = ("الصباح", "صباحا", "صباحاً")


def normalize_digits(s: str) -> str:
    return s.translate(_AR_DIGITS)


def _find_weekday(text: str) -> int | None:
    for name, idx in _WEEKDAYS.items():
        if name in text:
            return idx
    return None


def _find_hour(text: str) -> tuple[int | None, bool]:
    """Return (hour_24 or None, explicit) — explicit=False means inferred from daypart."""
    t = normalize_digits(text)
    m = re.search(r"الساعة\s*(\d{1,2})", t) or re.search(r"\bالساعه\s*(\d{1,2})", t)
    if m:
        hour = int(m.group(1))
        if any(h in text for h in _PM_HINTS) and hour < 12:
            hour += 12
        elif any(h in text for h in _AM_HINTS):
            pass
        return hour % 24, True
    for part, hour in _DAYPART_DEFAULT.items():
        if part in text:
            return hour, False
    return None, False


def resolve(datetime_text: str, *, meeting_end_iso: str, tz_name: str = "Asia/Riyadh") -> str | None:
    """Resolve an Arabic phrase to an ISO 8601 datetime with the tenant offset, or None
    if it cannot be pinned to a concrete day+time."""
    if not datetime_text:
        return None
    tz = ZoneInfo(tz_name)
    anchor = dt.datetime.fromisoformat(meeting_end_iso.replace("Z", "+00:00")).astimezone(tz)

    text = datetime_text
    target_date = None

    if "بكرة" in text or "بكره" in text or "غدا" in text or "الغد" in text:
        target_date = (anchor + dt.timedelta(days=1)).date()
    elif "بعد بكرة" in text or "بعد غد" in text:
        target_date = (anchor + dt.timedelta(days=2)).date()
    elif "اليوم" in text or "النهاردة" in text:
        target_date = anchor.date()
    else:
        wd = _find_weekday(text)
        if wd is not None:
            days_ahead = (wd - anchor.weekday()) % 7
            # "الجاي"/"القادم" or same-day reference -> push to next week's instance
            if days_ahead == 0 or any(k in text for k in ("الجاي", "القادم", "القادمة", "الجاية")):
                days_ahead = days_ahead or 7
                if days_ahead == 0:
                    days_ahead = 7
            target_date = (anchor + dt.timedelta(days=days_ahead)).date()

    if target_date is None:
        return None

    hour, _explicit = _find_hour(text)
    if hour is None:
        return None  # a day without a time is not "high confidence" — suggest only

    resolved = dt.datetime.combine(target_date, dt.time(hour=hour), tzinfo=tz)
    return resolved.isoformat()
