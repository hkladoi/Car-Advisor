#!/usr/bin/env python3
"""V1.10 gate for authenticated administration, QA, coverage and audit controls."""

from __future__ import annotations

import json
import os
import time
import uuid
from datetime import datetime, timedelta, timezone
from http.cookiejar import CookieJar
from urllib.error import HTTPError
from urllib.request import HTTPCookieProcessor, Request, build_opener, urlopen


API = os.getenv("VCP_API_BASE", "http://127.0.0.1:8080")
WEB = os.getenv("VCP_WEB_BASE", "http://127.0.0.1:3000")
ADMIN_EMAIL = os.getenv("ADMIN_BOOTSTRAP_EMAIL", "admin@vcp.local")
ADMIN_PASSWORD = os.getenv("ADMIN_BOOTSTRAP_PASSWORD", "vcp-admin-local-dev-only")
VF6 = "8b31de05-bd4c-5b70-9efd-47879f5e609c"


def call(
    path: str,
    *,
    method: str = "GET",
    body: dict | None = None,
    token: str | None = None,
    base: str = API,
    opener=None,
    headers: dict[str, str] | None = None,
) -> tuple[int, object | None, dict[str, str]]:
    request_headers = {"Accept": "application/json", **(headers or {})}
    if token:
        request_headers["Authorization"] = f"Bearer {token}"
    data = None
    if body is not None:
        data = json.dumps(body, ensure_ascii=False).encode("utf-8")
        request_headers["Content-Type"] = "application/json"
    request = Request(  # noqa: S310 - fixed local gate endpoints
        f"{base}{path}", data=data, headers=request_headers, method=method
    )
    transport = opener.open if opener is not None else urlopen
    try:
        with transport(request, timeout=60) as response:  # noqa: S310 - fixed local gate endpoints
            raw = response.read()
            payload = json.loads(raw) if raw else None
            return response.status, payload, dict(response.headers.items())
    except HTTPError as error:
        raw = error.read()
        payload = json.loads(raw) if raw else None
        return error.code, payload, dict(error.headers.items())


def require(status: int, expected: int, payload: object | None) -> None:
    assert status == expected, (status, expected, payload)


def main() -> None:
    status, _, _ = call("/api/v1/admin/coverage")
    require(status, 401, None)

    status, login, _ = call(
        "/api/v1/admin/auth/login",
        method="POST",
        body={"email": ADMIN_EMAIL, "password": ADMIN_PASSWORD},
    )
    require(status, 200, login)
    assert isinstance(login, dict) and login["role"] == "Administrator"
    token = login["token"]

    status, session, _ = call("/api/v1/admin/auth/session", token=token)
    require(status, 200, session)
    assert isinstance(session, dict) and session["email"] == ADMIN_EMAIL
    assert session["role"] == "Administrator" and "password" not in session

    status, coverage, _ = call("/api/v1/admin/coverage", token=token)
    require(status, 200, coverage)
    assert isinstance(coverage, dict)
    assert coverage["activeTrimCount"] >= 12 and coverage["brandScopeCount"] >= 11
    assert 0 <= coverage["coreCompleteness"] <= 1
    assert 0 <= coverage["freshness"] <= 1
    assert coverage["fullMarketGatePassed"] is False
    assert "BRAND_SCOPE_BELOW_INITIAL_VALIDATION_TARGET" in coverage["gateFailures"]
    assert len(coverage["brands"]) == coverage["brandScopeCount"]
    for brand in coverage["brands"]:
        for field in ("discovered", "mapped", "published", "blocked", "stale", "completeness", "freshness"):
            assert field in brand, (brand.get("brandName"), field)

    status, quality, _ = call("/api/v1/admin/quality", token=token)
    require(status, 200, quality)
    assert isinstance(quality, dict)
    for field in (
        "impossibleValues", "duplicates", "staleSources", "missingCoreFields",
        "sourceConflicts", "dealerOfferIssues",
    ):
        assert isinstance(quality[field], int) and quality[field] >= 0, field
    for issue in quality["issues"]:
        assert issue["code"] and issue["severity"] and issue["entityType"] and issue["fieldPath"]

    status, sources, _ = call("/api/v1/admin/sources", token=token)
    require(status, 200, sources)
    assert isinstance(sources, list) and sources
    current_sources = [source for source in sources if source["active"] and not source["stale"] and source["snapshotCount"] > 0]
    assert current_sources
    assert all(source["url"].startswith("https://") for source in sources)
    assert all(source["authorityLevel"] and source["contentType"] for source in sources)

    # Exercise typed source update without changing reviewed registry content.
    source = current_sources[0]
    same_source = {
        "name": source["name"],
        "url": source["url"],
        "authorityLevel": source["authorityLevel"],
        "contentType": source["contentType"],
        "robotsNote": source.get("robotsNote"),
        "termsNote": source.get("termsNote"),
        "active": source["active"],
        "priority": source["priority"],
        "refreshIntervalHours": source["refreshIntervalHours"],
        "reason": "V1.10 gate verifies typed source registry update and audit provenance.",
    }
    status, updated_source, _ = call(
        f"/api/v1/admin/sources/{source['id']}", method="PUT", body=same_source, token=token
    )
    require(status, 200, updated_source)
    assert isinstance(updated_source, dict) and updated_source["url"] == source["url"]

    # A rejected import demonstrates that impossible, duplicate and unregistered data cannot be staged.
    invalid_record = {
        "brand_name": "Rejected gate row",
        "brand_slug": "rejected-gate-row",
        "model_name": "Rejected",
        "model_slug": "rejected",
        "generation_code": "invalid",
        "model_year": 1800,
        "trim_name": "Rejected",
        "trim_slug": "rejected",
        "source_url": "http://invalid.local/not-publishable",
        "body_type": "Suv",
        "segment": "B",
        "market_status": "Active",
        "powertrain": "Warp",
        "price_type": "Msrp",
        "msrp_amount": -1,
    }
    import_body = {
        "fileName": "v1.10-rejected-gate.json",
        "content": json.dumps([invalid_record, invalid_record], ensure_ascii=False),
        "reason": "V1.10 gate proves invalid manual data remains outside the review and publish pipeline.",
    }
    status, invalid_import, _ = call(
        "/api/v1/admin/imports/validate", method="POST", body=import_body, token=token
    )
    require(status, 200, invalid_import)
    assert isinstance(invalid_import, dict) and invalid_import["status"] == "Invalid"
    codes = {issue["code"] for issue in invalid_import["issues"]}
    assert {"IMPOSSIBLE_VALUE", "CANONICAL_VALUE_REQUIRED", "DUPLICATE_TRIM_IDENTITY", "SOURCE_NOT_REGISTERED"} <= codes
    status, stage_error, _ = call(
        f"/api/v1/admin/imports/{invalid_import['id']}/stage",
        method="POST",
        body={"reason": "V1.10 gate confirms an invalid import cannot be staged for review."},
        token=token,
    )
    require(status, 409, stage_error)
    assert isinstance(stage_error, dict) and stage_error["code"] == "ADMIN_IMPORT_NOT_VALIDATED"

    # Positive CRUD is confined to an unsourced draft under an existing hierarchy and removed in finally.
    draft_id: str | None = None
    draft_slug = f"v110-gate-{uuid.uuid4().hex[:10]}"
    draft = {
        "brandName": "VinFast", "brandSlug": "vinfast", "brandCountryCode": "VN",
        "brandOfficialUrl": "https://vinfastauto.com/", "modelName": "VF 6", "modelSlug": "vf-6",
        "bodyType": "Suv", "segment": "B", "generationCode": "VF6-1",
        "generationStartYear": 2024, "modelYear": 2026, "trimName": "V1.10 gate draft",
        "trimSlug": draft_slug, "marketStatus": "Upcoming",
        "reason": "V1.10 gate creates an isolated unsourced draft to verify safe catalog CRUD.",
    }
    lock_id: str | None = None
    original_name: str | None = None
    try:
        status, created, _ = call("/api/v1/admin/catalog/trims", method="POST", body=draft, token=token)
        require(status, 201, created)
        assert isinstance(created, dict) and created["slug"] == draft_slug
        draft_id = created["trimId"]
        status, changed, _ = call(
            f"/api/v1/admin/catalog/trims/{draft_id}",
            method="PUT",
            body={
                "name": "V1.10 gate draft reviewed", "slug": draft_slug, "marketStatus": "Unknown",
                "launchedAt": None, "discontinuedAt": None,
                "reason": "V1.10 gate verifies typed draft update before safe deletion.",
            },
            token=token,
        )
        require(status, 200, changed)
        assert isinstance(changed, dict) and changed["marketStatus"] == "Unknown"

        # Prime public detail cache, mutate a real field, and require immediate cache invalidation.
        status, detail, _ = call(f"/api/v1/cars/{VF6}")
        require(status, 200, detail)
        assert isinstance(detail, dict)
        original_name = detail["car"]["trimName"]
        temporary_name = f"{original_name} [V1.10 cache gate]"
        status, field_lock, _ = call(
            "/api/v1/admin/overrides",
            method="POST",
            body={
                "entityType": "Trim", "entityId": VF6, "fieldPath": "name", "newValue": temporary_name,
                "reason": "V1.10 gate verifies manual override, field lock, cache invalidation and audit.",
                "lockField": True,
                "lockExpiresAt": (datetime.now(timezone.utc) + timedelta(minutes=10)).isoformat(),
            },
            token=token,
        )
        require(status, 200, field_lock)
        assert isinstance(field_lock, dict) and field_lock["active"] is True
        lock_id = field_lock["id"]
        status, locks, _ = call("/api/v1/admin/field-locks", token=token)
        require(status, 200, locks)
        assert isinstance(locks, list) and any(item["id"] == lock_id for item in locks)
        status, changed_detail, _ = call(f"/api/v1/cars/{VF6}")
        require(status, 200, changed_detail)
        assert isinstance(changed_detail, dict) and changed_detail["car"]["trimName"] == temporary_name
    finally:
        if original_name is not None:
            status, restored, _ = call(
                "/api/v1/admin/overrides",
                method="POST",
                body={
                    "entityType": "Trim", "entityId": VF6, "fieldPath": "name", "newValue": original_name,
                    "reason": "V1.10 gate restores the reviewed trim value after cache invalidation verification.",
                    "lockField": False, "lockExpiresAt": None,
                },
                token=token,
            )
            require(status, 200, restored)
        if lock_id is not None:
            status, unlocked, _ = call(
                f"/api/v1/admin/field-locks/{lock_id}/unlock",
                method="POST",
                body={"reason": "V1.10 gate releases its temporary field lock after restoring reviewed data."},
                token=token,
            )
            require(status, 204, unlocked)
        if draft_id is not None:
            status, deleted, _ = call(
                f"/api/v1/admin/catalog/trims/{draft_id}?reason=V1.10%20gate%20removes%20its%20isolated%20unsourced%20draft%20after%20CRUD%20verification.",
                method="DELETE",
                token=token,
            )
            require(status, 204, deleted)

    status, restored_detail, _ = call(f"/api/v1/cars/{VF6}")
    require(status, 200, restored_detail)
    assert isinstance(restored_detail, dict) and restored_detail["car"]["trimName"] == original_name

    status, queue, _ = call("/api/v1/admin/review-queue", token=token)
    require(status, 200, queue)
    assert isinstance(queue, list) and queue
    source_changes = [item for item in queue if item["entityType"].lower() == "source"]
    assert source_changes and all(item["source"] and item["source"]["url"].startswith("https://") for item in source_changes)
    assert any(item["source"].get("contentHash") and item["source"].get("parserVersion") for item in source_changes)

    # Dealer/branch/offer CRUD stays in Draft/PendingReview and is removed; no test offer reaches public data.
    dealer_id: str | None = None
    branch_id: str | None = None
    offer_id: str | None = None
    vinfast_brand = next(brand for brand in coverage["brands"] if brand["brandName"] == "VinFast")
    try:
        status, dealer, _ = call(
            "/api/v1/admin/dealers", method="POST", token=token,
            body={
                "brandId": vinfast_brand["brandId"], "name": "V1.10 isolated dealer draft",
                "slug": f"v110-gate-{uuid.uuid4().hex[:10]}", "officialStatus": False,
                "officialUrl": None,
                "reason": "V1.10 gate verifies dealer CRUD without publishing synthetic dealer data.",
            },
        )
        require(status, 201, dealer)
        assert isinstance(dealer, dict) and dealer["brandName"] == "VinFast"
        dealer_id = dealer["id"]
        status, branch, _ = call(
            "/api/v1/admin/dealer-branches", method="POST", token=token,
            body={
                "dealerId": dealer_id, "name": "Isolated Hanoi draft", "provinceCode": "VN-01",
                "address": "Draft record for automated gate; never published", "latitude": None, "longitude": None,
                "reason": "V1.10 gate verifies branch CRUD with canonical province ownership.",
            },
        )
        require(status, 201, branch)
        assert isinstance(branch, dict) and branch["provinceCode"] == "VN-01"
        branch_id = branch["id"]
        offer_body = {
            "branchId": branch_id, "trimId": VF6, "headline": "Isolated review-only offer draft",
            "combinabilityGroup": "v110-gate", "conditionsJson": json.dumps({"provinceCode": "VN-01"}),
            "status": "Draft", "effectiveFrom": datetime.now(timezone.utc).isoformat(),
            "effectiveTo": (datetime.now(timezone.utc) + timedelta(days=1)).isoformat(), "sourceFactId": None,
            "benefits": [{
                "type": "CashDiscount", "cashValue": 1, "statedValue": 1, "currency": "VND",
                "isCashEquivalent": True, "exclusivityGroup": "v110-cash", "note": "Gate-only draft",
            }],
            "reason": "V1.10 gate verifies structured offer validation in a non-public draft state.",
        }
        status, offer, _ = call("/api/v1/admin/dealer-offers", method="POST", token=token, body=offer_body)
        require(status, 201, offer)
        assert isinstance(offer, dict) and offer["status"] == "Draft" and len(offer["benefits"]) == 1
        offer_id = offer["id"]
        offer_body["status"] = "PendingReview"
        offer_body["reason"] = "V1.10 gate verifies review-state update while keeping the offer non-public."
        status, offer, _ = call(f"/api/v1/admin/dealer-offers/{offer_id}", method="PUT", token=token, body=offer_body)
        require(status, 200, offer)
        assert isinstance(offer, dict) and offer["status"] == "PendingReview"
        status, public_detail, _ = call(f"/api/v1/cars/{VF6}")
        require(status, 200, public_detail)
        assert isinstance(public_detail, dict) and all(item["id"] != offer_id for item in public_detail["dealerOffers"])
    finally:
        if offer_id is not None:
            status, deleted, _ = call(
                f"/api/v1/admin/dealer-offers/{offer_id}?reason=V1.10%20gate%20removes%20its%20non-public%20offer%20draft%20after%20verification.",
                method="DELETE", token=token,
            )
            require(status, 204, deleted)
        if branch_id is not None:
            status, deleted, _ = call(
                f"/api/v1/admin/dealer-branches/{branch_id}?reason=V1.10%20gate%20removes%20its%20isolated%20branch%20draft%20after%20verification.",
                method="DELETE", token=token,
            )
            require(status, 204, deleted)
        if dealer_id is not None:
            status, deleted, _ = call(
                f"/api/v1/admin/dealers/{dealer_id}?reason=V1.10%20gate%20removes%20its%20isolated%20dealer%20draft%20after%20verification.",
                method="DELETE", token=token,
            )
            require(status, 204, deleted)

    status, audit, _ = call("/api/v1/admin/audit?take=100", token=token)
    require(status, 200, audit)
    assert isinstance(audit, list)
    actions = {event["action"] for event in audit}
    assert {
        "SourceUpdated", "ManualImportValidated", "CatalogTrimCreated", "CatalogTrimUpdated",
        "CatalogTrimDeleted", "ManualOverrideWithFieldLock", "ManualOverride", "FieldUnlocked",
        "DealerCreated", "DealerDeleted", "DealerBranchCreated", "DealerBranchDeleted",
        "DealerOfferCreated", "DealerOfferUpdated", "DealerOfferDeleted",
    } <= actions
    assert all(event["actor"] and len(event["reason"]) >= 10 and event["occurredAt"] for event in audit)

    status, _, _ = call(
        "/api/v1/admin/auth/logout",
        method="POST",
        body={"reason": "V1.10 gate rotates and revokes its direct API administrator session."},
        token=token,
    )
    require(status, 204, None)
    status, _, _ = call("/api/v1/admin/auth/session", token=token)
    require(status, 401, None)

    # Verify the browser-facing BFF: HttpOnly cookie, protected SSR and same-origin mutation policy.
    jar = CookieJar()
    opener = build_opener(HTTPCookieProcessor(jar))
    status, browser_login, headers = call(
        "/api/admin/auth/login",
        base=WEB,
        opener=opener,
        method="POST",
        body={"email": ADMIN_EMAIL, "password": ADMIN_PASSWORD},
        headers={"Origin": WEB},
    )
    require(status, 200, browser_login)
    cookie_header = " ".join(value for key, value in headers.items() if key.lower() == "set-cookie").lower()
    assert "httponly" in cookie_header and "samesite=strict" in cookie_header
    status, proxy_coverage, _ = call("/api/admin/coverage", base=WEB, opener=opener)
    require(status, 200, proxy_coverage)
    assert isinstance(proxy_coverage, dict) and proxy_coverage["fullMarketGatePassed"] is False

    request = Request(f"{WEB}/admin", headers={"Accept": "text/html"})  # noqa: S310
    with opener.open(request, timeout=60) as response:  # noqa: S310
        html = response.read().decode("utf-8")
    for expected in ("Trust is an operating system.", "FULL-MARKET GATE", "BLOCKED", "Coverage &amp; QA"):
        assert expected in html, expected
    assert ADMIN_PASSWORD not in html and login["token"] not in html

    status, csrf_error, _ = call(
        "/api/admin/auth/logout",
        base=WEB,
        opener=opener,
        method="POST",
        body={"reason": "This request must be rejected before it reaches the API."},
        headers={"Origin": "https://evil.invalid"},
    )
    require(status, 403, csrf_error)
    status, signed_out, _ = call(
        "/api/admin/auth/logout",
        base=WEB,
        opener=opener,
        method="POST",
        body={"reason": "V1.10 gate signs out the browser-facing administrator session safely."},
        headers={"Origin": WEB},
    )
    require(status, 200, signed_out)
    assert signed_out == {"authenticated": False}

    # Let the final restored cache write settle before a last public-data assertion.
    time.sleep(0.1)
    status, final_detail, _ = call(f"/api/v1/cars/{VF6}")
    require(status, 200, final_detail)
    assert isinstance(final_detail, dict) and final_detail["car"]["trimName"] == original_name
    print(
        "PASS V1.10: RBAC sessions, safe CRUD, source registry, rejected import, review provenance, "
        "field locks, cache invalidation, coverage/DQ, immutable audit and same-origin BFF"
    )


if __name__ == "__main__":
    main()
