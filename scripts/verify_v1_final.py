#!/usr/bin/env python3
"""Final V1 gate: component goldens plus one continuous public-product journey."""

from __future__ import annotations

import json
import math
import subprocess
import sys
import time
from pathlib import Path
from urllib.parse import urlencode
from urllib.request import urlopen

import verify_v1_5_onroad as v15
import verify_v1_6_energy as v16
import verify_v1_7_affordability as v17
import verify_v1_8_financing as v18
import verify_v1_9_compare as v19


ROOT = Path(__file__).resolve().parent.parent
API = "http://127.0.0.1:8080"
WEB = "http://127.0.0.1:3000"
VF6 = "8b31de05-bd4c-5b70-9efd-47879f5e609c"
COMPONENT_GATES = [
    "verify_v1_3_catalog.py",
    "verify_v1_4_web.py",
    "verify_v1_5_onroad.py",
    "verify_v1_6_energy.py",
    "verify_v1_7_affordability.py",
    "verify_v1_8_financing.py",
    "verify_v1_9_compare.py",
    "verify_v1_10_admin.py",
]


def get_json(base: str, path: str) -> dict:
    with urlopen(f"{base}{path}", timeout=30) as response:  # noqa: S310 - fixed local gate endpoint
        assert response.status == 200, (path, response.status)
        return json.load(response)


def get_html(path: str) -> str:
    with urlopen(f"{WEB}{path}", timeout=30) as response:  # noqa: S310 - fixed local gate endpoint
        assert response.status == 200, (path, response.status)
        return response.read().decode("utf-8").replace("<!-- -->", "")


def compose(*args: str) -> None:
    subprocess.run(["docker", "compose", *args], cwd=ROOT, check=True)


def warm_p95_ms(action, samples: int) -> float:
    action()
    durations = []
    for _ in range(samples):
        started = time.perf_counter()
        action()
        durations.append((time.perf_counter() - started) * 1_000)
    return sorted(durations)[math.ceil(samples * 0.95) - 1]


def compare_once() -> None:
    status, payload = v19.request(v19.scenario())
    assert status == 200 and payload["vehicles"]


def run_component_gates() -> None:
    for gate in COMPONENT_GATES:
        subprocess.run([sys.executable, str(ROOT / "scripts" / gate)], cwd=ROOT, check=True)


def verify_continuous_journey() -> None:
    catalog = get_json(API, f"/api/v1/cars?{urlencode({'q': 'VF 6', 'Powertrain': 'Bev'})}")
    car = next(item for item in catalog["data"] if item["trimId"] == VF6)
    assert car["marketStatus"] == "Active" and car["currentPrice"]["type"] == "Msrp"

    detail = get_json(API, f"/api/v1/cars/{car['trimId']}")
    official_prices = [price for price in detail["prices"] if price["status"] == "Official"]
    assert official_prices and all(price["source"] for price in official_prices)
    assert all(len(price["source"]["contentHash"]) == 64 for price in official_prices)
    assert isinstance(detail["dealerOffers"], list)
    for offer in detail["dealerOffers"]:
        assert offer["status"] == "Published" and offer["source"]
        assert offer["source"]["url"].startswith("https://")
        assert isinstance(offer["benefits"], list)

    detail_html = get_html(f"/cars/{car['trimId']}")
    assert "Giá và hiệu lực" in detail_html and "Ưu đãi đại lý" in detail_html
    assert 'type="application/ld+json"' in detail_html and '"@type":"Vehicle"' in detail_html
    if not detail["dealerOffers"]:
        assert "Chưa có ưu đãi đại lý còn hiệu lực được publish." in detail_html

    on_road = v15.calculate(car["trimId"], "VN-01", "2026-08-22")
    assert on_road["result"]["onRoadPrice"] >= official_prices[0]["amount"]
    assert on_road["appliedRules"] and all(rule["source"] for rule in on_road["appliedRules"])

    energy = v16.calculate(v16.scenario(VF6, homeChargingShare=1, householdBaseKwh=250))
    assert energy["result"]["normalizedCost"] > 0 and energy["appliedRates"]

    ownership_request = v17.profile()
    ownership_request["trimIds"] = [VF6]
    ownership = v17.post("/api/v1/affordability/evaluate", ownership_request)
    ownership_candidates = ownership["eligibleCars"] + ownership["overBudgetCars"]
    assert len(ownership_candidates) == 1 and ownership_candidates[0]["vehicle"]["trimId"] == VF6
    assert ownership_candidates[0]["evaluation"]["reasons"] is not None

    financing = v18.post(v18.scenario())
    assert financing["onRoad"]["result"]["onRoadPrice"] == on_road["result"]["onRoadPrice"]
    assert financing["purchaseRating"] and financing["ownershipAffordability"]["reasons"] is not None

    purchase_filter = v17.profile()
    purchase_filter["maximumMonthlyVehicleSpend"] = 2_300_000
    filtered = v17.post("/api/v1/affordability/evaluate", purchase_filter)
    assert [item["vehicle"]["trimId"] for item in filtered["eligibleCars"]] == [VF6]
    assert all(item["evaluation"]["reasons"] for item in filtered["overBudgetCars"])

    compare_status, comparison = v19.request(v19.scenario())
    assert compare_status == 200 and comparison["vehicles"][0]["trimId"] == VF6
    assert any(row["code"] == "on_road" and row["cells"][0]["sources"] for row in comparison["rows"])

    detail_p95 = warm_p95_ms(lambda: get_json(API, f"/api/v1/cars/{VF6}"), 20)
    compare_p95 = warm_p95_ms(compare_once, 10)
    assert detail_p95 < 400, f"warm detail p95 {detail_p95:.2f}ms is above 400ms"
    assert compare_p95 < 700, f"warm compare p95 {compare_p95:.2f}ms is above 700ms"

    private_html = get_html("/financing")
    assert 'name="robots"' in private_html and "noindex" in private_html
    print(f"V1 final performance: detail p95 {detail_p95:.2f}ms; heavy compare p95 {compare_p95:.2f}ms")


def verify_catalog_has_no_live_external_dependency() -> None:
    catalog_service = (ROOT / "apps/api/src/Api/Features/Catalog/CatalogService.cs").read_text(encoding="utf-8")
    for forbidden in ("HttpClient", "IHttpClientFactory", "Brave", "OpenChargeMap", "Goong"):
        assert forbidden not in catalog_service, forbidden
    for relative in (
        "Registration/RegistrationEndpoints.cs",
        "Energy/EnergyEndpoints.cs",
        "Affordability/AffordabilityEndpoints.cs",
        "Financing/FinancingEndpoints.cs",
        "Compare/CompareEndpoints.cs",
    ):
        source = (ROOT / "apps/api/src/Api/Features" / relative).read_text(encoding="utf-8")
        assert 'RequireRateLimiting("anonymous-heavy")' in source, relative

    workers_stopped = False
    try:
        compose("stop", "ingestion-worker", "ingestion-scheduler")
        workers_stopped = True
        cold_query = urlencode({"q": "VF 6 Eco", "Powertrain": "Bev", "PageSize": 7})
        api_catalog = get_json(API, f"/api/v1/cars?{cold_query}")
        assert api_catalog["pagination"]["totalItems"] == 1
        html = get_html(f"/cars?{cold_query}")
        assert "VF 6" in html and "Phiên bản phù hợp" in html
    finally:
        if workers_stopped:
            compose("up", "--detach", "--wait", "ingestion-worker", "ingestion-scheduler")


def main() -> None:
    assert get_json(API, "/health/live")["status"] == "Healthy"
    run_component_gates()
    verify_continuous_journey()
    verify_catalog_has_no_live_external_dependency()
    assert get_json(API, "/health/ready")["status"] == "Healthy"
    print(
        "PASS V1 FINAL: catalog -> detail/offer -> on-road/energy -> ownership -> financing "
        "-> purchase filter -> compare; component goldens and external-independent catalog verified"
    )


if __name__ == "__main__":
    main()
