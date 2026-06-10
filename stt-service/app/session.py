"""Per-meeting transcription session: audio in -> VAD -> transcribe (off the event
loop) -> transcript store. One Session per meeting_id keeps state isolated so concurrent
meetings need only more Sessions, not a rewrite (plan §5.4)."""
from __future__ import annotations

import asyncio
import logging

from app.transcript_store import Segment, SessionMeta, TranscriptStore
from app.transcriber import Transcriber
from app.vad import Segmenter, Utterance

log = logging.getLogger("stt.session")

_STOP = object()


class Session:
    def __init__(self, meeting_id: str, store: TranscriptStore,
                 transcriber: Transcriber, segmenter: Segmenter):
        self.meeting_id = meeting_id
        self._store = store
        self._transcriber = transcriber
        self._segmenter = segmenter
        self._queue: asyncio.Queue = asyncio.Queue()
        self._worker: asyncio.Task | None = None

    async def start(self, meta: SessionMeta) -> None:
        self._store.start(meta)
        self._worker = asyncio.create_task(self._run_worker())
        log.info("session %s started", self.meeting_id)

    async def feed(self, pcm: bytes) -> None:
        for utt in self._segmenter.push(pcm):
            await self._queue.put(utt)

    async def end(self) -> None:
        for utt in self._segmenter.flush():
            await self._queue.put(utt)
        await self._queue.put(_STOP)
        if self._worker:
            await self._worker
        self._store.complete(self.meeting_id)
        log.info("session %s complete", self.meeting_id)

    async def _run_worker(self) -> None:
        while True:
            item = await self._queue.get()
            if item is _STOP:
                return
            utt: Utterance = item
            try:
                # transcription is blocking network/compute -> keep it off the event loop
                result = await asyncio.to_thread(self._transcriber.transcribe, utt.pcm)
                if result.text:
                    self._store.append(self.meeting_id, Segment(
                        t_start=utt.t_start, t_end=utt.t_end, text=result.text,
                        lang_detected=result.lang_detected, avg_logprob=result.avg_logprob))
            except Exception:
                log.exception("transcription failed for %s at %.2fs",
                              self.meeting_id, utt.t_start)
