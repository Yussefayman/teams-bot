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

All three services are built and tested on macOS using **Groq's hosted Whisper** (STT)
and **Groq's LLM** (MoM) in place of the GPU/Azure backends — swapped via config for
production. Only the live Teams media socket (Windows) and the real Azure/Graph wiring
need a deployment environment. See `docs/PLAN.md` for the plan and `CLAUDE.md` for
working agreements.

- `docs/SETUP-AZURE.md` — manual Azure/M365 setup runbook (M0)
- `docs/RUNBOOK.md` — how to start/stop/debug each service + the demo
- `docs/M0-M1-CHECKLIST.md` — what is done vs. what needs your hands

## Full pipeline demo (Mac, real Groq — no Azure/Windows)

```bash
# put GROQ_API_KEY in stt-service/.env and orchestrator/.env, then:
scripts/demo_e2e.sh
```

media-bot (replays an Arabic WAV) → STT (Groq Whisper) → orchestrator (Groq LLM) →
Arabic MoM adaptive card + an auto-scheduled follow-up event, all printed at the end.

## Quick verification (runs anywhere, no Azure or Groq needed)

```bash
tools/verify.sh                                          # schemas + WAV oracle
.venv/bin/pytest tests stt-service orchestrator -q       # 32 Python tests (offline fakes)
dotnet test media-bot/tests/MediaBot.Tests.csproj        # 9 C# tests
```
