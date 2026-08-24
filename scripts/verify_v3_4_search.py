#!/usr/bin/env python3
"""V3.4 PostgreSQL-first search scale and async projection gate."""

from __future__ import annotations

import json
import subprocess
import sys
import time
import urllib.parse
import urllib.request
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
API = "http://localhost:8080"


def run(*args: str) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        args,
        cwd=ROOT,
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


def json_get(path: str) -> dict:
    with urllib.request.urlopen(f"{API}{path}", timeout=15) as response:  # noqa: S310 - fixed local gate
        assert response.status == 200, (path, response.status)
        return json.load(response)


def main() -> None:
    benchmark = json.loads(run(
        sys.executable,
        str(ROOT / "scripts" / "benchmark_v3_4_search.py"),
        "--rows", "100000", "--runs", "5",
    ).stdout)
    assert benchmark["status"] == "PASS", benchmark
    assert benchmark["isolatedBenchmarkRows"] == 100_000, benchmark
    assert max(value["executionMsP95"] for value in benchmark["queries"].values()) <= 150, benchmark
    assert "not justified" in benchmark["decision"], benchmark

    structure = json.loads(psql("""
        SELECT json_build_object(
          'table', to_regclass('published_data_events') IS NOT NULL,
          'projector', to_regprocedure('process_catalog_search_events(integer)') IS NOT NULL,
          'materializedView', EXISTS (
              SELECT 1 FROM pg_matviews WHERE schemaname='public' AND matviewname='current_searchable_trims'),
          'constraints', (
              SELECT count(*) FROM pg_constraint
              WHERE conrelid='published_data_events'::regclass AND contype='c'),
          'queueIndexes', (
              SELECT count(*) FROM pg_indexes
              WHERE tablename='published_data_events'),
          'searchIndexes', (
              SELECT count(*) FROM pg_indexes
              WHERE tablename='current_searchable_trims'),
          'searchableRows', (SELECT count(*) FROM current_searchable_trims),
          'catalogSeedEvents', (
              SELECT count(*) FROM published_data_events
              WHERE event_type='CatalogSearchSync.CatalogSeedPublished'),
          'energyEvents', (
              SELECT count(*) FROM published_data_events
              WHERE event_type='CatalogSearchSync.EnergyProfilesPublished')
        )::text;
    """))
    assert structure["table"] and structure["projector"] and structure["materializedView"], structure
    assert structure["constraints"] == 2, structure
    assert structure["queueIndexes"] >= 4, structure  # primary key + three routing/audit indexes
    assert structure["searchIndexes"] >= 7, structure
    assert structure["searchableRows"] >= 3, structure
    assert structure["catalogSeedEvents"] >= 1 and structure["energyEvents"] >= 1, structure

    event_id = psql("""
        INSERT INTO published_data_events
            (id,event_type,aggregate_type,aggregate_id,payload_json,status,attempts,
             occurred_at,available_at,processing_started_at,processed_at,last_error,
             correlation_id,created_at,updated_at)
        VALUES (gen_random_uuid(),'CatalogSearchSync.GateProbe','SearchProjection',NULL,
                '{"gate":"V3.4"}'::jsonb,'Pending',0,CURRENT_TIMESTAMP,CURRENT_TIMESTAMP,
                NULL,NULL,NULL,'v3.4-gate',CURRENT_TIMESTAMP,CURRENT_TIMESTAMP)
        RETURNING id;
    """).splitlines()[0]

    deadline = time.monotonic() + 20
    event = ""
    while time.monotonic() < deadline:
        event = psql(f"""
            SELECT status || '|' || attempts || '|' ||
                   COALESCE(round(extract(epoch FROM (processed_at-created_at))*1000)::bigint, -1)
            FROM published_data_events WHERE id='{event_id}'::uuid;
        """)
        if event.startswith("Completed|"):
            break
        time.sleep(0.2)
    status, attempts, latency_ms = event.split("|")
    assert status == "Completed" and int(attempts) == 1, event
    assert 0 <= int(latency_ms) <= 10_000, event
    assert int(psql("SELECT count(*) FROM published_data_events WHERE status IN ('Pending','Processing','Failed');")) == 0

    typo = json_get("/api/v1/cars?" + urllib.parse.urlencode({"q": "toytoa yaris", "pageSize": 5}))
    assert typo["pagination"]["totalItems"] >= 1, typo
    assert typo["data"][0]["brandName"] == "Toyota" and typo["data"][0]["modelName"] == "Yaris Cross", typo
    exact = json_get("/api/v1/cars?" + urllib.parse.urlencode({"q": "ex5", "pageSize": 5}))
    assert exact["data"][0]["modelName"] == "EX5", exact

    services = set(run("docker", "compose", "config", "--services").stdout.split())
    assert not ({"typesense", "meilisearch"} & services), services

    publisher_sources = [
        ROOT / "workers" / "ingestion" / "src" / "ingestion" / "publisher.py",
        ROOT / "workers" / "ingestion" / "src" / "ingestion" / "market_scope.py",
        ROOT / "workers" / "ingestion" / "src" / "ingestion" / "energy_seed.py",
        ROOT / "workers" / "ingestion" / "src" / "ingestion" / "change_detection.py",
        ROOT / "apps" / "api" / "src" / "Api" / "Features" / "Admin" / "AdminCatalogService.cs",
        ROOT / "apps" / "api" / "src" / "Api" / "Features" / "Admin" / "AdminReviewService.cs",
    ]
    assert all("CatalogSearchSync" in path.read_text(encoding="utf-8")
               or "enqueue_catalog_search_sync" in path.read_text(encoding="utf-8")
               for path in publisher_sources)
    assert all("refresh_current_searchable_trims()" not in path.read_text(encoding="utf-8")
               for path in publisher_sources)

    print(json.dumps({
        "gate": "V3.4",
        "status": "PASS",
        "postgresBenchmarkRows": benchmark["isolatedBenchmarkRows"],
        "queryP95Milliseconds": {
            name: value["executionMsP95"] for name, value in benchmark["queries"].items()
        },
        "searchableRows": structure["searchableRows"],
        "asyncProjectionEventLatencyMilliseconds": int(latency_ms),
        "asyncProjectionAttempts": int(attempts),
        "externalSearchEngine": "not added: measured PostgreSQL performance is within gate",
    }, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
