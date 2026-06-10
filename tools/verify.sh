#!/usr/bin/env bash
# Runs every check that does NOT need Azure/Windows: schema validation + WAV oracle.
# Safe to run on any machine with python3.
set -euo pipefail
cd "$(dirname "$0")/.."

echo "==> ensuring jsonschema is available"
python3 -c "import jsonschema" 2>/dev/null || python3 -m pip install --quiet jsonschema

echo "==> validating shared JSON schemas against fixtures"
python3 tools/validate_schemas.py

echo
echo "==> WAV-format oracle self-test (M1 audio-dump contract)"
python3 tools/wav_selftest.py

echo
echo "ALL LOCAL CHECKS PASSED"
