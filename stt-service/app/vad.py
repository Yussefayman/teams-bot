"""Streaming utterance segmentation.

Production target is Silero VAD (plan §5), but that pulls in torch. For a torch-free
path that runs anywhere (incl. CI/Mac), the default EnergySegmenter cuts an utterance on
>= vad_silence_ms of low-energy audio, with a hard max-length cut so transcription
requests stay bounded. Both implement the same Segmenter protocol, so swapping to Silero
later is a config change.

Audio is PCM 16 kHz / 16-bit / mono (AUDIO-FORMAT.md). Energy is int16 RMS per 20 ms frame.
"""
from __future__ import annotations

import array
import math
from dataclasses import dataclass, field
from typing import Protocol


FRAME_MS = 20


@dataclass
class Utterance:
    pcm: bytes
    t_start: float   # seconds since session start
    t_end: float


class Segmenter(Protocol):
    def push(self, pcm: bytes) -> list[Utterance]: ...
    def flush(self) -> list[Utterance]: ...


def _rms_int16(frame: bytes) -> float:
    if not frame:
        return 0.0
    samples = array.array("h")
    samples.frombytes(frame[: len(frame) - (len(frame) % 2)])
    if not samples:
        return 0.0
    return math.sqrt(sum(s * s for s in samples) / len(samples))


@dataclass
class EnergySegmenter:
    sample_rate: int = 16000
    silence_ms: int = 600
    max_segment_ms: int = 25000
    min_segment_ms: int = 400
    energy_threshold: int = 500

    _frame_bytes: int = field(init=False)
    _carry: bytearray = field(default_factory=bytearray, init=False)
    _voiced: bytearray = field(default_factory=bytearray, init=False)
    _seg_start_s: float | None = field(default=None, init=False)
    _trailing_silence_ms: int = field(default=0, init=False)
    _elapsed_ms: int = field(default=0, init=False)

    def __post_init__(self):
        self._frame_bytes = self.sample_rate // 1000 * FRAME_MS * 2  # 640

    def push(self, pcm: bytes) -> list[Utterance]:
        out: list[Utterance] = []
        self._carry.extend(pcm)
        while len(self._carry) >= self._frame_bytes:
            frame = bytes(self._carry[: self._frame_bytes])
            del self._carry[: self._frame_bytes]
            out.extend(self._consume_frame(frame))
        return out

    def _consume_frame(self, frame: bytes) -> list[Utterance]:
        out: list[Utterance] = []
        is_voice = _rms_int16(frame) >= self.energy_threshold

        if is_voice:
            if self._seg_start_s is None:
                self._seg_start_s = self._elapsed_ms / 1000.0
            self._voiced.extend(frame)
            self._trailing_silence_ms = 0
        elif self._seg_start_s is not None:
            # keep a little trailing silence inside the clip, then decide to cut
            self._voiced.extend(frame)
            self._trailing_silence_ms += FRAME_MS
            if self._trailing_silence_ms >= self.silence_ms:
                cut = self._close(self._elapsed_ms / 1000.0 + FRAME_MS / 1000.0)
                if cut:
                    out.append(cut)

        self._elapsed_ms += FRAME_MS

        seg_ms = len(self._voiced) // (self._frame_bytes // FRAME_MS) if self._voiced else 0
        if self._seg_start_s is not None and seg_ms >= self.max_segment_ms:
            cut = self._close(self._elapsed_ms / 1000.0)
            if cut:
                out.append(cut)
        return out

    def _close(self, t_end: float) -> Utterance | None:
        pcm = bytes(self._voiced)
        start = self._seg_start_s
        self._voiced = bytearray()
        self._seg_start_s = None
        self._trailing_silence_ms = 0
        if start is None:
            return None
        dur_ms = len(pcm) / (self.sample_rate * 2) * 1000
        if dur_ms < self.min_segment_ms:
            return None
        return Utterance(pcm=pcm, t_start=round(start, 3), t_end=round(t_end, 3))

    def flush(self) -> list[Utterance]:
        if self._seg_start_s is None:
            return []
        cut = self._close(self._elapsed_ms / 1000.0)
        return [cut] if cut else []
