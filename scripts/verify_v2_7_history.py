#!/usr/bin/env python3
"""Repeatable V2.7 gate against the running Docker Compose stack.

The range test temporarily adds two archived observations anchored to an existing
source-backed official price, then removes only those generated rows in finally.
No synthetic record remains in the product database.
"""

from __future__ import annotations

import json
import subprocess
import urllib.error
import urllib.request
import uuid
from datetime import UTC, datetime, timedelta
from decimal import Decimal
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
API = "http://localhost:8080"
WEB = "http://localhost:3000"


def run(*args: str, check: bool = True) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        args,
        cwd=ROOT,
        check=check,
        capture_output=True,
        text=True,
        encoding="utf-8",
    )


def psql(sql: str) -> str:
    result = run(
        "docker",
        "compose",
        "exec",
        "-T",
        "postgres",
        "psql",
        "-U",
        "vcp",
        "-d",
        "vietnam_car_platform",
        "-v",
        "ON_ERROR_STOP=1",
        "-Atc",
        sql,
    )
    return result.stdout.strip()


def request(path: str, expected: int = 200) -> object:
    try:
        with urllib.request.urlopen(f"{API}{path}", timeout=10) as response:
            status = response.status
            body = response.read()
    except urllib.error.HTTPError as error:
        status = error.code
        body = error.read()
    assert status == expected, (path, status, body[:500])
    return json.loads(body) if body else None


def page(path: str) -> str:
    with urllib.request.urlopen(f"{WEB}{path}", timeout=20) as response:
        assert response.status == 200
        return response.read().decode("utf-8")


def sql_timestamp(value: datetime) -> str:
    return value.astimezone(UTC).isoformat().replace("+00:00", "Z")


def main() -> None:
    services = [json.loads(line) for line in run("docker", "compose", "ps", "--format", "json").stdout.splitlines()]
    health = {item["Service"]: item.get("Health") for item in services}
    expected_services = {"postgres", "redis", "minio", "api", "web", "ingestion-worker", "ingestion-scheduler"}
    assert expected_services <= health.keys(), health
    assert all(health[name] == "healthy" for name in expected_services), health

    price_row = psql(
        "SELECT p.id||'|'||p.trim_id||'|'||p.source_fact_id||'|'||p.amount "
        "FROM prices p WHERE p.price_type='Msrp' AND p.status='Official' "
        "AND p.amount IS NOT NULL AND p.source_fact_id IS NOT NULL "
        "AND p.effective_from <= now() AND (p.effective_to IS NULL OR p.effective_to > now()) "
        "ORDER BY p.updated_at DESC LIMIT 1;"
    )
    assert price_row, "A real source-backed current MSRP is required"
    price_id, trim_id, source_fact_id, current_raw = price_row.split("|")
    current_amount = Decimal(current_raw)
    before_count = int(psql("SELECT count(*) FROM price_history;"))

    baseline = request(f"/api/v1/cars/{trim_id}/prices?regionScope=VN&months=12")
    assert isinstance(baseline, dict)
    assert baseline["timeline"]
    assert any(item["valueKind"] == "CashPrice" and item["source"] for item in baseline["timeline"])
    # Curated V1 currently has one observation per trim. The product must refuse
    # a low/high claim rather than infer a trend from that point.
    assert baseline["currentVsTwelveMonthRange"]["available"] is False
    assert baseline["currentVsTwelveMonthRange"]["reasonCode"] == "INSUFFICIENT_12_MONTH_HISTORY"

    generated_ids = [uuid.uuid4(), uuid.uuid4()]
    now = datetime.now(UTC)
    older = now - timedelta(days=300)
    newer = now - timedelta(days=150)
    try:
        rows = [
            (generated_ids[0], older, newer, current_amount + Decimal("20000000")),
            (generated_ids[1], newer, now - timedelta(days=2), current_amount + Decimal("10000000")),
        ]
        values = ",".join(
            "(" + ",".join(
                [
                    f"'{row_id}'::uuid",
                    f"'{price_id}'::uuid",
                    f"'{trim_id}'::uuid",
                    "'Msrp'",
                    str(amount),
                    "'VND'",
                    "'VN'",
                    "'Official'",
                    f"'{sql_timestamp(effective_from)}'::timestamptz",
                    f"'{sql_timestamp(effective_to)}'::timestamptz",
                    f"'{source_fact_id}'::uuid",
                    "NULL",
                    f"'{sql_timestamp(effective_to)}'::timestamptz",
                    f"'{sql_timestamp(effective_to)}'::timestamptz",
                    f"'{sql_timestamp(effective_to)}'::timestamptz",
                ]
            ) + ")"
            for row_id, effective_from, effective_to, amount in rows
        )
        psql(
            "INSERT INTO price_history "
            "(id,price_id,trim_id,price_type,amount,currency,region_scope,status,effective_from,effective_to,"
            "source_fact_id,manual_override_reason,archived_at,created_at,updated_at) VALUES " + values + ";"
        )
        enough = request(f"/api/v1/cars/{trim_id}/prices?regionScope=VN&months=12")
        insight = enough["currentVsTwelveMonthRange"]
        assert insight["available"] is True, insight
        assert insight["reasonCode"] == "ENOUGH_HISTORY", insight
        assert insight["observationCount"] >= 3 and insight["spanDays"] >= 90, insight
        assert Decimal(str(insight["twelveMonthMinimum"])) == current_amount, insight
        assert insight["position"] == "At12MonthLow", insight
    finally:
        ids = ",".join(f"'{value}'::uuid" for value in generated_ids)
        psql(f"DELETE FROM price_history WHERE id IN ({ids});")
    assert int(psql("SELECT count(*) FROM price_history;")) == before_count

    offers = request(f"/api/v1/cars/{trim_id}/dealer-offers?months=12")
    assert offers["cashSemantics"].startswith("Only structured cash-equivalent")
    assert all(not item["isStale"] for item in offers["current"])

    energy = request("/api/v1/energy/prices/history?regionCode=VN&months=24")
    assert energy["series"], energy
    observations = [item for series in energy["series"] for item in series["observations"]]
    assert observations and all(item["source"] for item in observations), observations
    series_keys = [series["seriesKey"] for series in energy["series"]]
    assert len(series_keys) == len(set(series_keys)), series_keys

    request("/api/v1/energy/prices/history?months=0", expected=400)
    request(f"/api/v1/cars/{uuid.uuid4()}/prices", expected=404)
    openapi = request("/swagger/v1/swagger.json")
    for path in (
        "/api/v1/cars/{trimId}/prices",
        "/api/v1/cars/{trimId}/dealer-offers",
        "/api/v1/energy/prices/history",
    ):
        assert path in openapi["paths"], path

    package = json.loads((ROOT / "apps/web/package.json").read_text(encoding="utf-8"))
    assert package["dependencies"]["recharts"] == "3.10.1"
    detail_html = page(f"/cars/{trim_id}")
    assert "Chưa đủ dữ liệu để kết luận giá đang thấp hay cao" in detail_html
    assert "Lịch sử giá và ưu đãi" in detail_html
    energy_html = page("/energy/history?months=24")
    assert "Lịch sử giá xăng, dầu và điện" in energy_html
    assert "Không có bản ghi nguồn phù hợp" not in energy_html
    assert "VND/VND/" not in energy_html

    print(json.dumps({
        "status": "PASS",
        "trimId": trim_id,
        "baselineRangeClaim": "withheld",
        "threeObservationRangePolicy": "passed-and-cleaned",
        "energySeries": len(energy["series"]),
        "energyObservations": len(observations),
        "recharts": package["dependencies"]["recharts"],
    }, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
