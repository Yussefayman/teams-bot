"""Transcriber backends behind one interface (plan §5).

- GroqTranscriber: Whisper large-v3 via Groq's hosted OpenAI-compatible audio API
  (validated working). Sends each utterance as an in-memory WAV.
- FakeTranscriber: deterministic, offline — used by unit/integration tests and CI.
- FasterWhisperTranscriber: local-GPU production target; stub until M2 on the GPU box.

A Transcript is what the store persists (matches transcript_segment.schema.json).
"""
from __future__ import annotations

import io
import wave
from dataclasses import dataclass
from typing import Protocol

import httpx


@dataclass
class TranscriptResult:
    text: str
    lang_detected: str
    avg_logprob: float


class Transcriber(Protocol):
    def transcribe(self, pcm: bytes) -> TranscriptResult: ...


def pcm_to_wav_bytes(pcm: bytes, sample_rate: int = 16000) -> bytes:
    buf = io.BytesIO()
    with wave.open(buf, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(sample_rate)
        w.writeframes(pcm)
    return buf.getvalue()


class FakeTranscriber:
    """Returns a deterministic string derived from the audio so tests are stable."""

    def __init__(self, fixed_text: str | None = None):
        self._fixed = fixed_text

    def transcribe(self, pcm: bytes) -> TranscriptResult:
        text = self._fixed if self._fixed is not None else f"[utterance {len(pcm)} bytes]"
        return TranscriptResult(text=text, lang_detected="ar", avg_logprob=-0.2)


class GroqTranscriber:
    def __init__(self, *, api_key: str, base_url: str, model: str,
                 language: str | None, initial_prompt: str | None,
                 sample_rate: int = 16000, timeout: float = 60.0):
        if not api_key:
            raise ValueError("GROQ_API_KEY is required for the groq transcriber")
        self._key = api_key
        self._url = f"{base_url.rstrip('/')}/audio/transcriptions"
        self._model = model
        self._language = language
        self._initial_prompt = initial_prompt
        self._sample_rate = sample_rate
        self._client = httpx.Client(timeout=timeout)

    def transcribe(self, pcm: bytes) -> TranscriptResult:
        wav = pcm_to_wav_bytes(pcm, self._sample_rate)
        data = {"model": self._model, "response_format": "verbose_json", "temperature": "0"}
        if self._language:
            data["language"] = self._language
        if self._initial_prompt:
            data["prompt"] = self._initial_prompt
        resp = self._client.post(
            self._url,
            headers={"Authorization": f"Bearer {self._key}"},
            files={"file": ("audio.wav", wav, "audio/wav")},
            data=data,
        )
        resp.raise_for_status()
        j = resp.json()
        segs = j.get("segments") or []
        avg_lp = (sum(s.get("avg_logprob", 0.0) for s in segs) / len(segs)) if segs else 0.0
        return TranscriptResult(
            text=(j.get("text") or "").strip(),
            lang_detected=_lang_code(j.get("language")),
            avg_logprob=round(avg_lp, 4),
        )

    def close(self):
        self._client.close()


class FasterWhisperTranscriber:
    """Local-GPU production backend (plan §5). Implemented on the GPU box in M2."""

    def __init__(self, *args, **kwargs):
        raise NotImplementedError(
            "FasterWhisperTranscriber runs on the GPU host; use TRANSCRIBER=groq for dev.")

    def transcribe(self, pcm: bytes) -> TranscriptResult:  # pragma: no cover
        raise NotImplementedError


def _lang_code(language: str | None) -> str:
    if not language:
        return "unknown"
    table = {"arabic": "ar", "english": "en"}
    return table.get(language.lower(), language.lower())


def build_transcriber(settings) -> Transcriber:  # noqa: ANN001
    kind = settings.transcriber.lower()
    if kind == "fake":
        return FakeTranscriber()
    if kind == "groq":
        return GroqTranscriber(
            api_key=settings.groq_api_key,
            base_url=settings.groq_base_url,
            model=settings.groq_model,
            language=settings.language,
            initial_prompt=settings.initial_prompt(),
            sample_rate=settings.sample_rate,
        )
    if kind == "faster-whisper":
        return FasterWhisperTranscriber()
    raise ValueError(f"unknown transcriber backend: {settings.transcriber}")
