#!/usr/bin/env bash
# stt-service deploy/run (Component B). Single command, env-driven.
#   dev (Mac, Groq):  TRANSCRIBER=groq GROQ_API_KEY=... ./deploy.sh
#   prod (GPU box):   TRANSCRIBER=faster-whisper ./deploy.sh   (after installing GPU deps)
set -euo pipefail
cd "$(dirname "$0")"

PY="${PYTHON:-../.venv/bin/python}"
PORT="${STT_WS_PORT:-8799}"

if [ "${INSTALL:-0}" = "1" ]; then
  echo "installing deps..."
  "$PY" -m pip install -r requirements.txt
fi

echo "starting stt-service on :$PORT (transcriber=${TRANSCRIBER:-groq})"
exec "$PY" -m uvicorn app.main:app --host "${HOST:-0.0.0.0}" --port "$PORT"
