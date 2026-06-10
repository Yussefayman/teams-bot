#!/usr/bin/env python3
"""Tell the media bot to join a Teams meeting (plan §7 activation path).

    scripts/join.py "<teams join url>" [--meeting-id ID] [--bot URL]

Defaults: bot at $MEDIA_BOT_BASE_URL or http://127.0.0.1:8797; meeting id auto-generated.
"""
import argparse
import datetime as dt
import json
import os
import sys
import urllib.request


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("join_url")
    ap.add_argument("--meeting-id", default=None)
    ap.add_argument("--bot", default=os.environ.get("MEDIA_BOT_BASE_URL", "http://127.0.0.1:8797"))
    args = ap.parse_args()

    meeting_id = args.meeting_id or "mtg-" + dt.datetime.utcnow().strftime("%Y%m%d-%H%M%S")
    body = json.dumps({"joinUrl": args.join_url, "meetingId": meeting_id}).encode()
    req = urllib.request.Request(
        f"{args.bot.rstrip('/')}/api/joinCall",
        data=body, headers={"Content-Type": "application/json"}, method="POST")
    try:
        with urllib.request.urlopen(req, timeout=15) as res:
            print(res.status, res.read().decode())
    except urllib.error.HTTPError as e:
        print("HTTP", e.code, e.read().decode(), file=sys.stderr)
        return 1
    except urllib.error.URLError as e:
        print(f"cannot reach media bot at {args.bot}: {e.reason}", file=sys.stderr)
        return 1
    print(f"requested join as meetingId={meeting_id}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
