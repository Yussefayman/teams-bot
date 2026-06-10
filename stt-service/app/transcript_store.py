"""Per-meeting transcript persistence. JSONL on disk for MVP, behind an interface so it
can be swapped for Postgres later (plan §5.1). Each line is a transcript_segment that
validates against shared/schemas/transcript_segment.schema.json."""
from __future__ import annotations

import json
from dataclasses import dataclass, asdict
from pathlib import Path
from typing import Protocol


@dataclass
class Segment:
    t_start: float
    t_end: float
    text: str
    lang_detected: str
    avg_logprob: float


@dataclass
class SessionMeta:
    meeting_id: str
    join_url: str = ""
    chat_thread_id: str = ""
    organizer_id: str = ""
    participants: list | None = None


class TranscriptStore(Protocol):
    def start(self, meta: SessionMeta) -> None: ...
    def append(self, meeting_id: str, segment: Segment) -> None: ...
    def complete(self, meeting_id: str) -> None: ...
    def status(self, meeting_id: str) -> str: ...           # "unknown"|"in_progress"|"complete"
    def read(self, meeting_id: str) -> dict: ...            # {meeting_id,status,metadata,segments}


class JsonlTranscriptStore:
    """One <meeting_id>.jsonl of segments + a <meeting_id>.meta.json sidecar."""

    def __init__(self, data_dir: str):
        self._dir = Path(data_dir)
        self._dir.mkdir(parents=True, exist_ok=True)

    def _segments_path(self, meeting_id: str) -> Path:
        return self._dir / f"{meeting_id}.jsonl"

    def _meta_path(self, meeting_id: str) -> Path:
        return self._dir / f"{meeting_id}.meta.json"

    def start(self, meta: SessionMeta) -> None:
        self._segments_path(meta.meeting_id).write_text("", encoding="utf-8")
        payload = asdict(meta)
        payload["status"] = "in_progress"
        self._meta_path(meta.meeting_id).write_text(
            json.dumps(payload, ensure_ascii=False), encoding="utf-8")

    def append(self, meeting_id: str, segment: Segment) -> None:
        line = json.dumps(asdict(segment), ensure_ascii=False)
        with self._segments_path(meeting_id).open("a", encoding="utf-8") as f:
            f.write(line + "\n")

    def complete(self, meeting_id: str) -> None:
        meta = self._read_meta(meeting_id)
        meta["status"] = "complete"
        self._meta_path(meeting_id).write_text(
            json.dumps(meta, ensure_ascii=False), encoding="utf-8")

    def status(self, meeting_id: str) -> str:
        if not self._meta_path(meeting_id).exists():
            return "unknown"
        return self._read_meta(meeting_id).get("status", "in_progress")

    def read(self, meeting_id: str) -> dict:
        meta = self._read_meta(meeting_id) if self._meta_path(meeting_id).exists() else {}
        segments = []
        sp = self._segments_path(meeting_id)
        if sp.exists():
            for line in sp.read_text(encoding="utf-8").splitlines():
                if line.strip():
                    segments.append(json.loads(line))
        return {
            "meeting_id": meeting_id,
            "status": meta.get("status", "unknown"),
            "metadata": meta,
            "segments": segments,
        }

    def _read_meta(self, meeting_id: str) -> dict:
        return json.loads(self._meta_path(meeting_id).read_text(encoding="utf-8"))
