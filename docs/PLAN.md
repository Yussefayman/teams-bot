# Teams Arabic MoM Bot — Implementation Plan

**Project codename:** `mahdar` (محضر — Arabic for "meeting minutes")
**Goal:** A fully independent Microsoft Teams application. A bot joins Teams meetings as a participant, streams live audio to our own Arabic STT service, and the moment the meeting ends it: (1) generates an Arabic Meeting-of-Minutes (MoM), (2) posts it into the meeting chat as an adaptive card, and (3) schedules any follow-up meetings detected in the conversation via Microsoft Graph.

**Hard constraints (non-negotiable):**
- No third-party meeting-bot services (no Recall.ai, etc.). Everything runs on our own infrastructure + Microsoft APIs.
- We do NOT use Teams' built-in transcription or recording. Live audio is captured by our own media bot and transcribed by our own STT.
- Meetings are primarily in **Arabic** (Egyptian and Saudi/Gulf dialects) with heavy **Arabic↔English code-switching** (technical terms in English mid-sentence). The MoM output is in Arabic.
- Everything must work end-to-end inside a Microsoft 365 Developer tenant first (we are tenant admin there), before any corporate tenant deployment.

---

## 1. System Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                       Microsoft Teams Meeting                    │
│   (participants speaking Arabic + English code-switching)        │
└──────────────────────────┬──────────────────────────────────────┘
                           │ bot joins as participant
                           ▼
┌─────────────────────────────────────────────────────────────────┐
│  COMPONENT A: Media Bot  (C# / .NET, Windows VM, public TLS)     │
│  - Microsoft Graph Communications SDK, application-hosted media  │
│  - Joins call via Graph Calling API                              │
│  - Receives raw PCM audio (16 kHz, 16-bit mono)                  │
│  - Streams audio frames over WebSocket to Component B            │
│  - Emits call lifecycle events (joined, participant list,        │
│    callEnded) to Component C via HTTP webhook                    │
└──────────────────────────┬──────────────────────────────────────┘
                           │ WebSocket: binary PCM frames + JSON control msgs
                           ▼
┌─────────────────────────────────────────────────────────────────┐
│  COMPONENT B: STT Service  (Python / FastAPI, GPU host)          │
│  - WebSocket server accepting audio streams per meeting          │
│  - Silero VAD → segment audio into utterances                    │
│  - faster-whisper (Whisper large-v3) → Arabic/code-switched      │
│    transcription                                                 │
│  - Appends timestamped segments to per-meeting transcript store  │
│  - REST endpoint: GET /transcript/{meeting_id}                   │
└──────────────────────────┬──────────────────────────────────────┘
                           │ transcript (JSON) on callEnded
                           ▼
┌─────────────────────────────────────────────────────────────────┐
│  COMPONENT C: Orchestrator + MoM Pipeline (Python / FastAPI)     │
│  - Receives callEnded webhook from Component A                   │
│  - Pulls full transcript from Component B                        │
│  - LLM call (Azure OpenAI) → structured Arabic MoM (JSON)        │
│  - Validates/parses MoM JSON                                     │
│  - Triggers Component D actions                                  │
└──────────────────────────┬──────────────────────────────────────┘
                           │ Microsoft Graph REST calls
                           ▼
┌─────────────────────────────────────────────────────────────────┐
│  COMPONENT D: Graph Actions (part of Component C codebase)       │
│  - Post adaptive card (Arabic MoM) into the meeting chat         │
│  - For each detected follow-up meeting: create calendar event    │
│    (POST /users/{organizer}/events) OR post "suggested meeting"  │
│    card with confirm button when date/attendees are uncertain    │
└─────────────────────────────────────────────────────────────────┘
```

**Why this split:** Component A is the only piece that MUST be C#/.NET on Windows (Microsoft's application-hosted media SDK has no Python/Linux support — this is a hard platform constraint, confirmed by Microsoft). Components B, C, D are Python and can run anywhere (Linux, Mac dev, GPU box). Component A is deliberately kept "dumb" — it is only an audio pipe + lifecycle event emitter. All intelligence lives in Python.

---

## 2. Repository Structure

Monorepo, one repo: `mahdar`

```
mahdar/
├── README.md
├── docs/
│   ├── ARCHITECTURE.md
│   ├── SETUP-AZURE.md           # app registration, permissions, policy steps
│   └── RUNBOOK.md               # how to start/stop everything, debug
├── media-bot/                   # COMPONENT A — C# / .NET
│   ├── MediaBot.sln
│   ├── src/
│   │   ├── Bot/                 # call handling, Graph Communications SDK
│   │   ├── Audio/               # audio socket → WebSocket forwarder
│   │   └── Http/                # /joinCall endpoint, lifecycle webhooks out
│   ├── deploy.sh                # or deploy.ps1 — VM deployment script
│   └── appsettings.template.json
├── stt-service/                 # COMPONENT B — Python
│   ├── app/
│   │   ├── main.py              # FastAPI app + WebSocket endpoint
│   │   ├── vad.py               # Silero VAD segmentation
│   │   ├── transcriber.py       # faster-whisper wrapper
│   │   ├── transcript_store.py  # per-meeting transcript persistence
│   │   └── config.py            # pydantic-settings, env-driven, NO hardcoded values
│   ├── tests/
│   ├── requirements.txt
│   └── deploy.sh
├── orchestrator/                # COMPONENTS C + D — Python
│   ├── app/
│   │   ├── main.py              # FastAPI: webhook receivers
│   │   ├── mom_pipeline.py      # LLM call, JSON validation
│   │   ├── prompts.py           # all prompts in one module
│   │   ├── graph_client.py      # MS Graph auth + API wrappers
│   │   ├── adaptive_cards.py    # MoM card + suggested-meeting card builders
│   │   ├── scheduler.py         # follow-up event creation logic
│   │   └── config.py
│   ├── tests/
│   ├── requirements.txt
│   └── deploy.sh
├── teams-app/                   # Teams app manifest package
│   ├── manifest.json
│   ├── color.png                # 192x192 icon
│   └── outline.png              # 32x32 icon
└── shared/
    └── schemas/                 # JSON schemas shared across services
        ├── transcript_segment.schema.json
        ├── mom.schema.json
        └── call_event.schema.json
```

**Code style preferences (apply throughout):**
- Per-service `deploy.sh` scripts, NOT a sprawling root Makefile.
- All configuration via environment variables / `.env` files with `pydantic-settings`. Zero hardcoded URLs, keys, model names, or ports anywhere in code.
- Each service independently runnable with a single command.
- Clean separation: no service imports another service's code; they communicate only over the network using the JSON schemas in `shared/schemas/`.

---

## 3. Azure / Microsoft 365 Setup (Prerequisites)

Document every step in `docs/SETUP-AZURE.md` as it's done. Required setup, in order:

### 3.1 Microsoft 365 Developer Tenant
- Sign up for the Microsoft 365 Developer Program → get a free dev tenant with admin rights and test users (create at least 3 test users for multi-participant meeting tests).
- Enable custom app sideloading: Teams Admin Center → Teams apps → Setup policies → enable "Upload custom apps".

### 3.2 Entra ID App Registration (single multi-purpose app)
- Register one app in Entra ID (Azure AD). Record: `TENANT_ID`, `CLIENT_ID`, create a `CLIENT_SECRET`.
- **Application permissions** (admin consent required — we are admin in dev tenant):
  - `Calls.AccessMedia.All` — required for application-hosted media (raw audio access)
  - `Calls.JoinGroupCall.All` — bot joins scheduled meetings
  - `Calls.JoinGroupCallAsGuest.All` — fallback join mode
  - `Calls.Initiate.All`
  - `OnlineMeetings.Read.All` — resolve meeting join URLs to call info
  - `Chat.ReadWrite.All` / `ChatMessage.Send` path for posting MoM (see 3.5 alternative)
  - `Calendars.ReadWrite` — create follow-up events on organizer's calendar
  - `User.Read.All` — resolve participant display names/emails
- Grant admin consent in the portal.

### 3.3 Azure Bot Registration
- Create an **Azure Bot** resource linked to the app registration above.
- Add the **Microsoft Teams channel**, and under Teams channel settings enable **"Calling"** with the calling webhook pointing at the media bot's public endpoint: `https://<media-bot-host>/api/calling`.

### 3.4 Media Bot Infrastructure
- **Windows Server VM** in Azure (Standard D4s v5 or similar to start). Application-hosted media bots cannot run in App Service/containers/Linux — VM (or VMSS later) is required.
- Public DNS name + **TLS certificate** for the media endpoint (the Communications SDK requires a valid cert bound to the media port). Use a real domain + Let's Encrypt or an Azure-issued cert; document the binding steps (netsh/cert store) in SETUP-AZURE.md.
- Open required ports: 443 (signaling/HTTP), plus the media TCP port range per SDK config (commonly 8445).
- The Python services (B, C) run elsewhere — Linux VM, on-prem GPU machine, or dev laptop — reachable from the media bot via WebSocket/HTTP. During development this can be a tunnel (e.g., dev tunnel/ngrok-style) but document the production path.

### 3.5 Posting to Meeting Chat — implementation choice
Two supported options; implement Option 1, keep Option 2 documented as fallback:
1. **Bot Framework proactive message:** since the bot is installed in the meeting (it joined the call), use the Bot Framework connector to send an activity (adaptive card) to the meeting's chat thread. Requires capturing the conversation/chat thread ID when the bot joins.
2. **Graph chat API:** `POST /chats/{chatId}/messages` with application permissions (requires `Chat.ReadWrite.All` and may need protected-API access approval in corporate tenants — fine in dev tenant).

---

## 4. Component A — Media Bot (C# / .NET)

### 4.1 Starting point
- Fork/clone `microsoft/graph-comms-samples`. The most relevant samples: **AudioVideoPlaybackBot** and the **ComplianceRecording/RecordingBot** sample (the recording bot is closest: it joins calls and accesses inbound audio).
- Target .NET Framework / .NET version as required by the current `Microsoft.Graph.Communications.*` NuGet packages (check the sample's current target — historically .NET Framework 4.7+; some newer samples target .NET 6+ on Windows). Do not fight the SDK's platform requirements.

### 4.2 Responsibilities (keep it minimal)
1. **HTTP API:**
   - `POST /api/joinCall` — body: `{ "joinUrl": "<teams meeting join link>", "meetingId": "<our internal id>" }`. Resolves the join URL via Graph and joins the call with application-hosted media.
   - `GET /health`
   - `POST /api/calling` — the Azure Bot calling webhook (incoming call notifications, required by platform).
2. **On call joined:**
   - Open a WebSocket connection to STT service: `wss://<stt-host>/ingest/{meetingId}`.
   - Send a JSON control message: `{ "type": "session_start", "meetingId", "joinUrl", "participants": [...], "chatThreadId": "<captured thread id>", "organizerId": "<aad user id>" }`.
3. **Audio streaming:**
   - Subscribe to the **mixed audio socket** (MVP decision: mixed stream, not per-participant — simpler, one STT stream per meeting; per-speaker is a documented v1.1 upgrade).
   - Audio arrives as PCM 16 kHz, 16-bit, mono frames. Forward each frame as a binary WebSocket message. Prefix or wrap with a tiny header only if needed; otherwise raw binary frames + the session_start metadata is enough.
4. **Roster tracking:** on participant join/leave, send `{ "type": "roster_update", "participants": [{ "displayName", "aadId", "email?" }] }` over the same WebSocket (used later for MoM attendee list).
5. **On call ended:**
   - Send `{ "type": "session_end", "meetingId" }` over WebSocket, close it.
   - `POST` to orchestrator: `POST /webhooks/call-ended` with `{ meetingId, chatThreadId, organizerId, participants, startedAt, endedAt }`.
6. **Logging:** structured logs (Serilog) — every lifecycle event, audio socket status, reconnect attempts.

### 4.3 Resilience requirements
- If the STT WebSocket drops, buffer audio in memory (ring buffer, cap ~60s) and reconnect with exponential backoff; on reconnect, send a `session_resume` control message.
- The bot must handle being removed from the call by a participant (treat as call ended).
- Configuration (`appsettings` + env overrides): bot credentials, public hostname, media port, STT WebSocket base URL, orchestrator base URL.

### 4.4 Acceptance test for Component A alone
- Join a real Teams meeting in the dev tenant via `POST /api/joinCall`.
- Bot appears as participant within ~10 seconds.
- A debug mode flag (`DUMP_AUDIO=true`) writes the received PCM to a local `.wav` file. Play the file → clear meeting audio confirms the media pipeline works **before any Python exists**. This is the single riskiest milestone in the whole project; do it first.

---

## 5. Component B — STT Service (Python)

### 5.1 Stack
- Python 3.11+, FastAPI, `websockets`/Starlette WebSocket support.
- **faster-whisper** running **Whisper large-v3** (GPU). Fallback config for smaller model (`large-v3-turbo` or `medium`) for low-VRAM dev machines — model name strictly from env config.
- **Silero VAD** for utterance segmentation.
- Storage: per-meeting transcript as JSONL on disk for MVP (`data/transcripts/{meeting_id}.jsonl`); design `transcript_store.py` behind an interface so it can be swapped for Postgres later.

### 5.2 Audio ingestion flow
1. WebSocket endpoint `wss://host/ingest/{meeting_id}`.
2. Receive `session_start` JSON → create transcript session, persist metadata (participants, chat thread id, organizer).
3. Receive binary PCM frames → append to a rolling buffer.
4. **VAD loop:** run Silero VAD over the buffer; when an utterance is closed by ≥600 ms of silence (configurable), cut the segment.
5. **Transcription:** push segment to an asyncio queue → worker transcribes with faster-whisper:
   - `language=None` (auto-detect) OR `language="ar"` — make this configurable; **benchmark both** on real code-switched audio. Auto-detect sometimes handles code-switching better; forcing `ar` sometimes transliterates English terms into Arabic script. Pick per benchmark results (see §9).
   - `task="transcribe"` (never translate).
   - Use `initial_prompt` with a short Arabic business-meeting seed text containing common technical English terms — improves code-switching behavior. Keep the prompt in `prompts.py`/config, not inline.
6. Append result to transcript store: `{ "t_start": float, "t_end": float, "text": str, "lang_detected": str, "avg_logprob": float }` (timestamps relative to meeting start).
7. On `session_end`: flush remaining audio, mark transcript complete.

### 5.3 REST API
- `GET /transcript/{meeting_id}` → `{ "meeting_id", "status": "in_progress|complete", "metadata": {...}, "segments": [...] }`
- `GET /health` (include GPU availability + loaded model name).

### 5.4 Performance requirements
- Must keep up with real time on the dev GPU (RTX 4070-class): large-v3 with faster-whisper int8/float16 on segmented utterances is comfortably faster than real time; verify with a sustained 30-minute test.
- Single meeting at a time is acceptable for MVP; structure code so sessions are isolated per `meeting_id` (no globals) so concurrency is a config change, not a rewrite.

---

## 6. Components C + D — Orchestrator, MoM Pipeline, Graph Actions (Python)

### 6.1 Webhook receiver
- `POST /webhooks/call-ended` (from media bot). Validates payload against `call_event.schema.json`. Idempotent: ignore duplicate events for the same `meetingId`.
- On receipt: poll `GET /transcript/{meeting_id}` until `status == "complete"` (timeout 60s), then run the MoM pipeline.

### 6.2 MoM pipeline (`mom_pipeline.py`)
1. **Assemble input:** full transcript text (with timestamps), participant list with display names, meeting start/end time, meeting subject (from join metadata if available).
2. **LLM call:** Azure OpenAI (deployment name, endpoint, API version all from env). One call, JSON-mode/structured output. The output MUST validate against `shared/schemas/mom.schema.json`:

```json
{
  "summary_ar": "string — فقرة ملخص بالعربية",
  "decisions_ar": ["string"],
  "action_items": [
    {
      "description_ar": "string",
      "owner_name": "string — match to a participant display name when possible",
      "due_hint": "string|null — e.g. 'نهاية الأسبوع'"
    }
  ],
  "proposed_meetings": [
    {
      "title_ar": "string",
      "datetime_iso": "string|null — ISO 8601 with timezone IF explicitly stated",
      "datetime_text": "string — the verbatim phrase, e.g. 'الثلاثاء الجاي الساعة ٣'",
      "confidence": "high|medium|low",
      "attendees": ["participant display names"]
    }
  ],
  "language_note": "string|null — e.g. mixed Arabic/English terms preserved"
}
```

3. **Prompt requirements** (`prompts.py`, system prompt in Arabic+English):
   - Output MoM in **Arabic** (Modern Standard Arabic for the written MoM, regardless of spoken dialect).
   - **Preserve English technical terms as-is** in Latin script inside the Arabic text (e.g., "تم الاتفاق على نقل الـ API Gateway إلى production"). Do not translate or transliterate technical terms.
   - Extract follow-up meetings ONLY when actually discussed; never invent. `confidence=high` only when a concrete day/time was stated.
   - Resolve relative dates ("بكرة", "الأسبوع الجاي") into `datetime_iso` using the meeting end timestamp + tenant timezone (config: default `Asia/Riyadh`, overridable) — but if any ambiguity, leave `datetime_iso=null` and set confidence medium/low.
   - Attribute action-item owners only when clearly identifiable from the transcript; otherwise `owner_name = "غير محدد"`.
4. **Validation & retry:** parse JSON, validate schema; on failure, one retry with the validation error appended; on second failure, post a degraded plain-text MoM card and log the raw output.

### 6.3 Graph client (`graph_client.py`)
- MSAL client-credentials flow (app-only token), token caching, simple retry on 429/5xx with `Retry-After` respect.
- Wrappers: `post_chat_message(chat_id, card_payload)`, `create_event(organizer_id, event_payload)`, `get_user(aad_id)`.

### 6.4 Actions (`scheduler.py` + `adaptive_cards.py`)
1. **MoM card** posted to the meeting chat: RTL-friendly adaptive card —
   - Header: "📋 محضر الاجتماع" + meeting title + date
   - Sections: الملخص / القرارات / بنود العمل (table: البند، المسؤول، الموعد) / الاجتماعات المقترحة
   - Note: Adaptive Cards have limited RTL support — set `"rtl": true` on the card (supported in recent schema versions) and verify rendering in Teams; if rendering is poor, fall back to right-aligned TextBlocks. Test this early.
2. **Follow-up scheduling logic:**
   - `confidence == high` AND `datetime_iso` present → create the event directly on the organizer's calendar (`POST /users/{organizerId}/events`) with `isOnlineMeeting=true, onlineMeetingProvider="teamsForBusiness"`, attendees resolved to emails from roster. Then append a line to the MoM card: "✅ تم جدولة: {title} — {datetime}".
   - `confidence in (medium, low)` OR no `datetime_iso` → do NOT auto-create. Include it in a "اجتماعات مقترحة" section of the card with an `Action.Execute`/`Action.Submit` **"تأكيد الجدولة" button**. The button posts back to the bot (Bot Framework invoke) → orchestrator endpoint `POST /actions/confirm-meeting` creates the event. MVP-acceptable alternative if invoke wiring is heavy: deep-link button that opens a pre-filled Teams calendar compose (`https://teams.microsoft.com/l/meeting/new?...`) — implement the deep-link version first, upgrade to invoke later.
3. **Email of the MoM (stretch within MVP):** `POST /users/{organizerId}/sendMail` with the MoM as HTML to all participants. Behind a feature flag `SEND_MOM_EMAIL` (default off).

---

## 7. Teams App Packaging

`teams-app/manifest.json` (manifest schema v1.17+):
- Bot definition referencing the Azure Bot `botId` (= app CLIENT_ID), with `supportsCalling: true, supportsVideo: false`, scopes: `team`, `groupChat`.
- `webApplicationInfo` for the app registration.
- MVP activation path: **no in-meeting UI**. The bot is invoked via `POST /api/joinCall` (manual/cli trigger) or — preferred for daily use — a tiny CLI script `scripts/join.py <teams-join-url>` that calls the media bot. In-meeting side panel, message-extension triggers, and calendar auto-join are explicitly OUT of MVP scope (documented in §11 roadmap).
- Package: zip manifest + two icons; sideload via Teams Admin Center or Teams client "Upload a custom app".

---

## 8. End-to-End Data Flow (happy path, step-by-step)

1. Operator runs `python scripts/join.py "<meeting join url>"` → media bot `POST /api/joinCall`.
2. Bot joins meeting (visible as "Mahdar Bot"), announces nothing (MVP), opens WS to STT, sends `session_start` (+ roster).
3. Participants talk in Arabic/English mix; PCM frames stream continuously; STT produces timestamped Arabic segments in near-real-time (visible via `GET /transcript/{id}` for debugging).
4. Meeting ends (last human leaves / bot removed / organizer ends) → bot sends `session_end`, POSTs call-ended webhook to orchestrator, leaves call.
5. Orchestrator pulls complete transcript, runs MoM LLM call, validates JSON.
6. Orchestrator posts Arabic MoM adaptive card to the meeting chat.
7. High-confidence follow-ups → calendar events created with Teams links + invites; uncertain ones → "confirm" buttons/deep links on the card.
8. (flag) MoM email sent to participants.
9. All steps logged with `meeting_id` correlation ID across all three services.

---

## 9. Arabic STT Quality — Benchmark Task (do during Week 2)

Before locking the STT config, run a structured benchmark:
- **Test set:** 3–5 real recorded meetings (or simulated ones recorded by the team): Egyptian Arabic, Saudi/Gulf Arabic, heavy code-switching with software/banking terms.
- **Variants to compare:**
  1. faster-whisper large-v3, `language=None`
  2. faster-whisper large-v3, `language="ar"`
  3. large-v3 with the Arabic-business `initial_prompt`
  4. (baseline) Azure Speech `ar-EG`/`ar-SA` continuous recognition — for comparison only
- **Metrics:** WER on Arabic portions, how English terms survive (kept in Latin script vs mangled), subjective MoM quality when each transcript is fed to the same MoM prompt.
- **Output:** a short `docs/STT-BENCHMARK.md` with the chosen config. If base Whisper is insufficient on dialectal audio, note fine-tuning (LoRA on ArzEn/ZAEBUC-style code-switched data) as the first post-MVP task — do NOT block the MVP on fine-tuning.

---

## 10. Milestones & Build Order

**M0 — Foundations (days 1–3)**
- Dev tenant + app registration + admin consent + Azure Bot with calling enabled.
- Windows VM provisioned, DNS + TLS cert bound.
- Repo scaffolded per §2, schemas written, configs templated.

**M1 — Audio proof (days 3–10) — THE critical milestone**
- Media bot joins a dev-tenant meeting and dumps clean PCM/WAV to disk (`DUMP_AUDIO=true`).
- Exit criteria: a playable WAV of a 5-minute test meeting.

**M2 — Live transcript (days 10–17)**
- WS pipeline media-bot → STT service; live Arabic segments appearing in transcript store during a real meeting.
- Run the §9 benchmark; lock STT config.
- Exit criteria: a 30-minute Arabic test meeting yields a coherent transcript file with <1 segment-loss on a forced WS reconnect test.

**M3 — MoM generation (days 17–22)**
- callEnded webhook → orchestrator → Azure OpenAI → validated MoM JSON.
- Exit criteria: 5 different test transcripts each produce schema-valid Arabic MoM with sensible decisions/action items; English terms preserved.

**M4 — Teams actions (days 22–28)**
- Adaptive card (RTL verified in Teams client) posted to meeting chat.
- High-confidence follow-up → real calendar event with Teams link + invites received by test users.
- Uncertain follow-up → suggested-meeting entry with working deep-link/confirm path.
- Exit criteria: full happy-path demo (§8) on a live meeting with 3 test users, end-to-end, MoM in chat within 60 seconds of meeting end.

**M5 — Hardening (days 28–35)**
- Reconnect/buffering tests, idempotency, structured logging across services, RUNBOOK.md, deploy.sh for each service, feature flags wired.

---

## 11. Explicitly OUT of MVP scope (do not implement yet)

- Cross-meeting RAG / meeting memory / search.
- Per-speaker audio streams & diarized MoM attribution (v1.1).
- In-meeting side panel / live transcript UI / message extension triggers.
- Calendar auto-join (Graph subscription on calendars).
- Whisper fine-tuning (post-MVP, pending benchmark results).
- Analytics (talk time, sentiment).
- Bilingual (Arabic+English) dual MoM output — single Arabic MoM only for MVP.
- Multi-tenant / production compliance-recording-policy work — dev tenant only.

---

## 12. Risks & Mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| Application-hosted media SDK setup (cert, ports, Windows) stalls | Blocks everything | M1 first; follow the recording-bot sample exactly before customizing; budget the full week |
| Whisper weak on dialectal Arabic | Bad MoM quality | §9 benchmark with Azure Speech fallback path; fine-tune later |
| Adaptive card RTL renders badly | Ugly MoM | Test card rendering in M0 with a static sample card; fallback to right-aligned text |
| Relative Arabic dates parsed wrong → wrong meetings created | User trust | Auto-create ONLY on high confidence + explicit datetime; everything else is suggest+confirm |
| WS drops mid-meeting | Lost transcript chunks | Ring buffer + resume protocol; tested in M5 |
| Graph chat posting via app permissions blocked | MoM not delivered | Primary path is Bot Framework proactive message (bot is already in the meeting); Graph chat API is fallback |

---

## 13. Configuration Reference (env vars per service)

**media-bot:** `BOT_APP_ID, BOT_APP_SECRET, TENANT_ID, PUBLIC_HOSTNAME, MEDIA_PORT, STT_WS_BASE_URL, ORCHESTRATOR_BASE_URL, DUMP_AUDIO`

**stt-service:** `WHISPER_MODEL (default large-v3), WHISPER_COMPUTE_TYPE (float16|int8), WHISPER_LANGUAGE (auto|ar), VAD_SILENCE_MS (600), DATA_DIR, INITIAL_PROMPT_PATH`

**orchestrator:** `TENANT_ID, CLIENT_ID, CLIENT_SECRET, AZURE_OPENAI_ENDPOINT, AZURE_OPENAI_DEPLOYMENT, AZURE_OPENAI_API_VERSION, STT_BASE_URL, DEFAULT_TIMEZONE (Asia/Riyadh), SEND_MOM_EMAIL (false), AUTO_CREATE_CONFIDENCE_THRESHOLD (high)`

---

## 14. Definition of Done (MVP)

A live Teams meeting in the dev tenant, conducted in Arabic with English code-switching, where:
1. The bot joins on command and is visible as a participant.
2. Within 60 seconds of the meeting ending, a well-formatted Arabic MoM adaptive card appears in the meeting chat containing summary, decisions, and action items with owners.
3. A follow-up meeting explicitly agreed in the conversation ("نتقابل الثلاثاء الساعة ٣ العصر") appears as a real Teams calendar event with all participants invited.
4. A vaguely-mentioned follow-up appears as a suggested meeting requiring one click to confirm.
5. Killing and restarting the STT service mid-meeting loses at most a few seconds of transcript.
6. No credentials, URLs, or model names are hardcoded anywhere; each service deploys with its own `deploy.sh`.
