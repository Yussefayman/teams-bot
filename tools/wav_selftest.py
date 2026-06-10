#!/usr/bin/env python3
"""Self-test for the WAV oracle (tools/check_wav.py).

Builds a conforming 16k/16-bit/mono WAV and a deliberately wrong one, then asserts
check_wav.py accepts the first and rejects the second. Proves the oracle works before
it is ever pointed at the real C# dump. Runnable anywhere.
"""
import math
import struct
import subprocess
import sys
import tempfile
import wave
from pathlib import Path

HERE = Path(__file__).resolve().parent
CHECK = HERE / "check_wav.py"


def write_tone(path: Path, *, rate: int, channels: int, sampwidth: int,
               seconds: float = 1.0, freq: float = 440.0):
    n = int(rate * seconds)
    with wave.open(str(path), "wb") as w:
        w.setnchannels(channels)
        w.setsampwidth(sampwidth)
        w.setframerate(rate)
        frames = bytearray()
        for i in range(n):
            val = int(0.3 * 32767 * math.sin(2 * math.pi * freq * i / rate))
            sample = struct.pack("<h", val)
            frames += sample * channels  # duplicate across channels
        w.writeframes(bytes(frames))


def run_check(path: Path):
    return subprocess.run([sys.executable, str(CHECK), str(path)],
                          capture_output=True, text=True)


def main() -> int:
    tmp = Path(tempfile.mkdtemp(prefix="mahdar_wav_"))
    good = tmp / "good_16k_mono.wav"
    bad = tmp / "bad_44k_stereo.wav"

    write_tone(good, rate=16000, channels=1, sampwidth=2)
    write_tone(bad, rate=44100, channels=2, sampwidth=2)

    failures = []

    r = run_check(good)
    print(f"[good] exit={r.returncode} {r.stdout.strip()}")
    if r.returncode != 0 or "CONFORMS" not in r.stdout:
        failures.append("conforming 16k/mono WAV was not accepted")

    r = run_check(bad)
    print(f"[bad ] exit={r.returncode} {r.stdout.strip()}")
    if r.returncode == 0:
        failures.append("non-conforming 44.1k/stereo WAV was accepted")

    # leave a playable conforming sample for manual ear-check
    sample = HERE.parent / "shared" / "examples" / "sample_16k_mono.wav"
    write_tone(sample, rate=16000, channels=1, sampwidth=2, seconds=0.5)
    print(f"\nplayable sample written: {sample} (afplay to hear a 440Hz tone)")

    print(f"\n{2} oracle cases run, {len(failures)} failure(s).")
    for f in failures:
        print("  FAIL", f)
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
