#!/usr/bin/env bash
# Full mahdar pipeline on a Mac, no Azure/Windows:
#   media-bot (fake source = real Arabic WAV) -> STT (Groq Whisper) -> orchestrator
#   (Groq LLM + FakeGraph) -> Arabic MoM adaptive card + scheduling decision.
#
# Requires: GROQ_API_KEY in stt-service/.env and orchestrator/.env, .NET 8 at ~/.dotnet,
# the repo .venv. Generates the test audio with macOS `say` if missing.
set -euo pipefail
cd "$(dirname "$0")/.."
ROOT="$(pwd)"
VENV="$ROOT/.venv/bin"
export PATH="$HOME/.dotnet:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

WORK=/tmp/mahdar_demo
CARDS="$WORK/cards"
WAV="$WORK/ar_input.wav"
rm -rf "$WORK"; mkdir -p "$CARDS" "$WORK/transcripts"

STT_PORT=8799 ORCH_PORT=8798 BOT_PORT=8797
MID="mtg-demo-$(date +%H%M%S)"
PIDS=()
cleanup() {
  echo; echo "--- cleanup ---"
  for p in "${PIDS[@]:-}"; do kill "$p" 2>/dev/null || true; done
  for port in $STT_PORT $ORCH_PORT $BOT_PORT; do lsof -ti tcp:$port 2>/dev/null | xargs -r kill 2>/dev/null || true; done
}
trap cleanup EXIT

wait_health() { # url label
  for _ in $(seq 1 90); do curl -fsS "$1" >/dev/null 2>&1 && { echo "  up: $2"; return 0; }; sleep 1; done
  echo "  FAILED to reach $2 ($1)"; return 1
}

echo "=== 0. test audio (Arabic + English code-switching) ==="
if [ ! -f "$WAV" ]; then
  say -v Majed "اتفقنا اننا ننقل الـ API Gateway إلى production نهاية الأسبوع، سارة هتجهز الـ rollback plan، ونتقابل الثلاثاء الجاي الساعة ٣ العصر للمتابعة" -o "$WORK/ar.aiff"
  afconvert -f WAVE -d LEI16@16000 -c 1 "$WORK/ar.aiff" "$WAV"
fi
echo "  $WAV"

echo "=== 1. start STT service (Groq Whisper) ==="
( cd stt-service && DATA_DIR="$WORK/transcripts" "$VENV/uvicorn" app.main:app --host 127.0.0.1 --port $STT_PORT --log-level warning ) &
PIDS+=($!)
wait_health "http://127.0.0.1:$STT_PORT/health" "stt"

echo "=== 2. start orchestrator (Groq LLM + FakeGraph) ==="
( cd orchestrator && CARD_OUT_DIR="$CARDS" STT_BASE_URL="http://127.0.0.1:$STT_PORT" \
    "$VENV/uvicorn" app.main:app --host 127.0.0.1 --port $ORCH_PORT --log-level warning ) &
PIDS+=($!)
wait_health "http://127.0.0.1:$ORCH_PORT/health" "orchestrator"

echo "=== 3. start media-bot Host (fake source) ==="
dotnet build media-bot/src/MediaBot.Host/MediaBot.Host.csproj -c Release -v q >/dev/null
( CALL_SOURCE=fake FAKE_WAV_PATH="$WAV" \
  STT_WS_BASE_URL="ws://127.0.0.1:$STT_PORT" ORCHESTRATOR_BASE_URL="http://127.0.0.1:$ORCH_PORT" \
  DUMP_AUDIO=true DUMP_DIR="$WORK/dumps" ASPNETCORE_URLS="http://127.0.0.1:$BOT_PORT" \
  dotnet run --project media-bot/src/MediaBot.Host/MediaBot.Host.csproj -c Release --no-build ) &
PIDS+=($!)
wait_health "http://127.0.0.1:$BOT_PORT/health" "media-bot"

echo "=== 4. trigger join (meeting $MID) ==="
"$VENV/python" scripts/join.py "https://teams.example/meet/demo" --meeting-id "$MID" --bot "http://127.0.0.1:$BOT_PORT"

echo "=== 5. wait for the MoM card (orchestrator -> FakeGraph) ==="
for _ in $(seq 1 90); do ls "$CARDS"/card_*.json >/dev/null 2>&1 && break; sleep 1; done

echo; echo "########## RESULT ##########"
echo "--- transcript (Groq Whisper) ---"
curl -fsS "http://127.0.0.1:$STT_PORT/transcript/$MID" | "$VENV/python" -m json.tool --no-ensure-ascii 2>/dev/null \
  || curl -fsS "http://127.0.0.1:$STT_PORT/transcript/$MID"
echo; echo "--- created calendar events ---"
cat "$CARDS"/event_*.json 2>/dev/null || echo "(none auto-created)"
echo; echo "--- Arabic MoM adaptive card ---"
cat "$CARDS"/card_*.json 2>/dev/null || echo "(no card produced)"
echo; echo "############################"
