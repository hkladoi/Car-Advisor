#!/usr/bin/env python3
"""V3.1 gate: deterministic, explainable recommendation on the live stack."""

from __future__ import annotations

import json
import subprocess
from pathlib import Path
from typing import Any
from urllib.error import HTTPError
from urllib.request import Request, urlopen


ROOT = Path(__file__).resolve().parents[1]
API = "http://127.0.0.1:8080"
WEB = "http://127.0.0.1:3000"


def command(*args: str) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        args,
        cwd=ROOT,
        check=True,
        capture_output=True,
        text=True,
        encoding="utf-8",
    )


def require(condition: bool, message: str) -> None:
    if not condition:
        raise AssertionError(message)


def post(path: str, payload: dict[str, Any]) -> tuple[int, Any]:
    request = Request(  # noqa: S310 - fixed local gate URL
        f"{API}{path}",
        data=json.dumps(payload).encode("utf-8"),
        method="POST",
        headers={"Accept": "application/json", "Content-Type": "application/json"},
    )
    try:
        with urlopen(request, timeout=60) as response:  # noqa: S310 - fixed local gate URL
            return response.status, json.loads(response.read())
    except HTTPError as error:
        return error.code, json.loads(error.read())


def get_html(path: str) -> tuple[int, str]:
    with urlopen(Request(f"{WEB}{path}"), timeout=60) as response:  # noqa: S310 - fixed local gate URL
        return response.status, response.read().decode("utf-8")


def default_payload() -> dict[str, Any]:
    return {
        "hardFilters": {
            "maximumPrice": 1_500_000_000,
            "bodyTypes": [],
            "segments": [],
            "powertrains": [],
            "minimumSeats": 5,
            "requiredFeatureCodes": [],
        },
        "weights": {
            "priceValue": 20,
            "runningCost": 15,
            "space": 15,
            "safetyAdas": 20,
            "comfort": 10,
            "performance": 10,
            "technology": 10,
        },
        "regionCode": "VN-01",
        "maximumResults": 10,
    }


def stable(response: dict[str, Any]) -> dict[str, Any]:
    return {key: value for key, value in response.items() if key != "evaluatedAt"}


def main() -> None:
    services = [json.loads(line) for line in command("docker", "compose", "ps", "--format", "json").stdout.splitlines() if line.strip()]
    by_service = {item["Service"]: item for item in services}
    required_services = {"postgres", "redis", "minio", "api", "web", "ingestion-worker", "ingestion-scheduler"}
    require(required_services <= by_service.keys(), "Compose stack is incomplete")
    require(all(by_service[name]["State"] == "running" and by_service[name]["Health"] == "healthy" for name in required_services), "Compose stack is not healthy")

    status, response = post("/api/v1/recommendations", default_payload())
    require(status == 200 and isinstance(response, dict), "recommendation endpoint failed")
    status_again, repeat = post("/api/v1/recommendations", default_payload())
    require(status_again == 200 and stable(response) == stable(repeat), "same inputs did not reproduce the same recommendation result")

    methodology = response["methodology"]
    require(methodology["version"] == "v3.1-deterministic-1", "methodology is not explicitly versioned")
    require(methodology["evaluationOrder"][:3] == ["hard_filters", "component_completeness", "source_trust"], "gate order changed")
    require(float(methodology["completenessThreshold"]) >= 0.80, "public completeness threshold was weakened")
    require(abs(sum(float(value) for value in methodology["normalizedWeights"].values()) - 1.0) < 0.00001, "weights are not normalized")
    require("no LLM" in " ".join(methodology["assumptions"]), "deterministic/no-LLM methodology disclosure is missing")
    require("price_performance" in methodology["pricePerformanceFormula"], "P/P formula is missing")

    all_candidates = response["ranked"] + response["dataWithheld"] + response["hardFilterExcluded"]
    require(len(all_candidates) == response["considered"] == 49, "candidate accounting is not closed")
    require(len(response["ranked"]) + len(response["dataWithheld"]) == response["hardFilterMatched"], "hard-filter accounting is inconsistent")
    for candidate in response["ranked"]:
        require(candidate["completenessPassed"] and candidate["trustPassed"], "ranked candidate bypassed a gate")
        require(candidate["overallScore"] is not None and candidate["pricePerformanceScore"] is not None, "ranked score or P/P is absent")
        require(len(candidate["components"]) == 7 and all(component["rawMetrics"] for component in candidate["components"] if component["includedInOverall"]), "ranked explanation is incomplete")
    for candidate in response["dataWithheld"]:
        require(candidate["overallScore"] is None and candidate["pricePerformanceScore"] is None, "withheld candidate received a score")
        require(candidate["reasons"], "withheld candidate has no actionable reason")
        require(len(candidate["components"]) == 7, "withheld explanation does not show all seven components")
    for candidate in response["hardFilterExcluded"]:
        require(candidate["overallScore"] is None and candidate["rank"] is None, "hard-filter exclusion was scored")
        require(any(reason.startswith("HARD_FILTER_") for reason in candidate["reasons"]), "hard-filter exclusion has no hard-filter reason")

    impossible = default_payload()
    impossible["hardFilters"]["maximumPrice"] = 1
    status, filtered = post("/api/v1/recommendations", impossible)
    require(status == 200 and filtered["hardFilterMatched"] == 0, "strict price hard filter did not run first")
    require(not filtered["ranked"] and not filtered["dataWithheld"], "filtered candidates entered completeness/scoring")

    invalid = default_payload()
    invalid["weights"] = {key: 0 for key in invalid["weights"]}
    status, error = post("/api/v1/recommendations", invalid)
    require(status == 400 and error["code"] == "RECOMMENDATION_WEIGHTS_INVALID", "invalid weights did not fail explicitly")

    status, html = get_html("/recommend")
    require(status == 200, "recommendation page failed")
    html = html.replace("<!-- -->", "")
    for marker in ("Lọc trước. Chấm sau.", "Hard filters", "Chưa có trim đủ bằng chứng", "P/P chưa phát hành"):
        require(marker in html, f"recommendation page is missing: {marker}")

    openapi = json.loads((ROOT / "packages/contracts/openapi/v1.json").read_text(encoding="utf-8"))
    require("/api/v1/recommendations" in openapi["paths"], "versioned OpenAPI contract is missing recommendation")
    service = (ROOT / "apps/api/src/Api/Features/Recommendation/RecommendationService.cs").read_text(encoding="utf-8")
    require("HttpClient" not in service and "Brave" not in service and "Playwright" not in service, "user request path contains an external provider dependency")

    print(json.dumps({
        "status": "PASS",
        "methodology": methodology["version"],
        "considered": response["considered"],
        "hardFilterMatched": response["hardFilterMatched"],
        "ranked": len(response["ranked"]),
        "dataWithheld": len(response["dataWithheld"]),
        "hardFilterExcluded": len(response["hardFilterExcluded"]),
        "deterministicRepeat": True,
        "strictFilterSimulation": True,
        "invalidWeightsBlocked": True,
        "webVisible": True,
        "openApiPublished": True,
    }, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
