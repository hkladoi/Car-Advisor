#!/usr/bin/env python3
"""Aggregate V2 FINAL GATE for the running production Compose stack."""

from __future__ import annotations

import json
import subprocess
import sys
import time
from pathlib import Path
from typing import Any
from urllib.request import Request, urlopen


ROOT = Path(__file__).resolve().parents[1]
API = "http://127.0.0.1:8080"
WEB = "http://127.0.0.1:3000"


def command(*args: str) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        args,
        cwd=ROOT,
        check=True,
        capture_output=True,
        text=True,
        encoding="utf-8",
    )


def psql(query: str) -> str:
    return command(
        "docker", "compose", "exec", "-T", "postgres",
        "psql", "-U", "vcp", "-d", "vietnam_car_platform",
        "-v", "ON_ERROR_STOP=1", "-Atc", query,
    ).stdout.strip()


def request(path: str, *, base: str = API) -> tuple[int, Any]:
    with urlopen(Request(f"{base}{path}", headers={"Accept": "application/json"}), timeout=60) as response:  # noqa: S310 - fixed local gate URLs
        raw = response.read()
        content_type = response.headers.get("Content-Type", "")
        return response.status, json.loads(raw) if raw and "json" in content_type else raw.decode("utf-8")


def require(condition: bool, message: str) -> None:
    if not condition:
        raise AssertionError(message)


def supported_recurring_sources() -> dict[str, str]:
    registry = json.loads((ROOT / "data/source-registry.v1.json").read_text(encoding="utf-8"))
    monitor_by_category = {
        "vehicle": "vehicle_price_promotion",
        "dealer-offer": "dealer_offers",
        "finance-campaign": "finance_campaign_reference",
    }
    return {
        source["id"]: monitor_by_category[source["category"]]
        for source in registry["sources"]
        if source.get("automated_fetch", True) and source["category"] in monitor_by_category
    }


def reconcile_staleness() -> str:
    script = """
import asyncio
import redis.asyncio as redis
from ingestion.jobs import IngestionJob
from ingestion.settings import Settings

async def main():
    settings = Settings()
    client = redis.from_url(settings.redis_url, decode_responses=True)
    job = IngestionJob.staleness_check()
    await client.rpush(settings.ingestion_queue, job.model_dump_json())
    await client.aclose()
    print(job.run_id)

asyncio.run(main())
"""
    run_id = command(
        "docker", "compose", "exec", "-T", "ingestion-worker", "python", "-c", script
    ).stdout.strip()
    deadline = time.monotonic() + 60
    latest = ""
    while time.monotonic() < deadline:
        latest = psql(
            f"SELECT status FROM ingestion_job_runs WHERE id='{run_id}'::uuid"
        )
        if latest == "Succeeded":
            return run_id
        if latest in {"Failed", "Partial"}:
            raise AssertionError(f"staleness reconciliation ended as {latest}: {run_id}")
        time.sleep(0.2)
    raise AssertionError(f"staleness reconciliation timed out: {run_id} ({latest})")


def main() -> None:
    services = [json.loads(line) for line in command("docker", "compose", "ps", "--format", "json").stdout.splitlines() if line.strip()]
    by_service = {item["Service"]: item for item in services}
    required_services = {"postgres", "redis", "minio", "api", "web", "ingestion-worker", "ingestion-scheduler"}
    require(required_services <= by_service.keys(), "Compose stack is incomplete")
    require(all(by_service[name]["State"] == "running" and by_service[name]["Health"] == "healthy" for name in required_services), "Compose stack is not healthy")

    # The V2.5 negative-path gate injects bounded parser failures, verifies the
    # canonical catalog digest is unchanged, then performs a real official-page
    # recovery. Running it here makes published-data preservation executable.
    preservation = command(sys.executable, "scripts/verify_v2_5_monitoring.py")
    require(preservation.stdout.startswith("PASS V2.5:"), "parser-failure preservation gate failed")

    expected = supported_recurring_sources()
    require(bool(expected), "source registry has no supported recurring sources")
    succeeded_rows = psql(
        "SELECT monitor_kind||'|'||source_key FROM ingestion_job_runs "
        "WHERE status='Succeeded' AND source_key IS NOT NULL "
        "AND monitor_kind IN ('vehicle_price_promotion','dealer_offers','finance_campaign_reference') "
        "GROUP BY monitor_kind,source_key"
    ).splitlines()
    succeeded = {tuple(row.split("|", 1)) for row in succeeded_rows if "|" in row}
    detected = sorted(source_id for source_id, monitor in expected.items() if (monitor, source_id) in succeeded)
    recurring_coverage = len(detected) / len(expected)
    require(recurring_coverage >= 0.80, f"recurring detection coverage below 80%: {len(detected)}/{len(expected)}")

    unsafe_publications = int(psql(
        "SELECT count(*) FROM publication_versions pv "
        "JOIN data_changes dc ON dc.id=pv.data_change_id "
        "WHERE dc.risk_level IN ('High','Critical') "
        "AND (dc.reviewed_audit_event_id IS NULL OR pv.published_by LIKE 'system:%')"
    ))
    require(unsafe_publications == 0, "a high-risk change was published without human review")

    status, coverage = request("/api/v1/coverage")
    require(status == 200 and isinstance(coverage, dict), "public coverage API failed")
    require(coverage["scopeVersion"] == "v2.8", "reviewed V2.8 scope is not active")
    require(coverage["fullMarketGatePassed"] is True, f"full-market gate blocked: {coverage['gateFailures']}")
    require(coverage["reviewedBrandCount"] == 51 and coverage["brandScopeCount"] == 51, "BrandScope is not fully reviewed")
    require(coverage["resolvedCandidateCount"] == coverage["discoveredCandidateCount"] == 304, "candidate inventory is not closed")
    require(coverage["activeModelCount"] == 255 and coverage["activeTrimCount"] == 49, "published market inventory changed")
    require(coverage["unresolvedDuplicates"] == 0, "unresolved duplicate remains")
    require(float(coverage["coreCompleteness"]) >= 0.95 and float(coverage["freshness"]) == 1.0, "coverage/freshness target failed")
    require(all(gap["reason"].strip() for gap in coverage["candidateGaps"]), "coverage dashboard contains an unexplained gap")
    require(len(coverage["candidateGaps"]) == coverage["documentedBlockedCount"] == 236, "gap ledger is not reproducible")
    require(all(domain["passed"] for domain in coverage["freshnessDomains"]), "a required freshness domain failed")

    status, html = request("/coverage", base=WEB)
    require(status == 200 and isinstance(html, str), "coverage dashboard is unavailable")
    html = html.replace("<!-- -->", "")
    for marker in ("FULL-MARKET GATE", "PASS", "304/304", "255 model", "236 khoảng trống", "Scope hash"):
        require(marker in html, f"coverage dashboard is missing: {marker}")

    require(psql('SELECT count(*) FROM "__EFMigrationsHistory" WHERE migration_id=\'20260823062601_AddV28MarketCoverage\'') == "1", "V2.8 migration is not applied")
    staleness_run_id = reconcile_staleness()
    registry = json.loads((ROOT / "data/source-registry.v1.json").read_text(encoding="utf-8"))
    v3_owned_sources = sorted(
        source["id"]
        for source in registry["sources"]
        if source["category"] == "real-world-consumption"
    )
    v3_source_sql = ",".join(f"'{value}'" for value in v3_owned_sources)
    v2_open_alerts = psql(
        "SELECT count(*) FROM monitoring_alerts "
        "WHERE status='Open' AND severity IN ('High','Critical') "
        + (
            f"AND (source_key IS NULL OR source_key NOT IN ({v3_source_sql}))"
            if v3_source_sql
            else ""
        )
    )
    require(v2_open_alerts == "0", "a V2-owned high/critical monitoring alert remains open")
    deferred_v3_alerts = int(psql(
        "SELECT count(*) FROM monitoring_alerts "
        "WHERE status='Open' AND severity IN ('High','Critical') "
        + (f"AND source_key IN ({v3_source_sql})" if v3_source_sql else "AND FALSE")
    ))

    print(json.dumps({
        "status": "PASS",
        "recurringDetection": {
            "supportedSources": len(expected),
            "detectedWithoutManualSearch": len(detected),
            "coverage": round(recurring_coverage, 4),
        },
        "highRiskAutoPublished": unsafe_publications,
        "market": {
            "brandsReviewed": coverage["reviewedBrandCount"],
            "models": coverage["activeModelCount"],
            "trims": coverage["activeTrimCount"],
            "candidatesResolved": coverage["resolvedCandidateCount"],
            "documentedGaps": coverage["documentedBlockedCount"],
        },
        "parserFailurePreservedPublishedData": True,
        "dashboardVisible": True,
        "stalenessReconciliationRunId": staleness_run_id,
        "deferredV3SourceAlerts": deferred_v3_alerts,
    }, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
