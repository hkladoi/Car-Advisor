#!/usr/bin/env python3
"""V1.7 golden gate for ownership and salary affordability."""

from __future__ import annotations

import json
import subprocess
from pathlib import Path
from urllib.request import Request, urlopen


API = "http://127.0.0.1:8080"
VF6_TRIM_ID = "8b31de05-bd4c-5b70-9efd-47879f5e609c"


def post(path: str, body: dict) -> dict:
    request = Request(  # noqa: S310 - fixed localhost gate
        f"{API}{path}",
        data=json.dumps(body).encode(),
        headers={"Content-Type": "application/json"},
        method="POST",
    )
    with urlopen(request, timeout=30) as response:  # noqa: S310 - fixed localhost gate
        assert response.status == 200
        return json.load(response)


def profile() -> dict:
    return {
        "trimIds": [],
        "provinceCode": "VN-01",
        "calculationDate": "2026-08-22T12:00:00+07:00",
        "policy": "Balanced",
        "netMonthlyIncome": 20_000_000,
        "rentHousing": 2_000_000,
        "essentialExpenses": 6_000_000,
        "otherFixedDebt": 0,
        "savingsTarget": 2_000_000,
        "maximumMonthlyVehicleSpend": None,
        "expenses": {
            "monthlyKilometres": 1_000,
            "parkingMonthly": 500_000,
            "maintenanceReserveMonthly": 800_000,
            "bodyInsuranceAnnual": 0,
            "tyreReserveMonthly": 250_000,
            "batteryRentalMonthly": 0,
            "compulsoryInsuranceMonthlyOverride": None,
            "roadUsageMonthlyOverride": None,
            "inspectionMonthlyOverride": None,
            "firstInspectionExempt": True,
        },
        "energy": {
            "fuelType": "E10Ron95III",
            "evShare": 0.6,
            "homeChargingShare": 1,
            "chargingEfficiency": 0.9,
            "homeMode": "EvnMarginalTiers",
            "householdBaseKwh": 250,
            "customHomeAmountPerKwh": None,
            "chargingProviderSlug": "v-green",
            "connectorType": "DC",
            "chargingPowerKw": 60,
            "publicSessions": 6,
            "sessionsUsedThisMonth": 0,
            "postChargeMinutesPerSession": 0,
            "customerType": "Personal",
            "purchaseDate": None,
            "promotionEligibilityConfirmed": False,
        },
    }


def by_model(result: dict, model: str) -> dict:
    candidates = result["eligibleCars"] + result["overBudgetCars"]
    return next(candidate for candidate in candidates if candidate["vehicle"]["modelName"] == model)


def assert_sources(candidate: dict) -> None:
    rules = candidate["ownership"]["appliedRecurringRules"]
    assert {rule["component"] for rule in rules} == {"CompulsoryInsurance", "InspectionFee", "RoadUsageFee"}
    assert all(rule["source"] and rule["source"]["url"].startswith("https://") for rule in rules)
    rates = candidate["ownership"]["energy"]["appliedRates"]
    assert rates and all(rate["source"] and len(rate["source"]["contentHash"]) == 64 for rate in rates)


def main() -> None:
    low = post("/api/v1/affordability/evaluate", profile())
    assert low["policy"] == "Balanced"
    assert low["thresholds"]["maximumIncomeRatio"] == 0.2
    assert low["thresholds"]["maximumDisposableRatio"] == 0.5
    assert len(low["eligibleCars"]) == 3 and len(low["overBudgetCars"]) == 0
    assert len(low["dataExcludedCars"]) == 9
    assert all(item["reasons"] == ["ENERGY_PROFILE_UNKNOWN"] for item in low["dataExcludedCars"])
    vf6 = by_model(low, "VF 6")
    byd = by_model(low, "Sealion 6")
    toyota = by_model(low, "Yaris Cross")
    assert vf6["ownership"]["result"]["normalizedMonthlyCost"] == 2_233_467
    assert byd["ownership"]["result"]["normalizedMonthlyCost"] == 2_373_789
    assert toyota["ownership"]["result"]["normalizedMonthlyCost"] == 3_068_804
    assert all(item["component"] != "LoanPayment" for item in vf6["ownership"]["result"]["breakdown"])
    assert_sources(vf6)

    high_rent = profile()
    high_rent["rentHousing"] = 8_000_000
    rent_result = post("/api/v1/affordability/evaluate", high_rent)
    assert len(rent_result["eligibleCars"]) == 0 and len(rent_result["overBudgetCars"]) == 3
    assert all("DISPOSABLE_RATIO_EXCEEDED" in item["evaluation"]["reasons"] for item in rent_result["overBudgetCars"])

    high_parking = profile()
    high_parking["expenses"]["parkingMonthly"] = 4_000_000
    parking_result = post("/api/v1/affordability/evaluate", high_parking)
    assert len(parking_result["eligibleCars"]) == 0
    assert by_model(parking_result, "VF 6")["ownership"]["result"]["normalizedMonthlyCost"] == 5_733_467
    assert "PARKING_DOMINATES" in by_model(parking_result, "VF 6")["evaluation"]["reasons"]

    public = profile()
    public["trimIds"] = [VF6_TRIM_ID]
    public["energy"]["homeChargingShare"] = 0
    public_result = post("/api/v1/affordability/evaluate", public)
    assert by_model(public_result, "VF 6")["ownership"]["result"]["normalizedMonthlyCost"] == 2_277_839
    assert by_model(public_result, "VF 6")["ownership"]["result"]["normalizedMonthlyCost"] > vf6["ownership"]["result"]["normalizedMonthlyCost"]

    capped = profile()
    capped["maximumMonthlyVehicleSpend"] = 2_300_000
    capped_result = post("/api/v1/affordability/evaluate", capped)
    assert [item["vehicle"]["modelName"] for item in capped_result["eligibleCars"]] == ["VF 6"]
    assert all("MAX_MONTHLY_VEHICLE_SPEND_EXCEEDED" in item["evaluation"]["reasons"] for item in capped_result["overBudgetCars"])

    promotion = profile()
    promotion["trimIds"] = [VF6_TRIM_ID]
    promotion["netMonthlyIncome"] = 10_000_000
    promotion["rentHousing"] = 0
    promotion["essentialExpenses"] = 9_000_000
    promotion["savingsTarget"] = 0
    for field in ("parkingMonthly", "maintenanceReserveMonthly", "tyreReserveMonthly"):
        promotion["expenses"][field] = 0
    promotion["energy"].update(
        {
            "homeChargingShare": 0,
            "purchaseDate": "2026-02-10",
            "promotionEligibilityConfirmed": True,
        }
    )
    promo_result = post("/api/v1/affordability/evaluate", promotion)
    promo_vf6 = by_model(promo_result, "VF 6")
    assert promo_vf6["ownership"]["result"]["currentMonthlyCost"] == 170_058
    assert promo_vf6["ownership"]["result"]["normalizedMonthlyCost"] == 727_839
    assert promo_vf6["evaluation"]["current"]["eligible"] is True
    assert promo_vf6["evaluation"]["normalized"]["eligible"] is False
    assert "NORMALIZED_COST_FAILS" in promo_vf6["evaluation"]["reasons"]
    assert "CURRENT_ENERGY_PROMOTION_APPLIED" in promo_vf6["evaluation"]["reasons"]

    html = subprocess.run(
        ["docker", "compose", "exec", "-T", "web", "wget", "-qO-", "http://127.0.0.1:3000/affordability"],
        check=True,
        capture_output=True,
    ).stdout.decode("utf-8").replace("<!-- -->", "")
    for expected in ("3 xe trong ngưỡng", "Ước tính kịch bản", "không phải lời khuyên tài chính", "VF 6", "Yaris Cross", "Advanced"):
        assert expected in html, expected

    rendered_code = "\n".join(
        path.read_text(encoding="utf-8")
        for path in [*Path("apps/web/app").rglob("*.tsx"), *Path("apps/web/features").rglob("*.tsx")]
    )
    assert "monthly_vehicle_cashflow /" not in rendered_code
    assert "loanPayment" not in rendered_code
    print("PASS V1.7: sourced ownership, salary policies, rent/parking/charging sensitivity, normalized promotion and explainable exclusions")


if __name__ == "__main__":
    main()
