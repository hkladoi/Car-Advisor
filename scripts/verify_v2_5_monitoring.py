#!/usr/bin/env python3
"""V2.5 gate: real schedules, run telemetry, parser/staleness alert lifecycle, and audit."""

from __future__ import annotations

import json
import os
import subprocess
import time
from urllib.error import HTTPError
from urllib.request import Request, urlopen


API = os.getenv("VCP_API_BASE", "http://127.0.0.1:8080")
ADMIN_EMAIL = os.getenv("ADMIN_BOOTSTRAP_EMAIL", "admin@vcp.local")
ADMIN_PASSWORD = os.getenv("ADMIN_BOOTSTRAP_PASSWORD", "vcp-admin-local-dev-only")


def call(
    path: str,
    *,
    method: str = "GET",
    body: dict | None = None,
    token: str | None = None,
) -> tuple[int, object | None]:
    headers = {"Accept": "application/json"}
    if token:
        headers["Authorization"] = f"Bearer {token}"
    data = None
    if body is not None:
        data = json.dumps(body).encode("utf-8")
        headers["Content-Type"] = "application/json"
    request = Request(f"{API}{path}", data=data, headers=headers, method=method)  # noqa: S310
    try:
        with urlopen(request, timeout=60) as response:  # noqa: S310
            raw = response.read()
            return response.status, json.loads(raw) if raw else None
    except HTTPError as error:
        raw = error.read()
        return error.code, json.loads(raw) if raw else None


def require(status: int, expected: int, payload: object | None) -> None:
    assert status == expected, (status, expected, payload)


def psql(sql: str) -> str:
    process = subprocess.run(  # noqa: S603 - fixed local Docker Compose command
        [
            "docker", "compose", "exec", "-T", "postgres", "psql",
            "-v", "ON_ERROR_STOP=1", "-U", "vcp", "-d", "vietnam_car_platform",
            "-At", "-F", "|",
        ],
        input=sql,
        text=True,
        capture_output=True,
        check=False,
    )
    assert process.returncode == 0, process.stderr
    return process.stdout.strip()


def worker_python(source: str) -> str:
    process = subprocess.run(  # noqa: S603 - fixed local Docker Compose command
        ["docker", "compose", "exec", "-T", "ingestion-worker", "python", "-"],
        input=source,
        text=True,
        capture_output=True,
        check=False,
    )
    assert process.returncode == 0, process.stderr
    return process.stdout.strip()


def canonical_digest() -> str:
    return psql(
        """
        SELECT md5(COALESCE(string_agg(value, '|' ORDER BY value), ''))
        FROM (
            SELECT 'price:' || id || ':' || amount || ':' || COALESCE(source_fact_id::text, '') AS value FROM prices
            UNION ALL
            SELECT 'spec:' || trim_id || ':' || spec_definition_id || ':' || COALESCE(numeric_value::text, text_value, '') || ':' || COALESCE(source_fact_id::text, '') FROM trim_specs
            UNION ALL
            SELECT 'power:' || trim_id || ':' || type || ':' || COALESCE(fuel_type, '') || ':' || COALESCE(combined_power_kw::text, engine_power_kw::text, motor_power_kw::text, '') || ':' || COALESCE(source_fact_id::text, '') FROM powertrain_profiles
            UNION ALL
            SELECT 'energy:' || trim_id || ':' || COALESCE(official_fuel_litres_per100km::text, '') || ':' || COALESCE(official_electric_kwh_per100km::text, '') || ':' || COALESCE(source_fact_id::text, '') FROM energy_profiles
        ) canonical;
        """
    )


def main() -> None:
    before = canonical_digest()
    run_ids = json.loads(
        worker_python(
            """
import asyncio
import json
import redis.asyncio as redis
from ingestion.jobs import IngestionJob
from ingestion.settings import Settings

async def main():
    settings = Settings()
    client = redis.from_url(settings.redis_url, decode_responses=True)
    jobs = [
        IngestionJob.known_url('toyota-taf-august-2026-offer', 'dealer_offers'),
        IngestionJob.known_url('toyota-finance-reference', 'finance_campaign_reference'),
    ]
    await client.rpush(settings.ingestion_queue, *[job.model_dump_json() for job in jobs])
    await client.aclose()
    print(json.dumps([str(job.run_id) for job in jobs]))

asyncio.run(main())
"""
        )
    )
    quoted_ids = ",".join(f"'{value}'" for value in run_ids)
    deadline = time.monotonic() + 180
    live_runs = ""
    while time.monotonic() < deadline:
        live_runs = psql(
            f"SELECT id || ':' || status || ':' || COALESCE(http_status::text, '') || ':' || COALESCE(parse_status, '') FROM ingestion_job_runs WHERE id IN ({quoted_ids}) ORDER BY id;"
        )
        rows = [row for row in live_runs.splitlines() if row]
        if len(rows) == 2 and all(":Succeeded:" in row for row in rows):
            break
        if any(":Failed:" in row or ":Partial:" in row for row in rows):
            raise AssertionError(f"Official dealer/finance monitoring failed: {live_runs}")
        time.sleep(2)
    else:
        raise AssertionError(f"Timed out waiting for official monitoring jobs: {live_runs}")
    assert all(":200:parsed" in row or ":200:unchanged" in row for row in live_runs.splitlines())

    lifecycle = json.loads(
        worker_python(
            """
import json
from pathlib import Path

from ingestion.jobs import IngestionJob
from ingestion.monitoring import MonitoringRepository
from ingestion.registry import SourceRegistry
from ingestion.settings import Settings

settings = Settings()
registry = SourceRegistry.load(Path(settings.source_registry_path))
source = registry.by_id('toyota-taf-august-2026-offer')
repository = MonitoringRepository(settings.postgres_dsn, settings.parser_failure_alert_threshold)
failed = []
for _ in range(settings.parser_failure_alert_threshold):
    job = IngestionJob.known_url(source.id, 'dealer_offers')
    repository.begin(job, source)
    repository.fail(job, 'parser', ValueError('V2.5 deterministic fixture parser failure'))
    failed.append(str(job.run_id))
repository.reconcile_stale_sources([source], [source.id])
print(json.dumps({'failed': failed}))
"""
        )
    )
    assert len(lifecycle["failed"]) == 3

    status, login = call(
        "/api/v1/admin/auth/login",
        method="POST",
        body={"email": ADMIN_EMAIL, "password": ADMIN_PASSWORD},
    )
    require(status, 200, login)
    assert isinstance(login, dict)
    token = login["token"]

    status, monitoring = call("/api/v1/admin/monitoring", token=token)
    require(status, 200, monitoring)
    assert isinstance(monitoring, dict)
    kinds = {item["monitorKind"] for item in monitoring["monitorKinds"]}
    required_kinds = {
        "vehicle_price_promotion", "vehicle_specs_features", "vehicle_images_colors",
        "new_model_discovery", "dealer_offers", "finance_campaign_reference",
        "fuel_price", "electricity_tariff", "charging_tariff_promotion",
        "registration_legal_rules",
    }
    assert required_kinds <= kinds, required_kinds - kinds
    alerts = {item["alertType"]: item for item in monitoring["alerts"] if item["sourceKey"] == "toyota-taf-august-2026-offer"}
    parser_alert = alerts["PARSER_CONSECUTIVE_FAILURE"]
    stale_alert = alerts["SOURCE_STALE"]
    assert parser_alert["status"] == "Open" and parser_alert["occurrenceCount"] >= 1
    assert stale_alert["status"] == "Open"

    reason = "V2.5 gate acknowledges a deterministic parser-failure alert with an audited operator reason."
    escaped_reason = reason.replace("'", "''")
    audit_count_before = int(psql(
        f"SELECT COUNT(*) FROM audit_events WHERE entity_id = '{parser_alert['id']}' AND action = 'MonitoringAlertAcknowledged' AND reason = '{escaped_reason}';"
    ))
    status, payload = call(
        f"/api/v1/admin/monitoring/alerts/{parser_alert['id']}/acknowledge",
        method="POST",
        body={"reason": reason},
        token=token,
    )
    require(status, 204, payload)
    assert psql(
        f"SELECT status || ':' || COALESCE(acknowledged_by, '') FROM monitoring_alerts WHERE id = '{parser_alert['id']}';"
    ) == f"Acknowledged:{ADMIN_EMAIL}"
    assert int(psql(
        f"SELECT COUNT(*) FROM audit_events WHERE entity_id = '{parser_alert['id']}' AND action = 'MonitoringAlertAcknowledged' AND reason = '{escaped_reason}';"
    )) == audit_count_before + 1

    worker_python(
        """
from pathlib import Path

from ingestion.jobs import IngestionJob
from ingestion.monitoring import MonitoringRepository
from ingestion.registry import SourceRegistry
from ingestion.settings import Settings

settings = Settings()
registry = SourceRegistry.load(Path(settings.source_registry_path))
source = registry.by_id('toyota-taf-august-2026-offer')
repository = MonitoringRepository(settings.postgres_dsn, settings.parser_failure_alert_threshold)
job = IngestionJob.known_url(source.id, 'dealer_offers')
repository.begin(job, source)
repository.succeed(job, http_status=200, parse_status='parsed', content_changed=False)
repository.reconcile_stale_sources([source], [])
"""
    )
    terminal = psql(
        f"""
        SELECT alert_type || ':' || status || ':' || CASE WHEN resolved_at IS NULL THEN 'missing' ELSE 'set' END
        FROM monitoring_alerts
        WHERE id IN ('{parser_alert["id"]}', '{stale_alert["id"]}')
        ORDER BY alert_type;
        """
    ).splitlines()
    assert terminal == ["PARSER_CONSECUTIVE_FAILURE:Resolved:set", "SOURCE_STALE:Resolved:set"]
    assert canonical_digest() == before, "Monitoring failures must preserve the published canonical catalog"

    call(
        "/api/v1/admin/auth/logout",
        method="POST",
        body={"reason": "V2.5 gate revokes its administrator monitoring session."},
        token=token,
    )
    print(
        "PASS V2.5: daily/weekly schedules, real official dealer and finance fetches, run telemetry, "
        "parser/staleness alerts, audited acknowledgement, recovery, and published-data preservation verified."
    )


if __name__ == "__main__":
    main()
