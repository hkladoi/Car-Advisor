from __future__ import annotations

import json
from dataclasses import asdict
from datetime import datetime
from pathlib import Path

from ingestion.fetcher import Snapshot


def write_snapshot_manifest(path: Path, snapshots: list[Snapshot]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    payload = {
        "schema_version": "v1.2",
        "generated_at": datetime.now().astimezone().isoformat(),
        "snapshots": [_json_safe(asdict(snapshot)) for snapshot in snapshots],
    }
    path.write_text(json.dumps(payload, ensure_ascii=False, indent=2), encoding="utf-8")


def read_snapshot_manifest(path: Path) -> dict[str, Snapshot]:
    payload = json.loads(path.read_text(encoding="utf-8"))
    snapshots: dict[str, Snapshot] = {}
    for item in payload["snapshots"]:
        item["fetched_at"] = datetime.fromisoformat(item["fetched_at"])
        snapshot = Snapshot(**item)
        snapshots[snapshot.source_id] = snapshot
    return snapshots


def _json_safe(value: object) -> object:
    if isinstance(value, datetime):
        return value.isoformat()
    if isinstance(value, dict):
        return {key: _json_safe(item) for key, item in value.items()}
    if isinstance(value, list):
        return [_json_safe(item) for item in value]
    return value
