#!/usr/bin/env python3
"""Throwaway STT WebSocket capture server for the media-bot Host smoke test.

Accepts /ingest/{meetingId}, counts binary PCM bytes, records text control messages,
and writes a JSON summary to the path in CAPTURE_OUT when the socket closes.
Not part of the real STT service — just an oracle for the Host smoke test.
"""
import asyncio
import json
import os

import websockets

OUT = os.environ.get("CAPTURE_OUT", "/tmp/mahdar_capture.json")
PORT = int(os.environ.get("CAPTURE_PORT", "8799"))


async def handler(ws):
    path = getattr(getattr(ws, "request", None), "path", "?")
    summary = {"path": path, "binary_bytes": 0, "text_messages": []}
    try:
        async for msg in ws:
            if isinstance(msg, (bytes, bytearray)):
                summary["binary_bytes"] += len(msg)
            else:
                summary["text_messages"].append(json.loads(msg))
    finally:
        with open(OUT, "w", encoding="utf-8") as f:
            json.dump(summary, f, ensure_ascii=False, indent=2)
        print(f"capture written: {OUT} ({summary['binary_bytes']} bytes, "
              f"{len(summary['text_messages'])} control msgs)")


async def main():
    async with websockets.serve(handler, "127.0.0.1", PORT):
        print(f"ws capture listening on ws://127.0.0.1:{PORT}")
        await asyncio.Future()


if __name__ == "__main__":
    asyncio.run(main())
