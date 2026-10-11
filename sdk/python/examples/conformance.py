"""Conformance program for stand-in/conformance.mjs (see spec/conformance.json).

Reads one request as JSON on stdin, translates it with translate_texts using SUPERTEXT_API_URL
and SUPERTEXT_API_KEY, and prints {"translations": …} or {"error": {"code", "status"}}.

    node stand-in/conformance.mjs -- python3 sdk/python/examples/conformance.py
"""

from __future__ import annotations

import json
import os
import sys
from pathlib import Path

# Work from a source checkout too, without installing the package.
sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "src"))

import supertext


def main() -> int:
    request = json.load(sys.stdin.buffer)
    client = supertext.Client(
        os.environ.get("SUPERTEXT_API_KEY", ""),
        base_url=os.environ.get("SUPERTEXT_API_URL") or supertext.LIVE_BASE_URL,
        poll_interval=0.1,
    )
    segments = {s["key"]: supertext.Segment(s["text"], bool(s.get("html", False))) for s in request["segments"]}
    try:
        translations = client.translate_texts(
            segments,
            request["targetLang"],
            request.get("sourceLang"),
            request.get("politeness") or "default",
        )
        output: dict[str, object] = {"translations": translations}
    except supertext.SupertextError as error:
        output = {"error": {"code": error.code, "status": error.status}}
    json.dump(output, sys.stdout)
    sys.stdout.write("\n")
    return 0


if __name__ == "__main__":
    sys.exit(main())
