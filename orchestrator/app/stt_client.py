"""Pulls the finished transcript from the STT service (plan §6.1)."""
from __future__ import annotations

import asyncio

import httpx


class SttClient:
    def __init__(self, base_url: str, timeout_s: int = 60):
        self._base = base_url.rstrip("/")
        self._timeout_s = timeout_s

    async def wait_for_transcript(self, meeting_id: str, poll_interval_s: float = 1.0) -> dict:
        """Poll GET /transcript/{id} until status == complete or timeout. Returns the
        transcript dict (possibly still in_progress on timeout — caller decides)."""
        deadline = asyncio.get_event_loop().time() + self._timeout_s
        async with httpx.AsyncClient(timeout=15) as client:
            last = {"meeting_id": meeting_id, "status": "unknown", "segments": []}
            while asyncio.get_event_loop().time() < deadline:
                resp = await client.get(f"{self._base}/transcript/{meeting_id}")
                if resp.status_code == 200:
                    last = resp.json()
                    if last.get("status") == "complete":
                        return last
                await asyncio.sleep(poll_interval_s)
            return last
