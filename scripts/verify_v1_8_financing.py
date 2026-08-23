#!/usr/bin/env python3
"""V1.8 golden gate for purchase funding, loan maths and monthly commitment."""

from __future__ import annotations

import json
import subprocess
from pathlib import Path
from urllib.request import Request, urlopen


API = "http://127.0.0.1:8080"
VF6_TRIM_ID = "8b31de05-bd4c-5b70-9efd-47879f5e609c"


def post(body: dict) -> dict:
    request = Request(  # noqa: S310 - fixed localhost gate
        f"{API}/api/v1/financing/calculate",
        data=json.dumps(body).encode(),
        headers={"Content-Type": "application/json"},
        method="POST",
    )
    with urlopen(request, timeout=30) as response:  # noqa: S310 - fixed localhost gate
        assert response.status == 200
        return json.load(response)


def scenario() -> dict:
    return {
        "trimId": VF6_TRIM_ID,
        "provinceCode": "VN-01",
        "calculationDate": "2026-08-22T12:00:00+07:00",
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
        "purchase": {
            "fundingSource": "SelfFunded",
            "purchaseMethod": "Cash",
            "availableCash": 1_000_000_000,
            "familyContribution": 0,
            "tradeInNetValue": 0,
            "downPaymentAmount": None,
            "downPaymentPercent": None,
            "annualInterestRate": None,
            "interestRateSourceFactId": None,
            "termMonths": 0,
            "repaymentMethod": "Annuity",
            "bankFees": 0,
            "loanInsuranceUpfront": 0,
            "selectedDealerOfferIds": [],
        },
    }


def loan(acquisition: int, method: str) -> dict:
    body = scenario()
    down = acquisition - 600_000_000
    body["purchase"].update(
        {
            "purchaseMethod": "Loan",
            "availableCash": down,
            "downPaymentAmount": down,
            "annualInterestRate": 0.12,
            "termMonths": 60,
            "repaymentMethod": method,
        }
    )
    return body


def main() -> None:
    cash = post(scenario())
    acquisition = cash["financing"]["acquisitionCost"]
    assert acquisition == cash["onRoad"]["result"]["onRoadPrice"] == 662_040_700
    assert cash["financing"]["purchaseStatus"] == "Pass"
    assert cash["financing"]["financingStatus"] == "NotApplicable"
    assert cash["financing"]["loanPrincipal"] == cash["financing"]["monthlyPaymentForCommitment"] == 0
    assert cash["interestRate"]["origin"] == "NotApplicable"

    short = scenario()
    short["purchase"]["availableCash"] = acquisition - 1
    insufficient = post(short)
    assert insufficient["financing"]["purchaseStatus"] == "Fail"
    assert insufficient["financing"]["cashShortfall"] == 1
    assert insufficient["financing"]["monthlyPaymentForCommitment"] == 0

    annuity = post(loan(acquisition, "Annuity"))
    annuity_financing = annuity["financing"]
    assert annuity_financing["loanPrincipal"] == 600_000_000
    assert annuity_financing["firstPayment"] == annuity_financing["averagePayment"] == 13_346_669
    assert annuity_financing["monthlyPaymentForCommitment"] == 13_346_669
    assert annuity_financing["totalInterest"] == 200_800_117
    assert annuity_financing["totalLoanRepayment"] == 800_800_117
    assert annuity["interestRate"]["origin"] == "UserInput" and annuity["interestRate"]["source"] is None
    assert annuity["appliedDealerCredits"] == [] and annuity_financing["otherUpfrontCredits"] == 0
    assert annuity["purchaseCashflow"]["totalMonthlyVehicleCommitment"] == (
        annuity["ownership"]["result"]["normalizedMonthlyCost"] + annuity_financing["monthlyPaymentForCommitment"]
    )
    assert annuity["purchaseCashflow"]["vehicleDebtRatio"] == round(13_346_669 / 50_000_000, 6)

    reducing = post(loan(acquisition, "ReducingBalance"))
    reducing_financing = reducing["financing"]
    assert reducing_financing["firstPayment"] == 16_000_000
    assert reducing_financing["averagePayment"] == 13_050_000
    assert reducing_financing["lastPayment"] == 10_100_000
    assert reducing_financing["monthlyPaymentForCommitment"] == reducing_financing["firstPayment"]
    assert reducing_financing["totalInterest"] == 183_000_000
    assert reducing_financing["totalLoanRepayment"] == 783_000_000

    family = scenario()
    family.update({"netMonthlyIncome": 5_000_000, "rentHousing": 1_000_000, "essentialExpenses": 3_000_000, "savingsTarget": 0})
    family["purchase"].update(
        {
            "fundingSource": "FamilyFunded",
            "availableCash": 0,
            "familyContribution": acquisition,
        }
    )
    external = post(family)
    assert external["purchaseRating"] == "ExternallyFunded"
    assert external["financing"]["purchaseStatus"] == "ExternallyFunded"
    assert external["financing"]["financingStatus"] == "NotApplicable"
    assert external["financing"]["upfrontCashRequired"] == external["financing"]["monthlyPaymentForCommitment"] == 0
    assert external["ownershipAffordability"]["eligible"] is False
    assert external["purchaseCashflow"]["totalMonthlyVehicleCommitment"] == external["ownership"]["result"]["normalizedMonthlyCost"]

    html = subprocess.run(
        ["docker", "compose", "exec", "-T", "web", "wget", "-qO-", "http://127.0.0.1:3000/financing"],
        check=True,
        capture_output=True,
    ).stdout.decode("utf-8").replace("<!-- -->", "")
    for expected in (
        "Mua được, vay được và nuôi được",
        "Trong ngưỡng",
        "Không phải phê duyệt tín dụng",
        "VehicleDebtRatio",
        "Tổng cam kết xe",
        "User input · chưa phải báo giá",
        "không tự tạo bonus",
    ):
        assert expected.lower() in html.lower(), expected

    client = Path("apps/web/features/financing/financing-workbench.tsx").read_text(encoding="utf-8")
    assert 'fetch("/api/financing", { method: "POST"' in client
    assert "URLSearchParams" not in client
    assert "interestRateSourceFactId: null" in client
    print("PASS V1.8: cash/family separation, annuity/reducing goldens, sourced rate semantics, dealer eligibility and ownership+payment commitment")


if __name__ == "__main__":
    main()
