# media-bot (Component A)

Joins a Teams meeting, streams mixed PCM audio to the STT service over a WebSocket,
emits lifecycle events, and (DUMP_AUDIO) writes the audio to a `.wav` for verification.

## Project split (why three projects)

| Project | TFM | Platform | Contents |
|---|---|---|---|
| `MediaBot.Core` | `net8.0` | **any** (mac/Linux/Windows) | WavWriter, ring buffer, STT forwarder, lifecycle DTOs, `CallRunner`, `FakeCallSource` |
| `MediaBot.Host` | `net8.0` | **any** | ASP.NET host, HTTP API, DI, config, source selection |
| `MediaBot.Graph` | `net8.0-windows` | **Windows only** | `GraphCallSource` — the real Teams media socket (Graph Communications SDK) |

Raw meeting audio requires `Microsoft.Graph.Communications.Calls.Media`, which ships
native **Windows-only** media libraries — this is Microsoft's hard constraint. So only
`MediaBot.Graph` is Windows-bound. Everything else builds and is tested on macOS/Linux,
using `FakeCallSource` to replay a WAV through the *exact same* pipeline a real call uses.

## Run on a Mac (no Azure, no Windows)

```bash
export PATH="$HOME/.dotnet:$PATH"
# 1. start the throwaway STT capture server (from repo root)
CAPTURE_PORT=8799 ../.venv/bin/python ../tools/ws_capture.py &
# 2. make an input WAV (16k/16-bit/mono) — any such file works
# 3. run the bot with the fake source
CALL_SOURCE=fake FAKE_WAV_PATH=/tmp/mahdar_input.wav \
  STT_WS_BASE_URL=ws://127.0.0.1:8799 ORCHESTRATOR_BASE_URL=http://127.0.0.1:8798 \
  DUMP_AUDIO=true DUMP_DIR=/tmp/mahdar_dumps ASPNETCORE_URLS=http://127.0.0.1:8797 \
  dotnet run --project src/MediaBot.Host/MediaBot.Host.csproj
# 4. in another shell: trigger a "join"
python3 ../scripts/join.py "https://teams.example/meet/x" --meeting-id mtg-dev-001
# 5. verify the dump
python3 ../tools/check_wav.py /tmp/mahdar_dumps/dump_mtg-dev-001.wav   # -> CONFORMS
```

## Test

```bash
dotnet test tests/MediaBot.Tests.csproj   # WavWriter, ring buffer, + full pipeline E2E
```

## HTTP API

- `GET  /health` — status, selected call source, dump flag
- `POST /api/joinCall` `{ "joinUrl", "meetingId" }` — join + run the call (202 Accepted)
- `POST /api/calling` — Azure Bot calling webhook (real handling in the Windows build)

## Audio format

See `AUDIO-FORMAT.md`. Oracle: `tools/check_wav.py`.

## Windows / real meetings

`deploy.ps1` provisions the bot on the Windows VM. Set `CALL_SOURCE=graph` and the real
Azure values (`BOT_APP_ID`, `TENANT_ID`, `PUBLIC_HOSTNAME`, `CERT_THUMBPRINT`, …). Finish
the `TODO(graph-sdk)` items in `MediaBot.Graph/GraphCallSource.cs` against the pinned
`microsoft/graph-comms-samples` RecordingBot SDK version. See `docs/SETUP-AZURE.md`.
