#!/usr/bin/env bash
# orchestrator deploy/run (Components C + D). Single command, env-driven.
#   dev (Mac, Groq):  LLM_PROVIDER=groq GROQ_API_KEY=... GRAPH_ENABLED=false ./deploy.sh
#   prod:             LLM_PROVIDER=azure-openai GRAPH_ENABLED=true (Azure creds) ./deploy.sh
set -euo pipefail
cd "$(dirname "$0")"

PY="${PYTHON:-../.venv/bin/python}"
PORT="${ORCHESTRATOR_PORT:-8798}"

if [ "${INSTALL:-0}" = "1" ]; then
  echo "installing deps..."
  "$PY" -m pip install -r requirements.txt
fi

echo "starting orchestrator on :$PORT (llm=${LLM_PROVIDER:-groq}, graph=${GRAPH_ENABLED:-false})"
exec "$PY" -m uvicorn app.main:app --host "${HOST:-0.0.0.0}" --port "$PORT"
