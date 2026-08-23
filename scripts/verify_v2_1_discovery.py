from __future__ import annotations

import json
import sys
from pathlib import Path
from urllib.parse import urlsplit


def main() -> None:
    if len(sys.argv) != 2:
        raise SystemExit("usage: verify_v2_1_discovery.py <result.json>")
    payload = json.loads(Path(sys.argv[1]).read_text(encoding="utf-8"))
    assert payload["strategy"] == "known_url_first"
    assert payload["queries"] == []
    assert payload["charged_requests"] == 0
    assert payload["candidates"], "known official URL smoke must return candidates"
    forbidden = {"snippet", "description", "extra_snippets"}
    for candidate in payload["candidates"]:
        assert forbidden.isdisjoint(candidate), "search copy must not enter discovery candidates"
        parsed = urlsplit(candidate["url"])
        assert parsed.scheme == "https" and parsed.hostname
        assert candidate["provider"] == "registry"
    print("V2.1 discovery gate: PASS")


if __name__ == "__main__":
    main()
