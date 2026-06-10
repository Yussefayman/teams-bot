"""COMPONENT B — STT service. FastAPI WebSocket ingest + transcript REST (plan §5)."""
from __future__ import annotations

import json
import logging

from fastapi import FastAPI, WebSocket, WebSocketDisconnect

from app.config import Settings, get_settings
from app.session import Session
from app.transcriber import Transcriber, build_transcriber
from app.transcript_store import JsonlTranscriptStore, SessionMeta
from app.vad import EnergySegmenter

logging.basicConfig(level=logging.INFO)
log = logging.getLogger("stt.main")


def create_app(settings: Settings, transcriber: Transcriber | None = None) -> FastAPI:
    app = FastAPI(title="mahdar stt-service")
    store = JsonlTranscriptStore(settings.data_dir)
    tx = transcriber or build_transcriber(settings)

    def new_segmenter() -> EnergySegmenter:
        return EnergySegmenter(
            sample_rate=settings.sample_rate,
            silence_ms=settings.vad_silence_ms,
            max_segment_ms=settings.vad_max_segment_ms,
            min_segment_ms=settings.vad_min_segment_ms,
            energy_threshold=settings.vad_energy_threshold,
        )

    @app.get("/health")
    def health():
        return {
            "status": "ok",
            "transcriber": settings.transcriber,
            "model": settings.groq_model if settings.transcriber == "groq" else settings.whisper_model,
            "language": settings.whisper_language,
        }

    @app.get("/transcript/{meeting_id}")
    def get_transcript(meeting_id: str):
        return store.read(meeting_id)

    @app.websocket("/ingest/{meeting_id}")
    async def ingest(ws: WebSocket, meeting_id: str):
        await ws.accept()
        session = Session(meeting_id, store, tx, new_segmenter())
        started = False
        try:
            while True:
                msg = await ws.receive()
                if msg["type"] == "websocket.disconnect":
                    break
                if (data := msg.get("bytes")) is not None:
                    if started:
                        await session.feed(data)
                    continue
                text = msg.get("text")
                if text is None:
                    continue
                ctrl = json.loads(text)
                kind = ctrl.get("type")
                if kind == "session_start":
                    await session.start(SessionMeta(
                        meeting_id=meeting_id,
                        join_url=ctrl.get("joinUrl", ""),
                        chat_thread_id=ctrl.get("chatThreadId", ""),
                        organizer_id=ctrl.get("organizerId", ""),
                        participants=ctrl.get("participants", []),
                    ))
                    started = True
                elif kind == "session_resume":
                    log.info("session %s resumed", meeting_id)
                elif kind == "session_end":
                    break
        except WebSocketDisconnect:
            pass
        finally:
            if started:
                await session.end()

    return app


# Module-level app for `uvicorn app.main:app`.
app = create_app(get_settings())
