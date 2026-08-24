#!/usr/bin/env python3
"""V3.5 gate for the versioned, read-only public/partner API."""

from __future__ import annotations

import json
import os
import uuid
from datetime import datetime, timedelta, timezone
from urllib.error import HTTPError
from urllib.request import Request, urlopen


API = os.getenv("VCP_API_BASE", "http://127.0.0.1:8080")
ADMIN_EMAIL = os.getenv("ADMIN_BOOTSTRAP_EMAIL", "admin@vcp.local")
ADMIN_PASSWORD = os.getenv("ADMIN_BOOTSTRAP_PASSWORD", "vcp-admin-local-dev-only")
POLICY_VERSION = "2026-08-24"


def call(
    path: str,
    *,
    method: str = "GET",
    body: dict | None = None,
    admin_token: str | None = None,
    api_key: str | None = None,
) -> tuple[int, object | None, dict[str, str]]:
    headers = {"Accept": "application/json"}
    if admin_token:
        headers["Authorization"] = f"Bearer {admin_token}"
    if api_key:
        headers["X-VCP-API-Key"] = api_key
    data = None
    if body is not None:
        data = json.dumps(body, ensure_ascii=False).encode("utf-8")
        headers["Content-Type"] = "application/json"
    request = Request(  # noqa: S310 - the gate targets the configured local API only
        f"{API}{path}", data=data, headers=headers, method=method
    )
    try:
        with urlopen(request, timeout=60) as response:  # noqa: S310
            raw = response.read()
            payload = json.loads(raw) if raw else None
            return response.status, payload, {key.lower(): value for key, value in response.headers.items()}
    except HTTPError as error:
        raw = error.read()
        payload = json.loads(raw) if raw else None
        return error.code, payload, {key.lower(): value for key, value in error.headers.items()}


def require(status: int, expected: int, payload: object | None) -> None:
    assert status == expected, (status, expected, payload)


def assert_api_error(payload: object | None, code: str) -> None:
    assert isinstance(payload, dict), payload
    assert payload.get("code") == code, payload
    assert payload.get("message") and payload.get("traceId"), payload
    assert isinstance(payload.get("fieldErrors"), list), payload


def assert_policy_meta(payload: object | None) -> None:
    assert isinstance(payload, dict), payload
    meta = payload.get("meta")
    assert isinstance(meta, dict), payload
    assert meta["contractVersion"] == "v1"
    assert meta["policyVersion"] == POLICY_VERSION
    assert meta["license"] == "SOURCE-SPECIFIC"
    assert meta["attribution"]
    assert meta["policyPath"] == "/api/v1/partner/policy"


def main() -> None:
    status, policy, _ = call("/api/v1/partner/policy")
    require(status, 200, policy)
    assert isinstance(policy, dict)
    assert policy["contractVersion"] == "v1"
    assert policy["policyVersion"] == POLICY_VERSION
    assert policy["scope"] == "catalog.read"
    assert policy["license"] == "SOURCE-SPECIFIC"
    assert policy["attributionRequired"] is True and policy["attribution"]
    assert policy["policyDocument"] == "docs/api/data-attribution-policy.md"
    plans = {item["code"]: item for item in policy["usagePlans"]}
    assert set(plans) == {"sandbox", "standard"}, plans
    assert plans["sandbox"] == {
        "code": "sandbox",
        "name": "Sandbox read access",
        "requestsPerMinute": 30,
        "requestsPerMonth": 10_000,
        "maxPageSize": 25,
    }
    assert plans["standard"]["requestsPerMinute"] == 300
    assert plans["standard"]["requestsPerMonth"] == 500_000
    assert plans["standard"]["maxPageSize"] == 100

    status, missing, _ = call("/api/v1/partner/brands")
    require(status, 401, missing)
    assert_api_error(missing, "PARTNER_API_KEY_REQUIRED")

    status, login, _ = call(
        "/api/v1/admin/auth/login",
        method="POST",
        body={"email": ADMIN_EMAIL, "password": ADMIN_PASSWORD},
    )
    require(status, 200, login)
    assert isinstance(login, dict) and login["role"] == "Administrator"
    admin_token = login["token"]

    status, invalid_name, _ = call(
        "/api/v1/admin/partner-api/keys",
        method="POST",
        admin_token=admin_token,
        body={
            "name": None,
            "planCode": "sandbox",
            "policyVersion": POLICY_VERSION,
            "expiresAt": None,
            "reason": "V3.5 gate requires typed key issuance validation.",
        },
    )
    require(status, 400, invalid_name)
    assert_api_error(invalid_name, "PARTNER_API_KEY_NAME_INVALID")

    status, old_policy, _ = call(
        "/api/v1/admin/partner-api/keys",
        method="POST",
        admin_token=admin_token,
        body={
            "name": "V3.5 rejected old-policy key",
            "planCode": "sandbox",
            "policyVersion": "2026-01-01",
            "expiresAt": None,
            "reason": "V3.5 gate requires explicit current-policy acceptance.",
        },
    )
    require(status, 409, old_policy)
    assert_api_error(old_policy, "PARTNER_API_POLICY_NOT_ACCEPTED")

    key_id: str | None = None
    api_key: str | None = None
    revoked = False
    allowed_requests = 0
    try:
        status, issued, _ = call(
            "/api/v1/admin/partner-api/keys",
            method="POST",
            admin_token=admin_token,
            body={
                "name": f"V3.5 integration gate {uuid.uuid4().hex[:10]}",
                "planCode": "sandbox",
                "policyVersion": POLICY_VERSION,
                "expiresAt": (datetime.now(timezone.utc) + timedelta(minutes=15)).isoformat(),
                "reason": "V3.5 gate validates one-time issuance, rate enforcement and revocation.",
            },
        )
        require(status, 201, issued)
        assert isinstance(issued, dict) and isinstance(issued.get("key"), dict)
        key_id = issued["key"]["id"]
        api_key = issued["apiKey"]
        prefix = issued["key"]["keyPrefix"]
        assert len(api_key) == 61 and api_key.startswith(f"{prefix}.")
        assert issued["key"]["status"] == "Active"
        assert issued["key"]["scope"] == "catalog.read"
        assert issued["key"]["planCode"] == "sandbox"
        assert "keyHash" not in issued and "keyHash" not in issued["key"]

        status, keys, _ = call("/api/v1/admin/partner-api/keys", admin_token=admin_token)
        require(status, 200, keys)
        assert isinstance(keys, list)
        listed = next(item for item in keys if item["id"] == key_id)
        listed_text = json.dumps(listed, separators=(",", ":"))
        assert listed["keyPrefix"] == prefix and listed["status"] == "Active"
        assert api_key not in listed_text and "keyHash" not in listed_text and "apiKey" not in listed

        status, credential, headers = call("/api/v1/partner/me", api_key=api_key)
        require(status, 200, credential)
        allowed_requests += 1
        assert_policy_meta(credential)
        assert isinstance(credential, dict)
        assert credential["keyId"] == key_id and credential["keyPrefix"] == prefix
        assert credential["requestsPerMinute"] == 30
        assert headers["x-vcp-contract-version"] == "v1"
        assert headers["x-vcp-data-policy-version"] == POLICY_VERSION
        assert headers["cache-control"] == "private, no-store"
        assert headers["ratelimit-limit"] == "30"
        assert int(headers["ratelimit-remaining"]) == 29
        assert int(headers["x-ratelimit-month-remaining"]) == 9_999

        status, brands, _ = call("/api/v1/partner/brands", api_key=api_key)
        require(status, 200, brands)
        allowed_requests += 1
        assert_policy_meta(brands)
        assert isinstance(brands, dict) and brands["data"]["data"]

        status, cars, _ = call("/api/v1/partner/cars?pageSize=1", api_key=api_key)
        require(status, 200, cars)
        allowed_requests += 1
        assert_policy_meta(cars)
        assert isinstance(cars, dict) and len(cars["data"]["data"]) == 1
        trim_id = cars["data"]["data"][0]["trimId"]

        status, detail, _ = call(f"/api/v1/partner/cars/{trim_id}", api_key=api_key)
        require(status, 200, detail)
        allowed_requests += 1
        assert_policy_meta(detail)
        assert isinstance(detail, dict)
        primary_source = detail["data"]["primarySource"]
        assert primary_source["name"] and primary_source["url"].startswith("https://")
        assert len(primary_source["contentHash"]) == 64
        assert detail["data"]["prices"][0]["source"]["sourceId"]

        status, too_large, _ = call("/api/v1/partner/cars?pageSize=26", api_key=api_key)
        require(status, 403, too_large)
        assert_api_error(too_large, "PARTNER_API_PLAN_PAGE_SIZE_EXCEEDED")

        rate_headers: dict[str, str] | None = None
        for _ in range(70):
            status, rate_payload, current_headers = call("/api/v1/partner/me", api_key=api_key)
            if status == 429:
                rate_headers = current_headers
                assert_api_error(rate_payload, "PARTNER_API_RATE_LIMITED")
                break
            require(status, 200, rate_payload)
            allowed_requests += 1
        assert rate_headers is not None, "sandbox key did not reach the 30 requests/minute limit"
        assert rate_headers["ratelimit-limit"] == "30"
        assert rate_headers["ratelimit-remaining"] == "0"
        assert rate_headers["x-vcp-contract-version"] == "v1"
        assert rate_headers["x-vcp-data-policy-version"] == POLICY_VERSION
        assert rate_headers["cache-control"] == "private, no-store"
        assert int(rate_headers["retry-after"]) >= 1

        status, revoked_key, _ = call(
            f"/api/v1/admin/partner-api/keys/{key_id}/revoke",
            method="POST",
            admin_token=admin_token,
            body={"reason": "V3.5 gate revokes its isolated credential after verification."},
        )
        require(status, 200, revoked_key)
        assert isinstance(revoked_key, dict) and revoked_key["status"] == "Revoked"
        revoked = True

        status, rejected, _ = call("/api/v1/partner/me", api_key=api_key)
        require(status, 401, rejected)
        assert_api_error(rejected, "PARTNER_API_KEY_INVALID")

        status, audit, _ = call("/api/v1/admin/audit?take=500", admin_token=admin_token)
        require(status, 200, audit)
        assert isinstance(audit, list)
        key_events = [event for event in audit if event["entityId"] == key_id]
        assert {event["action"] for event in key_events} >= {
            "PartnerApiKeyIssued",
            "PartnerApiKeyRevoked",
        }
        audit_text = json.dumps(key_events, separators=(",", ":"))
        assert api_key not in audit_text and "keyHash" not in audit_text

        status, openapi, _ = call("/swagger/v1/swagger.json")
        require(status, 200, openapi)
        assert isinstance(openapi, dict)
        partner_paths = {
            path: operations
            for path, operations in openapi["paths"].items()
            if path.startswith("/api/v1/partner")
        }
        assert set(partner_paths) == {
            "/api/v1/partner/policy",
            "/api/v1/partner/me",
            "/api/v1/partner/brands",
            "/api/v1/partner/cars",
            "/api/v1/partner/cars/{trimId}",
        }
        assert all(set(operations) == {"get"} for operations in partner_paths.values())
        assert "security" not in partner_paths["/api/v1/partner/policy"]["get"]
        for path, operations in partner_paths.items():
            if path != "/api/v1/partner/policy":
                assert operations["get"]["security"] == [{"PartnerApiKey": []}]
        scheme = openapi["components"]["securitySchemes"]["PartnerApiKey"]
        assert scheme["type"] == "apiKey" and scheme["in"] == "header"
        assert scheme["name"] == "X-VCP-API-Key"
    finally:
        if key_id and api_key and not revoked:
            call(
                f"/api/v1/admin/partner-api/keys/{key_id}/revoke",
                method="POST",
                admin_token=admin_token,
                body={"reason": "V3.5 gate cleanup revokes its isolated credential after a failed assertion."},
            )

    print(json.dumps({
        "gate": "V3.5",
        "status": "PASS",
        "contractVersion": "v1",
        "policyVersion": POLICY_VERSION,
        "usagePlans": sorted(plans),
        "allowedRequestsBefore429": allowed_requests,
        "keyLifecycle": "issued-hashed-revoked",
        "plaintextKeyPrinted": False,
    }, separators=(",", ":")))


if __name__ == "__main__":
    main()
