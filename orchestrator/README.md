# orchestrator (Components C + D)

Receives the call-ended webhook, pulls the transcript, generates the Arabic MoM, posts the
adaptive card, and schedules follow-up meetings.

## Backends
| concern | config | dev (Mac) | prod |
|---|---|---|---|
| MoM LLM | `LLM_PROVIDER` | `groq` (`GROQ_API_KEY`) | `azure-openai` |
| Graph actions | `GRAPH_ENABLED` | `false` → `FakeGraph` (records to `CARD_OUT_DIR`) | `true` → MSAL client |

## Pipeline (`POST /webhooks/call-ended`)
1. Validate body against `call_event.schema.json`; idempotent per `meetingId`.
2. Poll STT `GET /transcript/{id}` until complete.
3. `generate_mom` → LLM JSON validated against `mom.schema.json`, one retry, then a
   schema-valid degraded fallback.
4. `route_meetings` → **deterministic** Arabic date resolution (`dates.py`, not the LLM)
   decides auto-create vs. suggest. Auto-create only when a concrete day+time resolves AND
   the LLM said high.
5. Create events (`FakeGraph`/Graph) + post the RTL adaptive card to the meeting chat.

## Why dates are resolved in code
The LLM miscounts weekdays and drops the timezone (observed: it returned `...17T...Z` for
"الثلاثاء الجاي"). `dates.resolve` anchors to the meeting end + `DEFAULT_TIMEZONE`
(`Asia/Riyadh`) and returns the correct `2026-06-16T15:00:00+03:00`, or `None` when vague.

## Run / test
```bash
LLM_PROVIDER=groq GROQ_API_KEY=... ./deploy.sh
../.venv/bin/pytest                 # offline, FakeLLM + FakeGraph
```
