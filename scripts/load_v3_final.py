#!/usr/bin/env python3
"""V3 final target-traffic load gate against the production Compose API image."""

from __future__ import annotations

import json
import math
import os
import time
import uuid
from concurrent.futures import ThreadPoolExecutor, as_completed
from datetime import datetime, timedelta, timezone
from typing import Any
from urllib.error import HTTPError
from urllib.parse import urlencode
from urllib.request import Request, urlopen


API = os.getenv("VCP_API_BASE", "http://127.0.0.1:8080")
ADMIN_EMAIL = os.getenv("ADMIN_BOOTSTRAP_EMAIL", "admin@vcp.local")
ADMIN_PASSWORD = os.getenv("ADMIN_BOOTSTRAP_PASSWORD", "vcp-admin-local-dev-only")
TARGET_RPS = int(os.getenv("VCP_V3_FINAL_TARGET_RPS", "20"))
DURATION_SECONDS = int(os.getenv("VCP_V3_FINAL_DURATION_SECONDS", "60"))
WORKERS = int(os.getenv("VCP_V3_FINAL_WORKERS", "32"))
POLICY_VERSION = "2026-08-24"
ROUTE_PATTERN = (
    ["catalog_search"] * 9
    + ["catalog_detail"] * 6
    + ["recommendation"]
    + ["partner_search"] * 2
    + ["partner_detail"] * 2
)
THRESHOLDS_MS = {
    "catalog_search": 300.0,
    "catalog_detail": 400.0,
    "recommendation": 700.0,
    "partner_search": 300.0,
    "partner_detail": 400.0,
}


def call(
    path: str,
    *,
    method: str = "GET",
    body: dict[str, Any] | None = None,
    admin_token: str | None = None,
    api_key: str | None = None,
    timeout: float = 30,
) -> tuple[int, object | None, dict[str, str]]:
    headers = {"Accept": "application/json"}
    if admin_token:
        headers["Authorization"] = f"Bearer {admin_token}"
    if api_key:
        headers["X-VCP-API-Key"] = api_key
    data = None
    if body is not None:
        data = json.dumps(body, separators=(",", ":")).encode("utf-8")
        headers["Content-Type"] = "application/json"
    request = Request(  # noqa: S310 - fixed local gate endpoint
        f"{API}{path}", data=data, headers=headers, method=method
    )
    try:
        with urlopen(request, timeout=timeout) as response:  # noqa: S310
            raw = response.read()
            payload = json.loads(raw) if raw else None
            return response.status, payload, {
                key.lower(): value for key, value in response.headers.items()
            }
    except HTTPError as error:
        raw = error.read()
        payload = json.loads(raw) if raw else None
        return error.code, payload, {
            key.lower(): value for key, value in error.headers.items()
        }


def recommendation_payload() -> dict[str, Any]:
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


def percentile(values: list[float], percent: int) -> float:
    ordered = sorted(values)
    index = max(0, math.ceil(len(ordered) * percent / 100) - 1)
    return round(ordered[index], 3)


def validate(route: str, payload: object | None) -> bool:
    if not isinstance(payload, dict):
        return False
    if route == "catalog_search":
        return isinstance(payload.get("data"), list) and bool(payload["data"])
    if route == "catalog_detail":
        return isinstance(payload.get("car"), dict) and bool(payload.get("primarySource"))
    if route == "recommendation":
        methodology = payload.get("methodology")
        return isinstance(methodology, dict) and methodology.get("version") == "v3.1-deterministic-1"
    if route == "partner_search":
        data = payload.get("data")
        meta = payload.get("meta")
        return (
            isinstance(data, dict)
            and isinstance(data.get("data"), list)
            and bool(data["data"])
            and isinstance(meta, dict)
            and meta.get("contractVersion") == "v1"
        )
    if route == "partner_detail":
        data = payload.get("data")
        meta = payload.get("meta")
        return (
            isinstance(data, dict)
            and isinstance(data.get("car"), dict)
            and bool(data.get("primarySource"))
            and isinstance(meta, dict)
            and meta.get("policyVersion") == POLICY_VERSION
        )
    return False


def measured_request(
    route: str,
    sequence: int,
    searches: list[str],
    trim_ids: list[str],
    api_key: str,
) -> tuple[str, float, int, bool, str | None]:
    query = searches[sequence % len(searches)]
    trim_id = trim_ids[sequence % len(trim_ids)]
    method = "GET"
    body = None
    key = None
    if route == "catalog_search":
        path = "/api/v1/cars?" + urlencode({"q": query, "pageSize": 20})
    elif route == "catalog_detail":
        path = f"/api/v1/cars/{trim_id}"
    elif route == "recommendation":
        path = "/api/v1/recommendations"
        method = "POST"
        body = recommendation_payload()
    elif route == "partner_search":
        path = "/api/v1/partner/cars?" + urlencode({"q": query, "pageSize": 20})
        key = api_key
    else:
        path = f"/api/v1/partner/cars/{trim_id}"
        key = api_key

    started = time.perf_counter()
    try:
        status, payload, _ = call(
            path, method=method, body=body, api_key=key, timeout=15
        )
        elapsed = (time.perf_counter() - started) * 1000
        valid = status == 200 and validate(route, payload)
        error = None if valid else f"status={status} code={payload.get('code') if isinstance(payload, dict) else 'invalid-json'}"
        return route, elapsed, status, valid, error
    except Exception as error:  # load gate records transport failures as failed requests
        elapsed = (time.perf_counter() - started) * 1000
        return route, elapsed, 0, False, type(error).__name__


def main() -> None:
    assert TARGET_RPS == 20, "The reviewed V3 target is exactly 20 requests/second"
    assert DURATION_SECONDS == 60, "The reviewed V3 soak duration is exactly 60 seconds"
    assert WORKERS >= 20, "Worker pool must not throttle the 20 requests/second target"
    assert len(ROUTE_PATTERN) == TARGET_RPS

    status, catalog, _ = call("/api/v1/cars?pageSize=5")
    assert status == 200 and isinstance(catalog, dict), catalog
    trim_ids = [str(item["trimId"]) for item in catalog["data"]]
    assert len(trim_ids) == 5
    searches = ["toyota", "vinfast", "yaris", "hybrid", "bmw"]

    status, login, _ = call(
        "/api/v1/admin/auth/login",
        method="POST",
        body={"email": ADMIN_EMAIL, "password": ADMIN_PASSWORD},
    )
    assert status == 200 and isinstance(login, dict) and login.get("role") == "Administrator"
    admin_token = str(login["token"])

    key_id: str | None = None
    api_key: str | None = None
    revoked = False
    try:
        status, issued, _ = call(
            "/api/v1/admin/partner-api/keys",
            method="POST",
            admin_token=admin_token,
            body={
                "name": f"V3 FINAL load gate {uuid.uuid4().hex[:10]}",
                "planCode": "standard",
                "policyVersion": POLICY_VERSION,
                "expiresAt": (datetime.now(timezone.utc) + timedelta(minutes=20)).isoformat(),
                "reason": "V3 FINAL gate load-tests the reviewed target traffic and revokes afterward.",
            },
        )
        assert status == 201 and isinstance(issued, dict), issued
        key_id = str(issued["key"]["id"])
        api_key = str(issued["apiKey"])

        # Warm every cache signature used by the measured phase. Warm-up is not
        # included in latency results and stays inside the standard usage plan.
        for query in searches:
            for path, key in (
                ("/api/v1/cars?" + urlencode({"q": query, "pageSize": 20}), None),
                ("/api/v1/partner/cars?" + urlencode({"q": query, "pageSize": 20}), api_key),
            ):
                warm_status, warm_payload, _ = call(path, api_key=key)
                assert warm_status == 200 and isinstance(warm_payload, dict), warm_payload
        for trim_id in trim_ids:
            for path, key in (
                (f"/api/v1/cars/{trim_id}", None),
                (f"/api/v1/partner/cars/{trim_id}", api_key),
            ):
                warm_status, warm_payload, _ = call(path, api_key=key)
                assert warm_status == 200 and isinstance(warm_payload, dict), warm_payload
        for _ in range(3):
            warm_status, warm_payload, _ = call(
                "/api/v1/recommendations", method="POST", body=recommendation_payload()
            )
            assert warm_status == 200 and isinstance(warm_payload, dict), warm_payload

        total_requests = TARGET_RPS * DURATION_SECONDS
        results: list[tuple[str, float, int, bool, str | None]] = []
        scheduled_at = time.perf_counter() + 0.5
        with ThreadPoolExecutor(max_workers=WORKERS) as executor:
            futures = []
            for sequence in range(total_requests):
                target = scheduled_at + sequence / TARGET_RPS
                delay = target - time.perf_counter()
                if delay > 0:
                    time.sleep(delay)
                route = ROUTE_PATTERN[sequence % len(ROUTE_PATTERN)]
                futures.append(executor.submit(
                    measured_request,
                    route,
                    sequence,
                    searches,
                    trim_ids,
                    api_key,
                ))
            for future in as_completed(futures):
                results.append(future.result())
        completed_at = time.perf_counter()

        failures = [result for result in results if not result[3]]
        assert not failures, failures[:10]
        assert len(results) == total_requests
        elapsed_seconds = completed_at - scheduled_at
        achieved_rps = total_requests / elapsed_seconds
        assert achieved_rps >= TARGET_RPS * 0.95, achieved_rps

        route_metrics: dict[str, dict[str, float | int]] = {}
        for route, threshold in THRESHOLDS_MS.items():
            latencies = [result[1] for result in results if result[0] == route]
            metrics = {
                "requests": len(latencies),
                "p50Ms": percentile(latencies, 50),
                "p95Ms": percentile(latencies, 95),
                "p99Ms": percentile(latencies, 99),
                "maxMs": round(max(latencies), 3),
                "targetP95Ms": int(threshold),
            }
            assert metrics["p95Ms"] < threshold, (route, metrics)
            route_metrics[route] = metrics

        status, credential, headers = call("/api/v1/partner/me", api_key=api_key)
        assert status == 200 and isinstance(credential, dict), credential
        assert credential["planCode"] == "standard"
        month_used = 500_000 - int(headers["x-ratelimit-month-remaining"])
        assert 251 <= month_used <= 260, month_used

        status, revoked_key, _ = call(
            f"/api/v1/admin/partner-api/keys/{key_id}/revoke",
            method="POST",
            admin_token=admin_token,
            body={"reason": "V3 FINAL load gate revokes its isolated credential after measurement."},
        )
        assert status == 200 and isinstance(revoked_key, dict)
        assert revoked_key["status"] == "Revoked"
        revoked = True

        print(json.dumps({
            "gate": "V3 FINAL target traffic",
            "status": "PASS",
            "target": {
                "requestsPerSecond": TARGET_RPS,
                "durationSeconds": DURATION_SECONDS,
                "workerPool": WORKERS,
                "totalRequests": total_requests,
                "mixPerSecond": {
                    "catalogSearch": 9,
                    "catalogDetail": 6,
                    "recommendation": 1,
                    "partnerSearch": 2,
                    "partnerDetail": 2,
                },
            },
            "achievedRequestsPerSecond": round(achieved_rps, 3),
            "httpErrors": 0,
            "invalidPayloads": 0,
            "routes": route_metrics,
            "partnerRequestsIncludingWarmupAndMe": month_used,
            "partnerCredentialRevoked": True,
            "externalProviderCalls": False,
        }, ensure_ascii=False, indent=2))
    finally:
        if key_id and api_key and not revoked:
            call(
                f"/api/v1/admin/partner-api/keys/{key_id}/revoke",
                method="POST",
                admin_token=admin_token,
                body={"reason": "V3 FINAL gate cleanup revokes its credential after a failed measurement."},
            )


if __name__ == "__main__":
    main()
