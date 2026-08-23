from __future__ import annotations

import json
import subprocess
import sys
from pathlib import Path
from typing import Any
from urllib.error import HTTPError
from urllib.request import Request, urlopen


ROOT = Path(__file__).resolve().parents[1]
API = "http://localhost:8080"
WEB = "http://localhost:3000"
EXPECTED_MIGRATION = "20260823050109_AddV26ChargingMapData"


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
        "psql", "-U", "vcp", "-d", "vietnam_car_platform", "-Atc", query,
    ).stdout.strip()


def request_json(path: str) -> tuple[int, dict[str, Any]]:
    request = Request(f"{API}{path}", headers={"Accept": "application/json"})
    try:
        with urlopen(request, timeout=10) as response:
            return response.status, json.loads(response.read())
    except HTTPError as error:
        return error.code, json.loads(error.read())


def require(condition: bool, message: str) -> None:
    if not condition:
        raise AssertionError(message)


def main() -> None:
    registry = json.loads((ROOT / "data" / "source-registry.v1.json").read_text(encoding="utf-8"))
    source = next((value for value in registry["sources"] if value["id"] == "open-charge-map"), None)
    require(source is not None, "Open Charge Map is missing from the source registry")
    require(source["category"] == "charging-poi", "OCM source must use the charging-poi schedule")
    require(source["authority"] == "TrustedSecondary", "OCM cannot be marked provider-official")
    require("never use OCM" in source["terms_note"], "registry must reject OCM tariff authority")

    compose_rows = [
        json.loads(line)
        for line in command("docker", "compose", "ps", "--format", "json").stdout.splitlines()
        if line.strip().startswith("{")
    ]
    by_service = {row["Service"]: row for row in compose_rows}
    for service in ("postgres", "redis", "minio", "api", "web", "ingestion-worker", "ingestion-scheduler"):
        require(service in by_service, f"compose service is not running: {service}")
        require(by_service[service]["State"] == "running", f"compose service is not running: {service}")
        require(by_service[service]["Health"] == "healthy", f"compose service is not healthy: {service}")

    latest = psql('SELECT migration_id FROM "__EFMigrationsHistory" ORDER BY migration_id DESC LIMIT 1')
    require(latest == EXPECTED_MIGRATION, f"unexpected latest migration: {latest}")
    require(
        psql("SELECT to_regclass('public.charging_stations') IS NOT NULL") == "t",
        "charging_stations table is missing",
    )
    forbidden = psql(
        "SELECT COALESCE(string_agg(column_name, ','), '') "
        "FROM information_schema.columns WHERE table_name='charging_stations' "
        "AND (column_name ILIKE '%tariff%' OR column_name ILIKE '%price%' OR column_name ILIKE '%cost%')"
    )
    require(forbidden == "", f"OCM station cache contains forbidden tariff-like columns: {forbidden}")

    station_status, stations = request_json("/api/v1/charging/stations?Limit=200")
    require(station_status == 200, "cached station API did not return 200")
    require(stations["dataset"]["coverage"] == "ReferenceOnly", "dataset must be reference-only")
    require("OCM usage-cost text is ignored" in stations["dataset"]["tariffPolicy"], "tariff policy is missing")
    for station in stations["data"]:
        require(station["coverage"] == "ReferenceOnly", "station coverage must be reference-only")
        require("not provider verified" in station["confidenceBasis"], "confidence disclosure is missing")
        if station["tariff"] is None:
            require(
                station["tariffAuthority"] == "UnavailableUntilReviewedProviderMapping",
                "unmapped station exposed a tariff authority",
            )
        else:
            require(station["tariffAuthority"] == "ProviderOfficialSource", "mapped tariff is not provider-authoritative")
            require("openchargemap" not in station["tariff"]["sourceUrl"].lower(), "OCM leaked into tariff provenance")

    invalid_status, invalid = request_json("/api/v1/charging/stations?MinLatitude=20&MaxLatitude=22")
    require(invalid_status == 400 and invalid["code"] == "CHARGING_BBOX_INCOMPLETE", "bbox guard failed")
    capabilities_status, capabilities = request_json("/api/v1/maps/capabilities")
    require(capabilities_status == 200, "map capabilities API did not return 200")
    require(capabilities["mapTilesKeyExposed"] is False, "server map key must never be exposed")

    ocm_configured = command(
        "docker", "compose", "exec", "-T", "ingestion-scheduler", "python", "-c",
        "from ingestion.settings import Settings; print('1' if Settings().open_charge_map_api_key.get_secret_value().strip() else '0')",
    ).stdout.strip() == "1"
    if not ocm_configured:
        lease = command(
            "docker", "compose", "exec", "-T", "redis", "redis-cli", "EXISTS",
            "ingestion:next-fetch:charging_poi_locations:open-charge-map",
        ).stdout.strip()
        require(lease == "0", "optional OCM sync acquired a lease without a key")
    if not capabilities["goongGeocodingEnabled"]:
        geocode_status, geocode = request_json("/api/v1/maps/geocode?address=Ha%20Noi")
        require(
            geocode_status == 503 and geocode["code"] == "GOONG_NOT_CONFIGURED",
            "missing Goong key did not degrade cleanly",
        )

    catalog_status, _ = request_json("/api/v1/cars?PageSize=1")
    require(catalog_status == 200, "core catalog is not available independently of OCM/Goong")
    with urlopen(f"{WEB}/charging", timeout=10) as response:
        page = response.read().decode("utf-8")
        require(response.status == 200, "charging page did not return 200")
        require("không có dữ liệu giả thay thế" in page, "empty state does not disclose real-data policy")

    openapi = json.loads((ROOT / "packages" / "contracts" / "openapi" / "v1.json").read_text(encoding="utf-8"))
    for path in ("/api/v1/charging/stations", "/api/v1/maps/geocode", "/api/v1/maps/capabilities"):
        require(path in openapi["paths"], f"OpenAPI is missing {path}")

    print(json.dumps({
        "status": "PASS",
        "migration": latest,
        "cachedStations": stations["count"],
        "ocmKeyConfigured": ocm_configured,
        "goongKeyConfigured": capabilities["goongGeocodingEnabled"],
        "mapTilesKeyExposed": capabilities["mapTilesKeyExposed"],
        "tariffPolicy": "provider-only-after-reviewed-mapping",
    }, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    try:
        main()
    except Exception as error:
        print(f"V2.6 gate failed: {error}", file=sys.stderr)
        raise
