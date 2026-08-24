#!/usr/bin/env python3
"""V3.3 trustworthy real-world consumption gate against the live Compose stack."""

from __future__ import annotations

import json
import subprocess
import time
import urllib.request
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
API = "http://localhost:8080"
WEB = "http://localhost:3000"
EEA_SOURCE_ID = "eea-real-world-cars-2023-aggregate"


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
        "-Atc", sql,
    ).stdout.strip()


def json_get(path: str) -> dict:
    with urllib.request.urlopen(f"{API}{path}", timeout=15) as response:  # noqa: S310 - fixed localhost gate
        assert response.status == 200, (path, response.status)
        return json.load(response)


def html_get(path: str) -> str:
    with urllib.request.urlopen(f"{WEB}{path}", timeout=30) as response:  # noqa: S310 - fixed localhost gate
        assert response.status == 200, (path, response.status)
        return response.read().decode("utf-8")


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
    run_id = run(
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
            raise AssertionError(("staleness reconciliation failed", run_id, latest))
        time.sleep(0.2)
    raise AssertionError(("staleness reconciliation timed out", run_id, latest))


def main() -> None:
    evidence = json.loads(psql("""
SELECT json_build_object(
  'rows', (SELECT count(*) FROM real_world_consumption_aggregates),
  'mappedRows', (SELECT count(*) FROM real_world_consumption_aggregates WHERE brand_id IS NOT NULL),
  'referencedSourceFacts', (SELECT count(DISTINCT source_fact_id) FROM real_world_consumption_aggregates),
  'sampleSizeTotal', (SELECT sum(sample_size) FROM real_world_consumption_aggregates),
  'invalidSamples', (SELECT count(*) FROM real_world_consumption_aggregates WHERE sample_size <= 0),
  'registrationYears', (SELECT json_agg(year ORDER BY year) FROM (SELECT DISTINCT vehicle_registration_year AS year FROM real_world_consumption_aggregates) years),
  'snapshots', (SELECT count(*) FROM source_snapshots ss JOIN sources s ON s.id=ss.source_id WHERE s.category='real-world-consumption' AND ss.http_status=200 AND ss.fetch_error IS NULL),
  'auditEvents', (SELECT count(*) FROM audit_events WHERE action='RealWorldConsumptionPublished'),
  'sourcePolicy', (SELECT json_build_object('authority',authority_level,'contentType',content_type,'url',url,'terms',terms_note) FROM sources WHERE category='real-world-consumption' LIMIT 1),
  'latestHash', (SELECT ss.content_hash FROM source_snapshots ss JOIN sources s ON s.id=ss.source_id WHERE s.category='real-world-consumption' ORDER BY ss.fetched_at DESC LIMIT 1),
  'latestObjectKey', (SELECT ss.object_key FROM source_snapshots ss JOIN sources s ON s.id=ss.source_id WHERE s.category='real-world-consumption' ORDER BY ss.fetched_at DESC LIMIT 1)
)::text;
"""))
    assert evidence["rows"] >= 300, evidence
    assert evidence["mappedRows"] >= 150, evidence
    assert evidence["referencedSourceFacts"] == evidence["rows"], evidence
    assert evidence["sampleSizeTotal"] >= 6_000_000, evidence
    assert evidence["invalidSamples"] == 0, evidence
    assert evidence["registrationYears"] == [2021, 2022, 2023], evidence
    assert evidence["snapshots"] >= 1 and evidence["auditEvents"] >= 1, evidence
    assert evidence["sourcePolicy"]["authority"] == "CompetentAuthority", evidence
    assert evidence["sourcePolicy"]["contentType"] == "Csv", evidence
    assert evidence["sourcePolicy"]["url"].startswith("https://sdi.eea.europa.eu/"), evidence
    assert "attribution" in evidence["sourcePolicy"]["terms"].lower(), evidence
    assert len(evidence["latestHash"]) == 64 and evidence["latestObjectKey"].endswith(".csv"), evidence

    catalog = json_get("/api/v1/cars?q=Yaris%20Cross&pageSize=100")
    yaris = next(
        car for car in catalog["data"]
        if car["brandName"] == "Toyota" and car["specifications"]["fuelLitresPer100Km"] is not None
    )
    detail = json_get(f"/api/v1/cars/{yaris['trimId']}")
    cohorts = detail["realWorldConsumption"]
    assert detail["car"]["specifications"]["fuelLitresPer100Km"] == 5.95, detail["car"]
    assert cohorts, "A reviewed Toyota manufacturer mapping must expose EEA cohorts"
    assert {row["vehicleRegistrationYear"] for row in cohorts} == {2023}, cohorts
    assert {row["fuelType"] for row in cohorts} == {"PETROL"}, cohorts
    assert all(row["isTrimSpecific"] is False for row in cohorts), cohorts
    assert all(row["sampleSize"] > 0 for row in cohorts), cohorts
    assert all(row["realWorldFuelWeightedLitresPer100Km"] is not None for row in cohorts), cohorts
    assert all(row["officialWltpFuelWeightedLitresPer100Km"] is not None for row in cohorts), cohorts
    assert all(row["source"]["authority"] == "CompetentAuthority" for row in cohorts), cohorts
    assert all(row["source"]["contentType"] == "Csv" for row in cohorts), cohorts
    assert all(row["source"]["factStatus"] == "Official" for row in cohorts), cohorts
    assert all(row["source"]["confidence"] == "VerifiedOfficial" for row in cohorts), cohorts
    assert all(row["methodologyUrl"].startswith("https://sdi.eea.europa.eu/") for row in cohorts), cohorts
    assert all("European Environment Agency" in row["attribution"] for row in cohorts), cohorts

    # The API contract itself must keep official trim consumption and cohort references separate.
    schema = json_get("/swagger/v1/swagger.json")
    detail_schema = schema["components"]["schemas"]["CarDetailResponse"]
    assert "realWorldConsumption" in detail_schema["properties"], detail_schema
    reference_schema = schema["components"]["schemas"]["RealWorldConsumptionReference"]
    assert {"sampleSize", "isTrimSpecific", "methodologyUrl", "source"} <= set(reference_schema["properties"]), reference_schema
    assert reference_schema["properties"]["sampleSize"]["type"] == "integer", reference_schema
    assert reference_schema["properties"]["isTrimSpecific"]["type"] == "boolean", reference_schema

    html = html_get(f"/cars/{yaris['trimId']}")
    for required in (
        "OFFICIAL TRIM ≠ REAL-WORLD COHORT",
        "Thông số công bố của trim Việt Nam",
        "COHORT — KHÔNG PHẢI TRIM",
        "không phải phép đo của trim này tại Việt Nam",
        "Cỡ mẫu",
        "Dữ liệu gốc EEA",
    ):
        assert required in html, required

    staleness_run_id = reconcile_staleness()
    assert int(psql(
        "SELECT count(*) FROM monitoring_alerts "
        "WHERE status='Open' AND severity IN ('High','Critical') "
        f"AND source_key='{EEA_SOURCE_ID}'"
    )) == 0

    print(json.dumps({
        "gate": "V3.3",
        "status": "PASS",
        "datasetRows": evidence["rows"],
        "mappedRows": evidence["mappedRows"],
        "sampleSizeTotal": evidence["sampleSizeTotal"],
        "registrationYears": evidence["registrationYears"],
        "trimId": yaris["trimId"],
        "officialTrimFuelLitresPer100Km": detail["car"]["specifications"]["fuelLitresPer100Km"],
        "compatibleLatestCohortCount": len(cohorts),
        "allReferencesExplicitlyNonTrim": True,
        "immutableSnapshotHash": evidence["latestHash"],
        "stalenessReconciliationRunId": staleness_run_id,
    }, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
