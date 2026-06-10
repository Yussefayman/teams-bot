#!/usr/bin/env python3
"""Oracle for the M1 audio dump.

Parses a RIFF/WAVE file and asserts it matches the media-bot canonical format
(see media-bot/AUDIO-FORMAT.md): PCM, 16000 Hz, 16-bit, mono. Exit 0 + CONFORMS
on success; non-zero with the first mismatch otherwise.

Usage:
    python3 tools/check_wav.py <file.wav>
"""
import struct
import sys

EXPECTED = {
    "audio_format": 1,      # PCM
    "num_channels": 1,      # mono (mixed audio socket)
    "sample_rate": 16000,
    "bits_per_sample": 16,
}


def fail(msg: str):
    print(f"MISMATCH: {msg}")
    raise SystemExit(2)


def check(path: str) -> int:
    with open(path, "rb") as f:
        head = f.read(44)
    if len(head) < 44:
        fail(f"file shorter than a 44-byte WAV header ({len(head)} bytes)")

    riff, chunk_size, wave = struct.unpack_from("<4sI4s", head, 0)
    if riff != b"RIFF":
        fail(f"ChunkID is {riff!r}, expected b'RIFF'")
    if wave != b"WAVE":
        fail(f"Format is {wave!r}, expected b'WAVE'")

    sub1id, sub1size, audio_format, num_channels, sample_rate, byte_rate, \
        block_align, bits_per_sample = struct.unpack_from("<4sIHHIIHH", head, 12)
    if sub1id != b"fmt ":
        fail(f"Subchunk1ID is {sub1id!r}, expected b'fmt '")
    if sub1size != 16:
        fail(f"Subchunk1Size is {sub1size}, expected 16 (PCM)")

    sub2id, data_size = struct.unpack_from("<4sI", head, 36)
    if sub2id != b"data":
        fail(f"Subchunk2ID is {sub2id!r}, expected b'data' (no extra chunks before data)")

    actual = {
        "audio_format": audio_format,
        "num_channels": num_channels,
        "sample_rate": sample_rate,
        "bits_per_sample": bits_per_sample,
    }
    for k, want in EXPECTED.items():
        if actual[k] != want:
            fail(f"{k} is {actual[k]}, expected {want}")

    exp_byte_rate = sample_rate * num_channels * bits_per_sample // 8
    exp_block_align = num_channels * bits_per_sample // 8
    if byte_rate != exp_byte_rate:
        fail(f"ByteRate is {byte_rate}, expected {exp_byte_rate}")
    if block_align != exp_block_align:
        fail(f"BlockAlign is {block_align}, expected {exp_block_align}")

    seconds = data_size / exp_byte_rate if exp_byte_rate else 0
    print(f"CONFORMS: PCM {sample_rate} Hz, {bits_per_sample}-bit, {num_channels}ch, "
          f"data={data_size} bytes (~{seconds:.2f}s)")
    if chunk_size != 36 + data_size:
        # not fatal — some writers leave RIFF size loose — but worth flagging
        print(f"  note: ChunkSize {chunk_size} != 36 + dataSize ({36 + data_size})")
    return 0


if __name__ == "__main__":
    if len(sys.argv) != 2:
        print(__doc__)
        raise SystemExit(64)
    raise SystemExit(check(sys.argv[1]))
