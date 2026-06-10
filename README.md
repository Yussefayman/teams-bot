# mahdar (محضر)

Self-hosted Microsoft Teams bot that joins meetings, transcribes Arabic + English
code-switched audio with our own STT, and posts an Arabic Meeting-of-Minutes (MoM)
adaptive card to the meeting chat when the call ends. It also schedules follow-up
meetings via Microsoft Graph.

No third-party meeting-bot services. No Teams built-in transcription. Everything runs
on our own infrastructure + Microsoft APIs.

## Components

| Dir | Lang | Host | Role |
|---|---|---|---|
| `media-bot/` | C# / .NET | Windows VM | Joins call, streams PCM audio over WebSocket, emits lifecycle webhooks |
| `stt-service/` | Python | GPU host | Silero VAD + faster-whisper large-v3 → Arabic transcription |
| `orchestrator/` | Python | Any Linux | MoM LLM pipeline + Microsoft Graph actions |

Services talk only over the network using the JSON schemas in `shared/schemas/`.

## Status

Building **M0** (foundations) and **M1** (audio proof). See `docs/PLAN.md` for the
full plan and `CLAUDE.md` for working agreements.

- `docs/SETUP-AZURE.md` — manual Azure/M365 setup runbook (M0)
- `docs/RUNBOOK.md` — how to start/stop/debug each service
- `docs/M0-M1-CHECKLIST.md` — what is done vs. what needs your hands

## Quick verification (runs anywhere, no Azure needed)

```bash
tools/verify.sh
```

Validates the JSON schemas against their example fixtures and runs the WAV-format
reference test (the oracle for the M1 audio dump).
