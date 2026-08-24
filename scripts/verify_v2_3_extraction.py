#!/usr/bin/env python3
"""V2.3 live gate for structured extraction and late catalog reconciliation."""

from __future__ import annotations

import json
import subprocess
import time
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
SOURCE_ID = "toyota-yaris-cross"
SOURCE_URL = "https://www.toyota.com.vn/yaris-cross"


def run(*args: str, input_text: str | None = None) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        args,
        cwd=ROOT,
        input=input_text,
        check=True,
        capture_output=True,
        text=True,
        encoding="utf-8",
    )


def psql(sql: str) -> str:
    return run(
        "docker", "compose", "exec", "-T", "postgres", "psql",
        "-U", "vcp", "-d", "vietnam_car_platform", "-v", "ON_ERROR_STOP=1",
        "-A", "-t", "-c", sql,
    ).stdout.strip()


def enqueue_replay() -> str:
    script = f"""
import asyncio
import redis.asyncio as redis
from ingestion.jobs import IngestionJob
from ingestion.settings import Settings

async def main():
    settings = Settings()
    client = redis.from_url(settings.redis_url, decode_responses=True)
    job = IngestionJob.known_url('{SOURCE_ID}', 'vehicle_specs_features')
    await client.rpush(settings.ingestion_queue, job.model_dump_json())
    await client.aclose()
    print(job.run_id)

asyncio.run(main())
"""
    return run(
        "docker", "compose", "exec", "-T", "ingestion-worker", "python", "-c", script
    ).stdout.strip()


def wait_for_run(run_id: str) -> dict[str, object]:
    deadline = time.monotonic() + 180
    latest = ""
    while time.monotonic() < deadline:
        latest = psql(f"""
            SELECT json_build_object(
              'status',status,'httpStatus',http_status,'parseStatus',parse_status,
              'errorStage',error_stage,'errorCode',error_code)::text
            FROM ingestion_job_runs WHERE id='{run_id}'::uuid;
        """)
        if latest:
            payload = json.loads(latest)
            if payload["status"] == "Succeeded":
                assert payload["httpStatus"] == 200, payload
                assert payload["parseStatus"] in {"parsed", "unchanged"}, payload
                return payload
            if payload["status"] in {"Failed", "Partial"}:
                raise AssertionError(payload)
        time.sleep(0.2)
    raise AssertionError(("timed out waiting for structured extraction", run_id, latest))


def resolved_facts() -> list[dict[str, object]]:
    payload = psql(f"""
        SELECT COALESCE(json_agg(row_to_json(evidence) ORDER BY evidence.id), '[]'::json)::text
        FROM (
          SELECT sf.id, sf.entity_id AS "entityId", sf.normalized_value AS value,
                 sf.confidence, sf.extraction_context::jsonb AS context
          FROM source_facts sf
          JOIN source_snapshots ss ON ss.id=sf.snapshot_id
          JOIN sources s ON s.id=ss.source_id
          JOIN trim_specs ts ON ts.trim_id=sf.entity_id
          JOIN spec_definitions sd ON sd.id=ts.spec_definition_id AND sd.code='SEATS'
          WHERE s.url='{SOURCE_URL}' AND sf.entity_type='Trim'
            AND sf.field_path='spec.seats' AND sf.normalized_value='5'
        ) evidence;
    """)
    return json.loads(payload)


def main() -> None:
    first_run = wait_for_run(enqueue_replay())
    first_facts = resolved_facts()
    assert first_facts, "official Toyota extraction did not resolve to a Vietnam trim"
    latest = first_facts[-1]
    context = latest["context"]
    assert latest["confidence"] == "VerifiedOfficial", latest
    assert context["schema_version"] == "v2.3", context
    assert context["extraction_version"] == "structured-extraction/2.3.0", context
    assert context["parser_version"] == "toyota-html/2.2.0", context
    assert context["entity_resolution"]["status"] == "resolved_trim", context

    second_run = wait_for_run(enqueue_replay())
    second_facts = resolved_facts()
    assert [value["id"] for value in second_facts] == [value["id"] for value in first_facts], (
        first_facts,
        second_facts,
    )

    print(json.dumps({
        "gate": "V2.3",
        "status": "PASS",
        "source": SOURCE_ID,
        "fieldPath": "spec.seats",
        "normalizedValue": "5",
        "confidence": latest["confidence"],
        "resolution": context["entity_resolution"]["status"],
        "firstReplay": first_run,
        "idempotentReplay": second_run,
        "resolvedFactCount": len(second_facts),
    }, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
