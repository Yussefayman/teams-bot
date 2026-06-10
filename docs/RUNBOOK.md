# RUNBOOK.md — start / stop / debug

## Local verification (no Azure, runs on any machine)

```bash
tools/verify.sh          # schema validation + WAV oracle self-test
.venv/bin/pytest -q      # same checks via pytest
```

## media-bot

### Dev on a Mac (fake source)
```bash
export PATH="$HOME/.dotnet:$PATH"
CAPTURE_PORT=8799 .venv/bin/python tools/ws_capture.py &       # throwaway STT
CALL_SOURCE=fake FAKE_WAV_PATH=/tmp/in.wav \
  STT_WS_BASE_URL=ws://127.0.0.1:8799 ORCHESTRATOR_BASE_URL=http://127.0.0.1:8798 \
  DUMP_AUDIO=true DUMP_DIR=/tmp/mahdar_dumps ASPNETCORE_URLS=http://127.0.0.1:8797 \
  dotnet run --project media-bot/src/MediaBot.Host/MediaBot.Host.csproj
# trigger:
python3 scripts/join.py "https://teams.example/meet/x" --meeting-id mtg-dev-001
# verify:
python3 tools/check_wav.py /tmp/mahdar_dumps/dump_mtg-dev-001.wav
```

### Tests
```bash
dotnet test media-bot/tests/MediaBot.Tests.csproj
```

### Prod on the Windows VM (real meetings)
`CALL_SOURCE=graph` + Azure env vars; start `Mahdar.MediaBot.Host.exe` (or a Windows
service). See `docs/SETUP-AZURE.md`.

## Full pipeline demo (Mac, real Groq)
```bash
# needs GROQ_API_KEY in stt-service/.env and orchestrator/.env
scripts/demo_e2e.sh
```
Runs media-bot(fake audio) → STT(Groq Whisper) → orchestrator(Groq LLM + FakeGraph) and
prints the transcript, the auto-created event, and the Arabic MoM adaptive card.

## stt-service (Component B)
```bash
cd stt-service && TRANSCRIBER=groq GROQ_API_KEY=... ./deploy.sh   # :8799
../.venv/bin/pytest          # offline tests (FakeTranscriber)
```

## orchestrator (Components C + D)
```bash
cd orchestrator && LLM_PROVIDER=groq GROQ_API_KEY=... GRAPH_ENABLED=false ./deploy.sh  # :8798
../.venv/bin/pytest          # offline tests (FakeLLM + FakeGraph)
```

## All tests at once
```bash
.venv/bin/pytest tests stt-service orchestrator -q
dotnet test media-bot/tests/MediaBot.Tests.csproj
```

## Debugging
- Every service tags logs with the `meetingId` correlation id.
- media-bot `/health` shows the selected call source, dump flag, and whether STT is
  configured.
- `DUMP_AUDIO=true` + `tools/check_wav.py` is the fastest way to confirm the audio path
  end-to-end before involving the STT model.

## Ports (defaults)
| Service | Port |
|---|---|
| media-bot HTTP | 8797 |
| stt-service WS | 8799 |
| orchestrator HTTP | 8798 |
| media TCP (Windows, Graph) | 8445 |
