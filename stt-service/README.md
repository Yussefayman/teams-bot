# stt-service (Component B)

Receives mixed PCM audio over a WebSocket, segments it into utterances (VAD), transcribes
each with Whisper, and serves the per-meeting transcript.

## Transcriber backends (config `TRANSCRIBER`)
| value | use | needs |
|---|---|---|
| `groq` | **dev on Mac** — Whisper large-v3 via Groq's hosted API | `GROQ_API_KEY` |
| `fake` | offline unit/integration tests | nothing |
| `faster-whisper` | production on a GPU host (plan §5) | GPU + `pip install -r requirements.txt` |

The `groq` path needs no GPU and gives real Arabic transcription on a laptop. The Arabic
`initial_prompt` (`app/initial_prompt_ar.txt`) seeds English tech terms to improve
code-switching (validated: it preserved "API, Gateway, production" in Latin script).

## Run
```bash
TRANSCRIBER=groq GROQ_API_KEY=... ./deploy.sh        # or rely on .env
```

## API
- `WS /ingest/{meeting_id}` — `session_start` (JSON) → binary PCM frames → `session_end`.
- `GET /transcript/{meeting_id}` → `{status, metadata, segments[]}` (segments match
  `shared/schemas/transcript_segment.schema.json`).
- `GET /health` — backend + model.

## Test
```bash
../.venv/bin/pytest      # offline, uses FakeTranscriber
```

## Notes
- VAD is a torch-free `EnergySegmenter` (default) so it runs anywhere; swap to Silero on
  the GPU box via config. Cuts on `VAD_SILENCE_MS` of silence or `vad_max_segment_ms`.
- Transcription runs off the event loop (`asyncio.to_thread`) so audio ingest never blocks.
