#!/usr/bin/env python3
"""Repeatable V2.8 full-market coverage gate for the running Compose stack.

The negative-path check temporarily ages one official Porsche source, proves the
gate blocks, and restores the exact timestamp in ``finally``. No generated or
synthetic market record is written to the product database.
"""

from __future__ import annotations

import json
import os
import subprocess
from pathlib import Path
from typing import Any
from urllib.error import HTTPError
from urllib.request import Request, urlopen


ROOT = Path(__file__).resolve().parents[1]
API = os.getenv("VCP_API_BASE", "http://127.0.0.1:8080")
WEB = os.getenv("VCP_WEB_BASE", "http://127.0.0.1:3000")
ADMIN_EMAIL = os.getenv("ADMIN_BOOTSTRAP_EMAIL", "admin@vcp.local")
ADMIN_PASSWORD = os.getenv("ADMIN_BOOTSTRAP_PASSWORD", "vcp-admin-local-dev-only")
EXPECTED_MIGRATION = "20260823062601_AddV28MarketCoverage"


def command(*args: str, check: bool = True) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        args,
        cwd=ROOT,
        check=check,
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


def call(
    path: str,
    *,
    method: str = "GET",
    body: dict[str, Any] | None = None,
    token: str | None = None,
    base: str = API,
) -> tuple[int, Any]:
    headers = {"Accept": "application/json"}
    if token:
        headers["Authorization"] = f"Bearer {token}"
    data = None
    if body is not None:
        data = json.dumps(body).encode("utf-8")
        headers["Content-Type"] = "application/json"
    request = Request(f"{base}{path}", data=data, headers=headers, method=method)  # noqa: S310 - fixed local gate URLs
    try:
        with urlopen(request, timeout=60) as response:  # noqa: S310 - fixed local gate URLs
            raw = response.read()
            content_type = response.headers.get("Content-Type", "")
            return response.status, json.loads(raw) if raw and "json" in content_type else raw.decode("utf-8")
    except HTTPError as error:
        raw = error.read()
        return error.code, json.loads(raw) if raw else None


def require(condition: bool, message: str) -> None:
    if not condition:
        raise AssertionError(message)


def assert_coverage(payload: dict[str, Any]) -> None:
    require(payload["scopeVersion"] == "v2.8", "coverage does not use the reviewed V2.8 scope")
    require(payload["fullMarketGatePassed"] is True, f"full-market gate blocked: {payload['gateFailures']}")
    require(payload["gateFailures"] == [], "passing gate still reports failures")
    require(payload["brandScopeCount"] == 51, "all 51 reviewed brands must remain visible in BrandScope")
    require(payload["reviewedBrandCount"] == 51, "brand review is incomplete")
    require(payload["excludedBrandCount"] == 13, "explicit exclusion count changed")
    require(payload["activeModelCount"] == 255, "official active/coming model inventory changed")
    require(payload["activeTrimCount"] == 49, "explicit official trim inventory changed")
    require(payload["discoveredCandidateCount"] == 304, "candidate inventory is not closed")
    require(payload["resolvedCandidateCount"] == 304, "a candidate was silently dropped")
    require(payload["documentedBlockedCount"] == 236, "documented inventory-gap count changed")
    require(payload["trimInventoryGapCount"] == 236, "trim inventory gaps are not explicit")
    require(len(payload["candidateGaps"]) == 236, "public gap ledger does not match gate totals")
    require(all(gap["reason"].strip() for gap in payload["candidateGaps"]), "gap without a reason")
    require(float(payload["coreCompleteness"]) >= 0.95, "core completeness is below 95%")
    require(float(payload["freshness"]) == 1.0, "market candidates are stale")
    require(payload["unresolvedDuplicates"] == 0, "unresolved duplicate remains")
    domains = {item["domain"]: item for item in payload["freshnessDomains"]}
    require(set(domains) == {"price", "promotion", "dealer-offer", "energy", "legal"}, "freshness domain set changed")
    require(all(item["passed"] and item["sourceCount"] > 0 and item["staleCount"] == 0 for item in domains.values()), "a required freshness domain failed")
    require(len(payload["manifestHash"] or "") == 64, "scope manifest hash is missing")

    brands = {item["brandName"]: item for item in payload["brands"]}
    require(brands["Porsche"]["included"] is True, "Porsche is required in premium scope")
    for name in ("Ferrari", "Lamborghini", "Lotus"):
        require(brands[name]["included"] is False, f"configured exclusion violated: {name}")
    require(all(item["reviewed"] for item in brands.values()), "one or more brand scopes lack review evidence")


def assert_database() -> None:
    require(psql('SELECT migration_id FROM "__EFMigrationsHistory" ORDER BY migration_id DESC LIMIT 1') == EXPECTED_MIGRATION, "V2.8 migration is not latest/applied")
    require(psql("SELECT count(*) FROM brand_scopes WHERE market='VN' AND effective_from<=now() AND (effective_to IS NULL OR effective_to>now())") == "51", "current BrandScope must contain 51 rows")
    require(psql("SELECT count(*) FROM brand_scopes WHERE market='VN' AND included AND effective_from<=now() AND (effective_to IS NULL OR effective_to>now())") == "38", "included BrandScope count changed")
    require(psql("SELECT count(*) FROM brand_scopes WHERE market='VN' AND NOT included AND effective_from<=now() AND (effective_to IS NULL OR effective_to>now())") == "13", "excluded BrandScope count changed")
    require(psql("SELECT count(*) FROM brand_scopes bs LEFT JOIN sources s ON s.id=bs.source_id LEFT JOIN source_snapshots ss ON ss.id=bs.evidence_snapshot_id WHERE bs.market='VN' AND bs.effective_from<=now() AND (bs.effective_to IS NULL OR bs.effective_to>now()) AND (bs.reviewed_at IS NULL OR trim(COALESCE(bs.reviewed_by,''))='' OR s.id IS NULL OR ss.id IS NULL OR ss.source_id<>s.id OR ss.http_status NOT BETWEEN 200 AND 299)") == "0", "scope review provenance is incomplete")
    require(psql("SELECT count(*) FROM market_candidates WHERE market='VN'") == "304", "market candidate table is not the closed manifest inventory")
    require(psql("SELECT count(*) FROM market_candidates mc JOIN sources s ON s.id=mc.source_id JOIN source_snapshots ss ON ss.id=mc.evidence_snapshot_id WHERE mc.market='VN' AND (s.authority_level NOT IN ('BrandOfficial','DistributorOfficial') OR ss.source_id<>s.id OR ss.http_status NOT BETWEEN 200 AND 299)") == "0", "candidate provenance is not official/current HTTP evidence")
    require(psql("SELECT count(*) FROM market_candidates WHERE market='VN' AND ((resolution='Published' AND (model_id IS NULL OR (kind='Trim' AND trim_id IS NULL))) OR (resolution='BlockedWithReason' AND trim(COALESCE(blocked_reason,''))='') OR (kind='Model' AND trim_inventory_status='BlockedWithReason' AND trim(COALESCE(trim_inventory_reason,''))=''))") == "0", "candidate resolution is open or unexplained")
    require(psql("SELECT count(*) FROM (SELECT market,brand_id,kind,external_key,count(*) FROM market_candidates GROUP BY market,brand_id,kind,external_key HAVING count(*)>1) duplicate") == "0", "duplicate market candidate identity exists")
    require(psql("SELECT count(*) FROM trims t JOIN model_years my ON my.id=t.model_year_id JOIN generations g ON g.id=my.generation_id JOIN models m ON m.id=g.model_id JOIN brand_scopes bs ON bs.brand_id=m.brand_id AND bs.market='VN' AND bs.included AND bs.effective_from<=now() AND (bs.effective_to IS NULL OR bs.effective_to>now()) LEFT JOIN market_candidates mc ON mc.trim_id=t.id AND mc.market='VN' AND mc.kind='Trim' AND mc.resolution='Published' WHERE t.market_status IN ('Active','Upcoming','Announced') AND mc.id IS NULL") == "0", "active catalog trim is outside reviewed market inventory")
    require(psql("SELECT count(*) FROM market_scope_reviews WHERE market='VN' AND schema_version='v2.8' AND reviewed_brand_count=51 AND included_brand_count=38 AND excluded_brand_count=13 AND model_candidate_count=255 AND trim_candidate_count=49") == "1", "persisted V2.8 review totals do not match the manifest")


def main() -> None:
    services = [json.loads(line) for line in command("docker", "compose", "ps", "--format", "json").stdout.splitlines() if line.strip()]
    by_service = {item["Service"]: item for item in services}
    required_services = {"postgres", "redis", "minio", "api", "web", "ingestion-worker", "ingestion-scheduler"}
    require(required_services <= by_service.keys(), "Compose stack is incomplete")
    require(all(by_service[name]["State"] == "running" and by_service[name]["Health"] == "healthy" for name in required_services), "Compose stack is not healthy")

    assert_database()
    status, public = call("/api/v1/coverage")
    require(status == 200 and isinstance(public, dict), "public coverage endpoint failed")
    assert_coverage(public)

    status, login = call("/api/v1/admin/auth/login", method="POST", body={"email": ADMIN_EMAIL, "password": ADMIN_PASSWORD})
    require(status == 200 and isinstance(login, dict), "admin login failed")
    status, admin = call("/api/v1/admin/coverage", token=login["token"])
    require(status == 200 and isinstance(admin, dict), "admin coverage endpoint failed")
    assert_coverage(admin)
    for key in ("manifestHash", "reviewedBrandCount", "discoveredCandidateCount", "resolvedCandidateCount", "fullMarketGatePassed"):
        require(admin[key] == public[key], f"public/admin coverage diverged at {key}")

    status, openapi = call("/swagger/v1/swagger.json")
    require(status == 200 and "/api/v1/coverage" in openapi["paths"], "public coverage is missing from OpenAPI")
    committed_openapi = json.loads((ROOT / "packages/contracts/openapi/v1.json").read_text(encoding="utf-8"))
    require("/api/v1/coverage" in committed_openapi["paths"], "committed OpenAPI contract is stale")

    status, html = call("/coverage", base=WEB)
    require(status == 200 and isinstance(html, str), "public coverage page failed")
    for marker in ("FULL-MARKET GATE", "PASS", "255", "49", "Scope hash"):
        require(marker in html, f"coverage page is missing marker: {marker}")

    stale_row = psql("SELECT s.id||'|'||s.last_fetched_at::text FROM sources s JOIN market_candidates mc ON mc.source_id=s.id JOIN brands b ON b.id=mc.brand_id WHERE mc.market='VN' AND b.name='Porsche' ORDER BY mc.kind LIMIT 1")
    require(bool(stale_row), "Porsche official market source not found for negative-path test")
    source_id, original_timestamp = stale_row.split("|", 1)
    escaped_timestamp = original_timestamp.replace("'", "''")
    try:
        psql(f"UPDATE sources SET last_fetched_at=now()-interval '1000 days' WHERE id='{source_id}'::uuid")
        status, blocked = call("/api/v1/coverage")
        require(status == 200 and isinstance(blocked, dict), "negative-path coverage request failed")
        require(blocked["fullMarketGatePassed"] is False, "stale official source did not block full-market badge")
        require("MARKET_CANDIDATE_SOURCE_FRESHNESS_SLA_FAILED" in blocked["gateFailures"], "stale-source blocker code is missing")
    finally:
        psql(f"UPDATE sources SET last_fetched_at='{escaped_timestamp}'::timestamptz WHERE id='{source_id}'::uuid")

    status, restored = call("/api/v1/coverage")
    require(status == 200 and isinstance(restored, dict), "coverage did not recover after restoration")
    assert_coverage(restored)

    print(json.dumps({
        "status": "PASS",
        "scopeVersion": public["scopeVersion"],
        "brands": {"reviewed": public["reviewedBrandCount"], "included": 38, "excluded": public["excludedBrandCount"]},
        "candidates": {"models": public["activeModelCount"], "trims": public["activeTrimCount"], "resolved": public["resolvedCandidateCount"]},
        "documentedGaps": public["documentedBlockedCount"],
        "coreCompleteness": public["coreCompleteness"],
        "freshness": public["freshness"],
        "negativePath": "stale source blocked and exact timestamp restored",
    }, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
