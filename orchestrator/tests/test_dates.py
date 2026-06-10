from app.dates import resolve, normalize_digits

# Meeting ended Wed 2026-06-10 12:18 UTC (=> 15:18 Asia/Riyadh, +03:00).
END = "2026-06-10T09:18:00Z"
TZ = "Asia/Riyadh"


def test_arabic_digits_normalize():
    assert normalize_digits("الساعة ٣") == "الساعة 3"


def test_next_tuesday_afternoon_resolves_correctly():
    # "الثلاثاء الجاي الساعة ٣ العصر" -> next Tuesday is 2026-06-16, 3pm Riyadh
    iso = resolve("الثلاثاء الجاي الساعة ٣ العصر", meeting_end_iso=END, tz_name=TZ)
    assert iso is not None
    assert iso.startswith("2026-06-16T15:00:00")
    assert iso.endswith("+03:00")


def test_bukra_tomorrow():
    iso = resolve("بكرة الساعة ١٠ الصباح", meeting_end_iso=END, tz_name=TZ)
    assert iso.startswith("2026-06-11T10:00:00")


def test_daypart_without_explicit_hour():
    iso = resolve("الخميس العصر", meeting_end_iso=END, tz_name=TZ)
    # Thursday after Wed 6-10 is 6-11, العصر -> 15:00
    assert iso.startswith("2026-06-11T15:00:00")


def test_ambiguous_returns_none():
    assert resolve("الأسبوع الجاي", meeting_end_iso=END, tz_name=TZ) is None
    assert resolve("", meeting_end_iso=END, tz_name=TZ) is None


def test_day_without_time_returns_none():
    # a concrete day but no time -> not auto-schedulable
    assert resolve("الثلاثاء الجاي", meeting_end_iso=END, tz_name=TZ) is None
