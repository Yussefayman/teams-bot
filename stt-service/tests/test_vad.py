import math
import struct

from app.vad import EnergySegmenter


def tone(ms: int, amp: int = 8000, rate: int = 16000, freq: int = 220) -> bytes:
    n = rate * ms // 1000
    return b"".join(
        struct.pack("<h", int(amp * math.sin(2 * math.pi * freq * i / rate)))
        for i in range(n)
    )


def silence(ms: int, rate: int = 16000) -> bytes:
    return b"\x00\x00" * (rate * ms // 1000)


def test_cuts_on_silence_gap():
    seg = EnergySegmenter(silence_ms=600, min_segment_ms=200)
    out = []
    out += seg.push(tone(1000))      # 1s speech
    out += seg.push(silence(800))    # >600ms silence -> cut
    assert len(out) == 1
    u = out[0]
    assert u.t_start < u.t_end
    assert len(u.pcm) > 0


def test_two_utterances_separated_by_silence():
    seg = EnergySegmenter(silence_ms=600, min_segment_ms=200)
    out = []
    out += seg.push(tone(800))
    out += seg.push(silence(700))
    out += seg.push(tone(800))
    out += seg.flush()
    assert len(out) == 2
    assert out[0].t_start < out[1].t_start


def test_pure_silence_yields_nothing():
    seg = EnergySegmenter()
    assert seg.push(silence(2000)) == []
    assert seg.flush() == []


def test_max_length_forces_a_cut():
    seg = EnergySegmenter(silence_ms=5000, max_segment_ms=1000, min_segment_ms=200)
    out = seg.push(tone(2500))  # continuous speech longer than max
    assert len(out) >= 1
