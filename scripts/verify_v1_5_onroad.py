#!/usr/bin/env python3
"""V1.5 golden gate against the running source-seeded Compose stack."""

from __future__ import annotations

import json
import subprocess
from pathlib import Path
from urllib.parse import urlencode
from urllib.request import Request, urlopen


API = "http://127.0.0.1:8080"
VF6_QUERY = urlencode({"q": "VF 6", "Powertrain": "Bev"})


def get_json(path: str) -> dict:
    with urlopen(f"{API}{path}", timeout=10) as response:  # noqa: S310 - fixed localhost gate
        assert response.status == 200
        return json.load(response)


def calculate(trim_id: str, province: str, date: str) -> dict:
    body = json.dumps(
        {
            "trimId": trim_id,
            "provinceCode": province,
            "calculationDate": f"{date}T12:00:00+07:00",
            "buyerType": "Individual",
            "vehicleType": "PassengerCar",
            "firstInspectionExempt": True,
            "roadUsageMonths": 12,
            "selectedOfferIds": [],
        }
    ).encode()
    request = Request(  # noqa: S310 - fixed localhost gate
        f"{API}/api/v1/calculators/on-road",
        data=body,
        headers={"Content-Type": "application/json"},
        method="POST",
    )
    with urlopen(request, timeout=10) as response:  # noqa: S310 - fixed localhost gate
        assert response.status == 200
        return json.load(response)


def component(result: dict, name: str) -> dict:
    return next(item for item in result["breakdown"] if item["component"] == name)


def psql(sql: str) -> None:
    subprocess.run(
        ["docker", "compose", "exec", "-T", "postgres", "psql", "-v", "ON_ERROR_STOP=1", "-U", "vcp", "-d", "vietnam_car_platform"],
        input=sql,
        text=True,
        check=True,
        capture_output=True,
    )


def main() -> None:
    regions = get_json("/api/v1/regions")["data"]
    assert len(regions) == 34
    assert next(region for region in regions if region["code"] == "VN-01")["areaClass"] == "I"
    area_ii = next(region for region in regions if region["areaClass"] == "II")
    assert all(region["source"] and len(region["source"]["contentHash"]) == 64 for region in regions)

    cars = get_json(f"/api/v1/cars?{VF6_QUERY}")["data"]
    vf6 = next(car for car in cars if car["brandName"] == "VinFast" and car["modelName"] == "VF 6")
    assert vf6["specifications"]["seats"] == 5 and vf6["powertrainType"] == "Bev"

    hanoi = calculate(vf6["trimId"], "VN-01", "2026-08-22")
    assert component(hanoi, "PlateAndRegistrationFee")["amount"] == 14_000_000
    assert component(hanoi, "FirstRegistrationTax")["amount"] == 0
    assert component(hanoi, "CompulsoryInsurance")["amount"] == 480_700
    assert component(hanoi, "InspectionFee")["amount"] == 0
    assert component(hanoi, "RoadUsageFee")["amount"] == 1_560_000
    assert hanoi["result"]["onRoadPrice"] == 662_040_700
    assert not hanoi["warnings"]
    assert all(rule["source"] and rule["source"]["url"].startswith("https://") for rule in hanoi["appliedRules"])

    regional = calculate(vf6["trimId"], area_ii["code"], "2026-08-22")
    assert component(regional, "PlateAndRegistrationFee")["amount"] == 140_000

    future = calculate(vf6["trimId"], "VN-01", "2027-03-01")
    future_tax = component(future, "FirstRegistrationTax")
    assert future_tax["amount"] == 0
    assert future_tax["appliedRule"]["version"] == 2
    assert "docid=218368" in future_tax["appliedRule"]["source"]["url"]

    changed_amount = 14_123_000
    try:
        psql(
            "UPDATE registration_rules SET parameters_json = jsonb_set(parameters_json, '{amount}', '14123000'::jsonb) "
            "WHERE component = 'PlateAndRegistrationFee' AND parameters_json->>'amount' = '14000000';"
        )
        changed = calculate(vf6["trimId"], "VN-01", "2026-08-22")
        assert component(changed, "PlateAndRegistrationFee")["amount"] == changed_amount
        assert changed["result"]["onRoadPrice"] == hanoi["result"]["onRoadPrice"] + 123_000
    finally:
        psql(
            "UPDATE registration_rules SET parameters_json = jsonb_set(parameters_json, '{amount}', '14000000'::jsonb) "
            "WHERE component = 'PlateAndRegistrationFee' AND parameters_json->>'amount' = '14123000';"
        )

    page_query = urlencode(
        {"trimId": vf6["trimId"], "provinceCode": "VN-01", "calculationDate": "2026-08-22", "buyerType": "Individual"}
    )
    html = subprocess.run(
        ["docker", "compose", "exec", "-T", "web", "wget", "-qO-", f"http://127.0.0.1:3000/calculators/on-road?{page_query}"],
        check=True,
        capture_output=True,
    ).stdout.decode("utf-8").replace("<!-- -->", "")
    for expected in ("662.040.700", "14.000.000", "Decree 51/2025", "Rule v1", "Giá mua tiền mặt hiệu lực"):
        assert expected in html, expected

    web_sources = [*Path("apps/web/app").rglob("*.tsx"), *Path("apps/web/components").rglob("*.tsx")]
    rendered_code = "\n".join(path.read_text(encoding="utf-8") for path in web_sources)
    assert "14000000" not in rendered_code and "14123000" not in rendered_code
    print("PASS V1.5: 34 regions, temporal legal rules, sourced breakdown, SSR and DB-driven no-redeploy update")


if __name__ == "__main__":
    main()
