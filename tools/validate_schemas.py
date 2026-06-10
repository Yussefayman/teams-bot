#!/usr/bin/env python3
"""Validate the shared JSON schemas against their example fixtures.

Valid fixtures (shared/examples/*.valid.json) MUST pass their schema.
Invalid fixtures (shared/examples/invalid/<schema-prefix>.*.json) MUST fail.

Exit code is non-zero if any expectation is violated. Runnable anywhere; no Azure.
"""
import json
import sys
from pathlib import Path

from jsonschema import Draft202012Validator
from jsonschema import FormatChecker

ROOT = Path(__file__).resolve().parents[1]
SCHEMA_DIR = ROOT / "shared" / "schemas"
EX_DIR = ROOT / "shared" / "examples"

# fixture filename prefix -> schema file
SCHEMAS = {
    "transcript_segment": "transcript_segment.schema.json",
    "call_event": "call_event.schema.json",
    "mom": "mom.schema.json",
}


def load(path: Path):
    return json.loads(path.read_text(encoding="utf-8"))


def validator_for(prefix: str) -> Draft202012Validator:
    schema = load(SCHEMA_DIR / SCHEMAS[prefix])
    Draft202012Validator.check_schema(schema)
    return Draft202012Validator(schema, format_checker=FormatChecker())


def prefix_of(path: Path) -> str:
    name = path.name
    for p in SCHEMAS:
        if name.startswith(p):
            return p
    raise SystemExit(f"fixture {name} does not match any schema prefix")


def main() -> int:
    failures = []
    checked = 0

    for fx in sorted(EX_DIR.glob("*.valid.json")):
        v = validator_for(prefix_of(fx))
        errs = sorted(v.iter_errors(load(fx)), key=lambda e: e.path)
        checked += 1
        if errs:
            failures.append(f"[VALID FIXTURE FAILED] {fx.name}: {errs[0].message}")
        else:
            print(f"  ok    valid   {fx.name}")

    for fx in sorted((EX_DIR / "invalid").glob("*.json")):
        v = validator_for(prefix_of(fx))
        errs = list(v.iter_errors(load(fx)))
        checked += 1
        if not errs:
            failures.append(f"[INVALID FIXTURE PASSED unexpectedly] {fx.name}")
        else:
            print(f"  ok    reject  {fx.name}  -> {errs[0].message[:60]}...")

    print(f"\n{checked} fixtures checked, {len(failures)} unexpected result(s).")
    for f in failures:
        print("  FAIL", f)
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
