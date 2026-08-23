#!/usr/bin/env python3
"""V1.6 golden gate against the running, source-published Compose stack."""

from __future__ import annotations

import json
import subprocess
from pathlib import Path
from urllib.parse import urlencode
from urllib.request import Request, urlopen


API = "http://127.0.0.1:8080"
TOYOTA_TRIM_ID = "6d9b50a0-d340-516a-9c90-89bed9484b42"
VF6_TRIM_ID = "8b31de05-bd4c-5b70-9efd-47879f5e609c"
BYD_TRIM_ID = "13bb54aa-f730-5a7a-a12d-9050aa0e58fd"


def calculate(body: dict) -> dict:
    request = Request(  # noqa: S310 - fixed localhost gate
        f"{API}/api/v1/calculators/energy",
        data=json.dumps(body).encode(),
        headers={"Content-Type": "application/json"},
        method="POST",
    )
    with urlopen(request, timeout=10) as response:  # noqa: S310 - fixed localhost gate
        assert response.status == 200
        return json.load(response)


def base(trim_id: str) -> dict:
    return {
        "trimId": trim_id,
        "calculationDate": "2026-08-22T12:00:00+07:00",
        "monthlyKilometres": 1_000,
        "chargingEfficiency": 0.9,
    }


def scenario(trim_id: str, **overrides: object) -> dict:
    request = base(trim_id)
    request.update(overrides)
    return request


def psql(sql: str) -> None:
    subprocess.run(
        [
            "docker",
            "compose",
            "exec",
            "-T",
            "postgres",
            "psql",
            "-v",
            "ON_ERROR_STOP=1",
            "-U",
            "vcp",
            "-d",
            "vietnam_car_platform",
        ],
        input=sql,
        text=True,
        check=True,
        capture_output=True,
    )


def assert_provenance(result: dict) -> None:
    sources = [rate["source"] for rate in result["appliedRates"]]
    sources.append(result["energyProfile"]["source"])
    sources.extend(promotion["source"] for promotion in result["appliedPromotions"])
    assert sources and all(source for source in sources)
    assert all(source["url"].startswith("https://") for source in sources)
    assert all(len(source["contentHash"]) == 64 and not source["isStale"] for source in sources)


def main() -> None:
    ice_request = scenario(TOYOTA_TRIM_ID, fuelType="E10Ron95III")
    ice = calculate(ice_request)
    assert ice["result"] == {
        "currentCost": 1_348_746,
        "normalizedCost": 1_348_746,
        "promotionSavings": 0,
        "fuelLitres": 59.5,
        "batteryEnergyKwh": 0,
        "gridEnergyKwh": 0,
        "currency": "VND",
    }
    assert ice["energyProfile"]["fuelConsumptionCondition"] == "Combined manufacturer disclosure"
    assert_provenance(ice)

    home = calculate(scenario(VF6_TRIM_ID, homeChargingShare=1, householdBaseKwh=250))
    assert home["result"]["currentCost"] == 513_409
    assert home["result"]["normalizedCost"] == 513_409
    assert len([item for item in home["breakdown"] if item["component"] == "HomeChargingTier"]) == 2
    assert home["energyProfile"]["electricConsumptionCondition"] == "Pure electric; Vietnam type N5DG03"
    assert_provenance(home)

    public = calculate(
        scenario(
            VF6_TRIM_ID,
            homeChargingShare=0,
            publicSessions=6,
            postChargeMinutesPerSession=70,
            customerType="Organization",
        )
    )
    assert public["result"]["currentCost"] == 977_781
    assert public["result"]["normalizedCost"] == 977_781
    assert next(item for item in public["breakdown"] if item["component"] == "PostChargeServiceFee")[
        "currentAmount"
    ] == 420_000
    assert_provenance(public)

    promo = calculate(
        scenario(
            VF6_TRIM_ID,
            homeChargingShare=0,
            publicSessions=6,
            sessionsUsedThisMonth=0,
            customerType="Personal",
            purchaseDate="2026-02-10",
            promotionEligibilityConfirmed=True,
        )
    )
    assert promo["result"]["currentCost"] == 0
    assert promo["result"]["normalizedCost"] == 557_781
    assert promo["result"]["promotionSavings"] == 557_781
    assert len(promo["appliedPromotions"]) == 1
    assert_provenance(promo)

    phev = calculate(
        scenario(
            BYD_TRIM_ID,
            fuelType="E10Ron95III",
            evShare=0.6,
            homeChargingShare=0.7,
            householdBaseKwh=300,
            publicSessions=3,
            customerType="Organization",
        )
    )
    assert phev["result"]["currentCost"] == 848_996
    assert phev["result"]["fuelLitres"] == 18.88
    assert phev["result"]["batteryEnergyKwh"] == 101.4
    assert phev["energyProfile"]["fuelConsumptionCondition"] == "Charge-sustaining SOC below 25%"
    assert phev["energyProfile"]["electricConsumptionCondition"] == "Charge-depleting electric energy ADR 81/02"
    assert_provenance(phev)

    try:
        psql("UPDATE energy_prices SET amount = 23000 WHERE energy_type = 'E10Ron95III' AND amount = 22668;")
        changed = calculate(ice_request)
        assert changed["result"]["currentCost"] == 1_368_500
    finally:
        psql("UPDATE energy_prices SET amount = 22668 WHERE energy_type = 'E10Ron95III' AND amount = 23000;")

    query = urlencode(
        {
            "trimId": VF6_TRIM_ID,
            "calculationDate": "2026-08-22",
            "monthlyKilometres": "1000",
            "homeSharePercent": "0",
            "publicSessions": "6",
            "customerType": "Personal",
            "purchaseDate": "2026-02-10",
            "promotionEligibilityConfirmed": "true",
        }
    )
    html = subprocess.run(
        [
            "docker",
            "compose",
            "exec",
            "-T",
            "web",
            "wget",
            "-qO-",
            f"http://127.0.0.1:3000/calculators/energy?{query}",
        ],
        check=True,
        capture_output=True,
    ).stdout.decode("utf-8").replace("<!-- -->", "")
    for expected in ("557.781", "Chi phí hiện tại", "Chi phí chuẩn hóa", "PROMOTION APPLIED", "N5DG03"):
        assert expected in html, expected

    web_sources = [*Path("apps/web/app").rglob("*.tsx"), *Path("apps/web/components").rglob("*.tsx")]
    rendered_code = "\n".join(path.read_text(encoding="utf-8") for path in web_sources)
    for forbidden in ("22668", "3858", "1984", "3460", "557781"):
        assert forbidden not in rendered_code, forbidden

    print("PASS V1.6: ICE, BEV home/public, conditional promotion, PHEV, provenance, SSR and DB-driven rates")


if __name__ == "__main__":
    main()
