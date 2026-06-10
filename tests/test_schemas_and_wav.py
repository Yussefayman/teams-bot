"""Pytest wrappers around the standalone verification scripts so `pytest` covers them.

Run from the repo root:  .venv/bin/pytest
"""
import importlib.util
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
TOOLS = ROOT / "tools"


def _load(name: str):
    spec = importlib.util.spec_from_file_location(name, TOOLS / f"{name}.py")
    mod = importlib.util.module_from_spec(spec)
    sys.modules[name] = mod
    spec.loader.exec_module(mod)
    return mod


def test_json_schemas_match_fixtures():
    assert _load("validate_schemas").main() == 0


def test_wav_oracle_self_test():
    assert _load("wav_selftest").main() == 0
