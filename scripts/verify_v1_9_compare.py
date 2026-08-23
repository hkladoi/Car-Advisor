#!/usr/bin/env python3
"""V1.9 golden gate for trim comparison and share-safe scenarios."""

from __future__ import annotations

import copy
import json
import subprocess
from pathlib import Path
from urllib.error import HTTPError
from urllib.request import Request, urlopen


API = "http://127.0.0.1:8080"
VF6 = "8b31de05-bd4c-5b70-9efd-47879f5e609c"
BYD_SEALION_6 = "13bb54aa-f730-5a7a-a12d-9050aa0e58fd"


def request(body: dict) -> tuple[int, dict]:
    call = Request(  # noqa: S310 - fixed localhost gate
        f"{API}/api/v1/compare/calculate",
        data=json.dumps(body).encode(),
        headers={"Content-Type": "application/json"},
        method="POST",
    )
    try:
        with urlopen(call, timeout=60) as response:  # noqa: S310 - fixed localhost gate
            return response.status, json.load(response)
    except HTTPError as error:
        return error.code, json.load(error)


def scenario() -> dict:
    return {
        "trimIds": [VF6, BYD_SEALION_6],
        "provinceCode": "VN-01",
        "calculationDate": "2026-08-22T12:00:00+07:00",
        "profilePreset": "city-balanced",
        "financingPreset": "standard-loan",
        "policy": "Balanced",
        "netMonthlyIncome": 50_000_000,
        "rentHousing": 8_000_000,
        "essentialExpenses": 8_000_000,
        "otherFixedDebt": 0,
        "savingsTarget": 3_000_000,
        "maximumMonthlyVehicleSpend": None,
        "expenses": {
            "monthlyKilometres": 1_000,
            "parkingMonthly": 1_200_000,
            "maintenanceReserveMonthly": 1_000_000,
            "bodyInsuranceAnnual": 0,
            "tyreReserveMonthly": 300_000,
            "batteryRentalMonthly": 0,
            "compulsoryInsuranceMonthlyOverride": None,
            "roadUsageMonthlyOverride": None,
            "inspectionMonthlyOverride": None,
            "firstInspectionExempt": True,
        },
        "energy": {
            "fuelType": "E10Ron95III",
            "evShare": 0.5,
            "homeChargingShare": 0.7,
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
        "purchase": {
            "fundingSource": "SelfFunded",
            "purchaseMethod": "Loan",
            "availableCash": 600_000_000,
            "familyContribution": 0,
            "tradeInNetValue": 0,
            "downPaymentAmount": None,
            "downPaymentPercent": 0.2,
            "annualInterestRate": 0.12,
            "interestRateSourceFactId": None,
            "termMonths": 60,
            "repaymentMethod": "Annuity",
            "bankFees": 0,
            "loanInsuranceUpfront": 0,
            "selectedDealerOfferIds": [],
        },
    }


def rows(payload: dict) -> dict[str, dict]:
    return {row["code"]: row for row in payload["rows"]}


def numbers(row: dict) -> list[int | float | None]:
    return [cell["numericValue"] for cell in row["cells"]]


def main() -> None:
    status, baseline = request(scenario())
    assert status == 200
    assert len(baseline["vehicles"]) == 2
    assert [vehicle["trimId"] for vehicle in baseline["vehicles"]] == [VF6, BYD_SEALION_6]
    assert baseline["scenario"]["provinceCode"] == "VN-01"
    assert baseline["scenario"]["currency"] == "VND"

    base = rows(baseline)
    required_units = {
        "msrp": "VND",
        "promotion_price": "VND",
        "dealer_cash_price": "VND",
        "current_cash_price": "VND",
        "on_road": "VND",
        "upfront_cash": "VND",
        "installment": "VND/tháng",
        "ownership_current": "VND/tháng",
        "ownership_normalized": "VND/tháng",
        "total_monthly_commitment": "VND/tháng",
    }
    for code, unit in required_units.items():
        assert base[code]["canonicalUnit"] == unit, (code, base[code]["canonicalUnit"])
        assert len(base[code]["cells"]) == 2

    assert numbers(base["msrp"]) == [646_000_000, 839_000_000]
    assert numbers(base["on_road"]) == [662_040_700, 839_000_000]
    assert numbers(base["upfront_cash"]) == [132_408_140, 167_800_000]
    assert numbers(base["installment"]) == [11_781_384, 14_930_473]
    assert numbers(base["ownership_normalized"]) == [3_190_971, 3_366_458]
    assert numbers(base["total_monthly_commitment"]) == [14_972_355, 18_296_931]
    assert all(cell["state"] == "Unknown" for cell in base["promotion_price"]["cells"])
    assert base["promotion_price"]["different"] is False
    assert all(cell["sources"] for cell in base["msrp"]["cells"])
    assert all(cell["sources"] for cell in base["on_road"]["cells"])

    seat_row = next(row for row in baseline["rows"] if row["label"] == "Số chỗ ngồi")
    assert seat_row["canonicalUnit"] is None
    assert [cell["state"] for cell in seat_row["cells"]] == ["Official", "Unknown"]
    assert seat_row["different"] is True
    wheelbase = next(row for row in baseline["rows"] if row["label"] == "Chiều dài cơ sở")
    assert wheelbase["canonicalUnit"] == "mm"

    high_use = copy.deepcopy(scenario())
    high_use.update({"profilePreset": "high-mileage-public"})
    high_use["expenses"].update(
        {"monthlyKilometres": 2_500, "parkingMonthly": 2_000_000, "maintenanceReserveMonthly": 1_500_000}
    )
    high_use["energy"]["homeChargingShare"] = 0
    status, high_use_result = request(high_use)
    assert status == 200
    high = rows(high_use_result)
    assert numbers(high["msrp"]) == numbers(base["msrp"])
    assert numbers(high["on_road"]) == numbers(base["on_road"])
    assert numbers(high["installment"]) == numbers(base["installment"])
    assert numbers(high["ownership_normalized"]) != numbers(base["ownership_normalized"])
    assert numbers(high["total_monthly_commitment"]) != numbers(base["total_monthly_commitment"])

    region_two = copy.deepcopy(scenario())
    region_two["provinceCode"] = "VN-48"
    status, region_result = request(region_two)
    assert status == 200
    region = rows(region_result)
    assert numbers(region["msrp"]) == numbers(base["msrp"])
    assert numbers(region["on_road"]) != numbers(base["on_road"])
    assert numbers(region["upfront_cash"]) != numbers(base["upfront_cash"])
    assert numbers(region["installment"]) != numbers(base["installment"])

    reducing = copy.deepcopy(scenario())
    reducing.update({"financingPreset": "short-reducing"})
    reducing["purchase"].update(
        {"availableCash": 800_000_000, "downPaymentPercent": 0.3, "annualInterestRate": 0.1, "termMonths": 36, "repaymentMethod": "ReducingBalance"}
    )
    status, reducing_result = request(reducing)
    assert status == 200
    reduced = rows(reducing_result)
    assert numbers(reduced["msrp"]) == numbers(base["msrp"])
    assert numbers(reduced["on_road"]) == numbers(base["on_road"])
    assert numbers(reduced["installment"]) != numbers(base["installment"])

    for trim_ids in ([VF6], [VF6, VF6], [VF6, BYD_SEALION_6, VF6, BYD_SEALION_6, VF6]):
        invalid = scenario()
        invalid["trimIds"] = trim_ids
        status, error = request(invalid)
        assert status == 400 and error["code"] == "COMPARE_INPUT_INVALID"

    html = subprocess.run(
        ["docker", "compose", "exec", "-T", "web", "wget", "-qO-", "http://127.0.0.1:3000/compare"],
        check=True,
        capture_output=True,
    ).stdout.decode("utf-8").replace("<!-- -->", "")
    for expected in (
        "2–4 trims",
        "Chỉ hiện khác biệt",
        "Chia sẻ URL",
        "UNKNOWN",
        "Giá ra biển",
        "Chi phí sở hữu chuẩn hóa",
        "VND/tháng",
    ):
        assert expected.lower() in html.lower(), expected

    catalog = subprocess.run(
        ["docker", "compose", "exec", "-T", "web", "wget", "-qO-", "http://127.0.0.1:3000/cars?Powertrain=Bev"],
        check=True,
        capture_output=True,
    ).stdout.decode("utf-8")
    assert "/compare?trims=" in catalog and "So sánh" in catalog

    client = Path("apps/web/features/compare/compare-workbench.tsx").read_text(encoding="utf-8")
    assert "new URLSearchParams" in client
    for sensitive in ("netMonthlyIncome", "availableCash", "otherFixedDebt", "annualInterestRate"):
        assert sensitive not in client
    assert "profile: String(data.get(\"profile\"))" in client
    assert "financing: String(data.get(\"financing\"))" in client
    print("PASS V1.9: 2-4 trim matrix, canonical units, explicit unknowns, scenario recomputation, catalog entry and share-safe URL")


if __name__ == "__main__":
    main()
