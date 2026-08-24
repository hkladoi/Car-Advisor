from __future__ import annotations

import json
import math
import os
import statistics
import time
import urllib.error
import urllib.parse
import urllib.request


BASE_URL = os.environ.get("API_BASE_URL", "http://localhost:8080").rstrip("/")


def get(path: str, expected_status: int = 200) -> dict:
    request = urllib.request.Request(f"{BASE_URL}{path}", headers={"Accept": "application/json"})
    try:
        with urllib.request.urlopen(request, timeout=5) as response:
            if response.status != expected_status:
                raise AssertionError(f"{path}: expected {expected_status}, got {response.status}")
            return json.load(response)
    except urllib.error.HTTPError as error:
        if error.code != expected_status:
            raise AssertionError(f"{path}: expected {expected_status}, got {error.code}") from error
        return json.loads(error.read())


def cars(**parameters: str | int) -> dict:
    return get(f"/api/v1/cars?{urllib.parse.urlencode(parameters)}")


def assert_search(query: str, expected_model: str) -> dict:
    response = cars(q=query, pageSize=5)
    assert response["pagination"]["totalItems"] >= 1, query
    assert response["data"][0]["modelName"] == expected_model, response["data"]
    return response["data"][0]


def main() -> None:
    deadline = time.monotonic() + 20
    brands = get("/api/v1/brands")
    while len(brands["data"]) < 11 and time.monotonic() < deadline:
        time.sleep(0.2)
        brands = get("/api/v1/brands")
    assert len(brands["data"]) == 11, brands

    ex5 = assert_search("ex5", "EX5")
    assert ex5["currentPrice"]["amount"] == 839_000_000
    assert_search("vf6", "VF 6")
    tucson = assert_search("tucson hybrid", "Tucson")
    assert tucson["powertrainType"] == "Ice"
    assert tucson["currentPrice"] is None

    and_hit = cars(features="CAMERA_360,PANORAMIC_ROOF", featureMode="and")
    assert [car["modelName"] for car in and_hit["data"]] == ["EX5"]
    and_miss = cars(features="CAMERA_360,AEB", featureMode="and")
    assert and_miss["pagination"]["totalItems"] == 0
    or_hit = cars(features="PANORAMIC_ROOF,AEB", featureMode="or")
    assert or_hit["pagination"]["totalItems"] >= 1
    assert or_hit["featureFilterSemantics"].startswith("OR:")

    geely_bev = cars(brand="geely", powertrain="bev", lengthMin=4_600_000 / 1000)
    assert [car["modelName"] for car in geely_bev["data"]] == ["EX5"]
    no_fabricated_on_road = cars(onRoadMin=1)
    assert no_fabricated_on_road["pagination"]["totalItems"] == 0
    invalid = get("/api/v1/cars?featureMode=xor", expected_status=400)
    assert invalid["code"] == "CATALOG_FILTER_INVALID"
    assert invalid["fieldErrors"] and invalid["traceId"]

    warm_path = "/api/v1/cars?q=ex5&features=CAMERA_360&pageSize=24"
    get(warm_path)
    durations_ms: list[float] = []
    for _ in range(100):
        started = time.perf_counter()
        get(warm_path)
        durations_ms.append((time.perf_counter() - started) * 1000)
    ordered = sorted(durations_ms)
    p95 = ordered[math.ceil(len(ordered) * 0.95) - 1]
    assert p95 < 300, f"warm catalog p95 {p95:.2f}ms is above 300ms"

    print(
        json.dumps(
            {
                "brands": len(brands["data"]),
                "queries": ["ex5", "vf6", "tucson hybrid"],
                "feature_and_or": "PASS",
                "warm_requests": len(durations_ms),
                "warm_p50_ms": round(statistics.median(durations_ms), 2),
                "warm_p95_ms": round(p95, 2),
                "result": "V1.3 catalog gate: PASS",
            },
            ensure_ascii=False,
        )
    )


if __name__ == "__main__":
    main()
