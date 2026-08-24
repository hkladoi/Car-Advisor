#!/usr/bin/env python3
"""Consolidated V3 FINAL acceptance gate."""

from __future__ import annotations

import json
import subprocess
import sys
from pathlib import Path
from typing import Any


ROOT = Path(__file__).resolve().parents[1]


def run(*args: str, timeout: int = 900) -> str:
    try:
        return subprocess.run(
            args,
            cwd=ROOT,
            check=True,
            capture_output=True,
            text=True,
            encoding="utf-8",
            timeout=timeout,
        ).stdout.strip()
    except subprocess.CalledProcessError as error:
        detail = (error.stderr or error.stdout or "gate command failed").strip()
        raise RuntimeError(detail) from error


def json_gate(script: str, timeout: int = 900) -> dict[str, Any]:
    output = run(sys.executable, str(ROOT / "scripts" / script), timeout=timeout)
    payload = json.loads(output)
    assert payload.get("status") == "PASS", (script, payload)
    return payload


def psql(sql: str) -> str:
    return run(
        "docker", "compose", "exec", "-T", "postgres", "psql",
        "-U", "vcp", "-d", "vietnam_car_platform", "-v", "ON_ERROR_STOP=1",
        "-A", "-t", "-c", sql,
    )


def main() -> None:
    services = [
        json.loads(line)
        for line in run("docker", "compose", "ps", "--format", "json").splitlines()
        if line.strip()
    ]
    required = {
        "web", "api", "postgres", "redis", "minio",
        "ingestion-worker", "ingestion-scheduler",
    }
    by_service = {item["Service"]: item for item in services}
    assert required <= set(by_service), by_service.keys()
    assert all(
        by_service[name]["State"] == "running" and by_service[name].get("Health") == "healthy"
        for name in required
    ), by_service

    recommendation = json_gate("verify_v3_1_recommendation.py")
    assert recommendation["deterministicRepeat"] is True
    assert recommendation["methodology"] == "v3.1-deterministic-1"
    assert recommendation["considered"] == (
        recommendation["ranked"]
        + recommendation["dataWithheld"]
        + recommendation["hardFilterExcluded"]
    )

    privacy = json_gate("verify_v3_2_accounts.py")
    assert privacy["anonymousRejected"] and privacy["consentRequired"]
    assert privacy["exportComplete"]
    assert all(value == 0 for value in privacy["rowsAfterDelete"].values())

    search = json_gate("verify_v3_4_search.py", timeout=1200)
    assert search["postgresBenchmarkRows"] == 100_000
    assert max(search["queryP95Milliseconds"].values()) <= 150
    assert search["asyncProjectionEventLatencyMilliseconds"] <= 10_000

    run(sys.executable, str(ROOT / "scripts" / "backup_restore_test.py"), timeout=1200)
    restore = json.loads((ROOT / "output" / "restore-drill" / "v1-final-restore-report.json").read_text(encoding="utf-8"))
    assert restore["status"] == "PASS" and restore["migrationCount"] >= 1
    assert restore["objectCount"] == restore["restoredCounts"]["source_snapshots"]

    load = json_gate("load_v3_final.py", timeout=1200)
    assert load["target"]["totalRequests"] == 1_200
    assert load["httpErrors"] == 0 and load["invalidPayloads"] == 0
    assert load["partnerCredentialRevoked"] is True

    residual = json.loads(psql("""
        SELECT json_build_object(
          'latestMigration', EXISTS (
            SELECT 1 FROM \"__EFMigrationsHistory\"
            WHERE migration_id='20260824032619_AddV35PartnerApi'),
          'privacyGateAccounts', (
            SELECT count(*) FROM user_accounts WHERE email LIKE 'v32-gate-%@example.invalid'),
          'activeFinalLoadKeys', (
            SELECT count(*) FROM partner_api_keys
            WHERE name LIKE 'V3 FINAL load gate %' AND revoked_at IS NULL),
          'unfinishedSearchEvents', (
            SELECT count(*) FROM published_data_events
            WHERE status IN ('Pending','Processing','Failed')),
          'temporaryDatabases', (
            SELECT count(*) FROM pg_database
            WHERE datname LIKE 'vcp_v34_bench_%'
               OR datname LIKE 'vcp_v35_%'
               OR datname LIKE 'vcp_restore_%')
        )::text;
    """))
    assert residual == {
        "latestMigration": True,
        "privacyGateAccounts": 0,
        "activeFinalLoadKeys": 0,
        "unfinishedSearchEvents": 0,
        "temporaryDatabases": 0,
    }, residual

    final_services = [
        json.loads(line)
        for line in run("docker", "compose", "ps", "--format", "json").splitlines()
        if line.strip()
    ]
    final_by_service = {item["Service"]: item for item in final_services}
    assert required <= set(final_by_service), final_by_service.keys()
    assert all(
        final_by_service[name]["State"] == "running"
        and final_by_service[name].get("Health") == "healthy"
        for name in required
    ), final_by_service

    print(json.dumps({
        "gate": "V3 FINAL",
        "status": "PASS",
        "recommendation": {
            "methodology": recommendation["methodology"],
            "deterministicRepeat": recommendation["deterministicRepeat"],
            "considered": recommendation["considered"],
            "ranked": recommendation["ranked"],
            "dataWithheld": recommendation["dataWithheld"],
        },
        "privacy": {
            "consentRequired": privacy["consentRequired"],
            "exportComplete": privacy["exportComplete"],
            "rowsAfterDelete": privacy["rowsAfterDelete"],
        },
        "search": {
            "benchmarkRows": search["postgresBenchmarkRows"],
            "queryP95Milliseconds": search["queryP95Milliseconds"],
            "asyncProjectionEventLatencyMilliseconds": search["asyncProjectionEventLatencyMilliseconds"],
        },
        "apiLoad": {
            "target": load["target"],
            "achievedRequestsPerSecond": load["achievedRequestsPerSecond"],
            "routes": load["routes"],
            "errors": 0,
        },
        "recovery": {
            "migrationCount": restore["migrationCount"],
            "objectCount": restore["objectCount"],
            "totalRtoSeconds": restore["totalRtoSeconds"],
        },
        "residualGateState": residual,
        "servicesHealthy": sorted(required),
    }, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
