# Mahdar (محضر) — CLAUDE.md

## What This Project Is

A Microsoft Teams bot that joins meetings, streams live audio to our own Arabic STT, and posts an Arabic Meeting-of-Minutes (MoM) adaptive card to the meeting chat when the call ends. It also schedules follow-up meetings via Microsoft Graph.

**Repo name:** `mahdar`  
**Hard rule:** No third-party bot services (no Recall.ai). No Teams built-in transcription. Everything runs on our own infra + Microsoft APIs.

---

## Component Map

| Name | Lang | Host | Role |
|---|---|---|---|
| **media-bot** (A) | C# (.NET Framework 4.7.2) | Windows VM (Azure) | Joins call, streams PCM audio over WebSocket, emits lifecycle webhooks. Core lib multi-targets net8.0 for cross-platform unit tests. |
| **stt-service** (B) | Python | Mac/dev via Groq; GPU host for prod | VAD + Whisper (Groq hosted / faster-whisper) → Arabic transcription |
| **orchestrator** (C+D) | Python | Any host | Webhook, MoM LLM (Groq/Azure), deterministic date resolution, Graph actions |

**Dev backends (swap via config):** STT `TRANSCRIBER=groq|fake|faster-whisper`; orchestrator `LLM_PROVIDER=groq|fake|azure-openai` and `GRAPH_ENABLED` (FakeGraph on Mac). Groq gives real Arabic STT + MoM on a laptop with no GPU/Azure. The full pipeline runs on Mac via `scripts/demo_e2e.sh`. Unit tests use the `fake` backends (offline). **Never commit the Groq key** — it lives in gitignored `stt-service/.env` and `orchestrator/.env`.

**Key design rule (validated):** follow-up dates are resolved deterministically in `orchestrator/app/dates.py` (anchored to meeting end + `Asia/Riyadh`), NOT by the LLM — the LLM miscounts weekdays and drops the timezone.

Services communicate **only over the network** using JSON schemas in `shared/schemas/`. No service imports another's code.

### media-bot is three projects (decided during M1; retargeted to net472 during M2)
- `MediaBot.Core` (multi-targets `net8.0;net472`) — WavWriter, ring buffer, STT forwarder, lifecycle DTOs, `CallRunner`, `FakeCallSource`. Unit-tested on `net8.0` (Mac/Linux). The `net472` TFM exists only so Host/Graph can reference it.
- `MediaBot.Host` (`net472`, **Windows only**) — `HttpListener` HTTP API, DI, config, call-source selection. **Not ASP.NET Core** — see below.
- `MediaBot.Graph` (`net472`, **Windows only**) — `GraphCallSource`, the real Teams media socket (Graph Communications SDK). Referenced by the Host.

**Why net472 (hard constraint, learned on the VM):** the Skype/Graph application-hosted media SDK is .NET Framework only — its `MPAzAppHost` native host depends on `System.Web.Http`, and the net472 NuGet package ships the native media libs (`src/skype_media_lib/*`, copied by `build/net472/*.targets`). On .NET 8 it fails with `DllNotFoundException: NativeMedia`. Because the media SDK forces net472, the Host must also be net472, which rules out ASP.NET Core/Kestrel (net472 max is ASP.NET Core 2.1, EOL) — hence `HttpListener`. TLS is bound to the port via `netsh http add sslcert` (http.sys), not a Kestrel pfx.

`Microsoft.NETFramework.ReferenceAssemblies` lets the whole solution **compile** on Mac/Linux (`dotnet build media-bot/MediaBot.sln`), but the Host/Graph only **run** on the Windows VM (native media libs). Mac dev/CI = `dotnet test media-bot/tests/MediaBot.Tests.csproj` (Core on `net8.0`).

Audio source is pluggable behind `ICallSource`: `CALL_SOURCE=fake` (replays a WAV — Core logic + forwarder are unit/integration-tested on macOS) or `CALL_SOURCE=graph` (real meeting, Windows VM only).

---

## Repository Layout

```
mahdar/
├── media-bot/          # C# — Component A
├── stt-service/        # Python — Component B
├── orchestrator/       # Python — Components C + D
├── teams-app/          # Teams manifest package
├── shared/schemas/     # JSON schemas (transcript_segment, mom, call_event)
└── docs/               # ARCHITECTURE.md, SETUP-AZURE.md, RUNBOOK.md
```

---

## Non-Negotiable Constraints

1. **No hardcoded values anywhere** — all URLs, credentials, model names, ports via environment variables / `.env` + `pydantic-settings`. Zero exceptions.
2. **media-bot must run on Windows VM** — the Microsoft application-hosted media SDK has no Python/Linux support. Do not try to containerize it or run it on Linux.
3. **Each service has its own `deploy.sh`** — no root Makefile, no shared deploy script.
4. **Config via env, not appsettings literals** — `appsettings.template.json` shows shape; real values come from env overrides.
5. **MoM language is Arabic (MSA)** — English technical terms are preserved in Latin script inline (e.g., "نقل الـ API Gateway إلى production"). Never translate or transliterate English terms.

---

## Code Style

- **Python services:** FastAPI, `pydantic-settings`, Python 3.11+. Keep each service's `app/` clean — no cross-service imports.
- **Config module:** every service has `app/config.py` using `pydantic-settings`. All env vars defined there. Never read `os.environ` directly elsewhere.
- **No comments explaining what code does** — names should do that. Only add a comment for a non-obvious constraint, subtle invariant, or workaround.
- **No docstrings** unless a single short line genuinely adds something.
- **Prompts live in `prompts.py`** — never inline LLM prompts in pipeline code.
- **No premature abstractions** — write the straightforward version. Three similar lines is fine.
- **Logging:** structured (Serilog for C#, structlog or standard logging for Python), every lifecycle event tagged with `meeting_id` correlation ID.

---

## Key Data Schemas (in `shared/schemas/`)

### `transcript_segment.schema.json`
```json
{ "t_start": float, "t_end": float, "text": str, "lang_detected": str, "avg_logprob": float }
```

### `mom.schema.json`
```json
{
  "summary_ar": "string",
  "decisions_ar": ["string"],
  "action_items": [{ "description_ar": str, "owner_name": str, "due_hint": "str|null" }],
  "proposed_meetings": [{
    "title_ar": str, "datetime_iso": "str|null", "datetime_text": str,
    "confidence": "high|medium|low", "attendees": ["display names"]
  }],
  "language_note": "str|null"
}
```

### `call_event.schema.json`
Fields: `meetingId`, `chatThreadId`, `organizerId`, `participants`, `startedAt`, `endedAt`.

---

## Critical Implementation Details

### Media Bot (C#)
- **Build/test on Mac**: `dotnet test media-bot/tests/MediaBot.Tests.csproj` (WavWriter, ring buffer, full pipeline E2E). Run the bot locally with `CALL_SOURCE=fake` — see `media-bot/README.md`.
- Real meetings: implement the `TODO(graph-sdk)` items in `MediaBot.Graph/GraphCallSource.cs` on the Windows VM. Start from `microsoft/graph-comms-samples` — RecordingBot sample is closest.
- MVP: subscribe to the **mixed audio socket** (not per-participant). Per-speaker diarization is v1.1.
- `DUMP_AUDIO=true` → writes received PCM to a `.wav` matching `media-bot/AUDIO-FORMAT.md`. Verify with `tools/check_wav.py` (the oracle). This is the M1 acceptance check.
- WS drops: `AudioRingBuffer` keeps ~`RECONNECT_BUFFER_SECONDS` of audio, reconnect with exponential backoff, send `session_resume` on reconnect (all in `SttWebSocketForwarder`).
- Lifecycle WebSocket messages: `session_start`, `roster_update`, `session_end`, `session_resume` (`Lifecycle/ControlMessages.cs`).
- On call ended: send `session_end` over WS, then `POST /webhooks/call-ended` to orchestrator (`OrchestratorClient`).
- **Never hardcode dotnet path** in committed scripts; dev SDK is at `~/.dotnet` on this Mac (`export PATH="$HOME/.dotnet:$PATH"`).

### STT Service (Python)
- WebSocket endpoint: `wss://host/ingest/{meeting_id}`.
- VAD silence threshold: 600ms (configurable via `VAD_SILENCE_MS`).
- faster-whisper: `task="transcribe"`, never `"translate"`. `language` is configurable — benchmark `None` vs `"ar"` on real audio before locking in (see docs/STT-BENCHMARK.md).
- Use `initial_prompt` with Arabic business-meeting seed text containing common English tech terms — keeps it in `config.py`/`prompts.py`, not inline.
- Transcript store backed by an interface in `transcript_store.py` — MVP uses JSONL on disk, swappable to Postgres later.
- `GET /transcript/{meeting_id}` returns `{ status: "in_progress|complete", segments: [...] }`.

### Orchestrator (Python)
- `POST /webhooks/call-ended`: validate against `call_event.schema.json`, idempotent (ignore duplicate `meetingId`).
- Poll `GET /transcript/{meeting_id}` until `status == "complete"` (60s timeout), then run MoM pipeline.
- LLM: Azure OpenAI, JSON mode. One call. On schema validation failure → one retry with error appended → on second failure, post degraded plain-text card and log raw output.
- Relative date resolution (e.g., "الأسبوع الجاي"): use meeting end timestamp + `DEFAULT_TIMEZONE` (default `Asia/Riyadh`). If ambiguous → `datetime_iso=null`, confidence medium/low.
- Graph auth: MSAL client-credentials (app-only), token caching, retry on 429/5xx with `Retry-After`.

### Graph Actions
- **MoM posting** (primary): Bot Framework proactive message — bot is already in the call, use the connector to post adaptive card to the meeting's chat thread.
- **Graph chat API** (`POST /chats/{chatId}/messages`) is the documented fallback.
- Follow-up scheduling:
  - `confidence=high` + `datetime_iso` present → create event directly on organizer's calendar with `isOnlineMeeting=true`.
  - `confidence=medium/low` or no `datetime_iso` → suggested-meeting section on the card with a deep-link button (`https://teams.microsoft.com/l/meeting/new?...`). Upgrade to `Action.Execute` invoke later.
- Adaptive card: set `"rtl": true`. Test RTL rendering early — fallback to right-aligned TextBlocks if rendering is broken.

---

## Environment Variables

**media-bot:** `BOT_APP_ID`, `BOT_APP_SECRET`, `TENANT_ID`, `PUBLIC_HOSTNAME`, `MEDIA_PORT`, `STT_WS_BASE_URL`, `ORCHESTRATOR_BASE_URL`, `DUMP_AUDIO`

**stt-service:** `WHISPER_MODEL` (default `large-v3`), `WHISPER_COMPUTE_TYPE` (`float16|int8`), `WHISPER_LANGUAGE` (`auto|ar`), `VAD_SILENCE_MS` (600), `DATA_DIR`, `INITIAL_PROMPT_PATH`

**orchestrator:** `TENANT_ID`, `CLIENT_ID`, `CLIENT_SECRET`, `AZURE_OPENAI_ENDPOINT`, `AZURE_OPENAI_DEPLOYMENT`, `AZURE_OPENAI_API_VERSION`, `STT_BASE_URL`, `DEFAULT_TIMEZONE` (`Asia/Riyadh`), `SEND_MOM_EMAIL` (false), `AUTO_CREATE_CONFIDENCE_THRESHOLD` (high)

---

## Milestones (for context)

| # | Goal | Exit Criteria |
|---|---|---|
| M0 | Tenant setup, repo scaffold, schemas | Dev tenant live, Azure Bot registered, VM provisioned |
| **M1** | **Audio proof — the riskiest milestone** | `DUMP_AUDIO=true` → playable WAV of a 5-min test meeting |
| M2 | Live transcript | 30-min Arabic meeting → coherent transcript; WS reconnect test passes |
| M3 | MoM generation | 5 test transcripts → schema-valid Arabic MoM, English terms intact |
| M4 | Teams actions | Full happy-path demo: MoM card + calendar event + suggest flow, end-to-end |
| M5 | Hardening | Reconnect/buffer tests, idempotency, deploy.sh, RUNBOOK.md |

**Do M1 before writing any Python.** It is the single biggest technical risk.

---

## Out of MVP Scope (do not implement)

- Per-speaker diarization / diarized MoM attribution
- In-meeting side panel, live transcript UI, message extension triggers
- Calendar auto-join via Graph subscription
- Whisper fine-tuning
- Cross-meeting RAG / meeting memory
- Analytics (talk time, sentiment)
- Bilingual MoM (Arabic + English)
- Multi-tenant / compliance-recording-policy work

---

## Definition of Done

A live dev-tenant Teams meeting in Arabic + English code-switching where:
1. Bot joins on command and is visible as a participant.
2. Arabic MoM adaptive card appears in the meeting chat within **60 seconds** of meeting end.
3. An explicitly agreed follow-up ("نتقابل الثلاثاء الساعة ٣ العصر") creates a real Teams calendar event with all participants invited.
4. A vague follow-up appears as a suggested meeting requiring one click to confirm.
5. Restarting STT mid-meeting loses at most a few seconds of transcript.
6. Zero hardcoded credentials, URLs, or model names anywhere in the codebase.
