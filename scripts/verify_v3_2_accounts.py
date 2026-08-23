#!/usr/bin/env python3
"""V3.2 opt-in account/privacy gate against the live Compose stack."""

from __future__ import annotations

import json
import secrets
import subprocess
import time
import urllib.error
import urllib.request


API = "http://localhost:8080/api/v1"


def call(method: str, path: str, body: object | None = None, token: str | None = None) -> tuple[int, object | None]:
    headers = {"Accept": "application/json"}
    data = None
    if body is not None:
        headers["Content-Type"] = "application/json"
        data = json.dumps(body).encode("utf-8")
    if token:
        headers["Authorization"] = f"Bearer {token}"
    request = urllib.request.Request(f"{API}/{path}", data=data, headers=headers, method=method)
    try:
        with urllib.request.urlopen(request, timeout=30) as response:
            raw = response.read()
            return response.status, json.loads(raw) if raw else None
    except urllib.error.HTTPError as error:
        raw = error.read()
        return error.code, json.loads(raw) if raw else None


def require(condition: bool, message: str) -> None:
    if not condition:
        raise AssertionError(message)


def database_count(user_id: str, email: str) -> dict[str, int]:
    sql = f"""
SELECT json_build_object(
  'accounts', (SELECT count(*) FROM user_accounts WHERE id = '{user_id}'::uuid OR normalized_email = upper('{email}')),
  'sessions', (SELECT count(*) FROM user_sessions WHERE user_account_id = '{user_id}'::uuid),
  'profiles', (SELECT count(*) FROM affordability_profiles WHERE owner_subject_id = '{user_id}'),
  'comparisons', (SELECT count(*) FROM saved_comparisons WHERE user_account_id = '{user_id}'::uuid),
  'watchlist', (SELECT count(*) FROM watchlist_entries WHERE user_account_id = '{user_id}'::uuid)
)::text;
"""
    result = subprocess.run(
        ["docker", "compose", "exec", "-T", "postgres", "psql", "-U", "vcp", "-d", "vietnam_car_platform", "-At", "-c", sql],
        check=True,
        capture_output=True,
        text=True,
    )
    return json.loads(result.stdout.strip())


def cleanup_gate_accounts() -> None:
    sql = """
BEGIN;
DELETE FROM affordability_profiles
WHERE owner_subject_id IN (SELECT id::text FROM user_accounts WHERE email LIKE 'v32-gate-%@example.invalid');
DELETE FROM user_accounts WHERE email LIKE 'v32-gate-%@example.invalid';
COMMIT;
"""
    subprocess.run(
        ["docker", "compose", "exec", "-T", "postgres", "psql", "-U", "vcp", "-d", "vietnam_car_platform", "-v", "ON_ERROR_STOP=1", "-c", sql],
        check=True,
        capture_output=True,
        text=True,
    )


def main() -> None:
    cleanup_gate_accounts()
    suffix = int(time.time() * 1000)
    email = f"v32-gate-{suffix}@example.invalid"
    password = f"V3-{secrets.token_urlsafe(18)}-A1"

    status, _ = call("GET", "accounts/me")
    require(status == 401, "private account endpoint must reject anonymous requests")
    status, payload = call("POST", "accounts/register", {
        "email": email,
        "password": password,
        "displayName": "V3.2 Privacy Gate",
        "privacyConsent": False,
    })
    require(status == 400 and isinstance(payload, dict) and payload.get("code") == "ACCOUNT_CONSENT_REQUIRED",
            "registration must reject missing explicit consent")

    status, auth = call("POST", "accounts/register", {
        "email": email,
        "password": password,
        "displayName": "V3.2 Privacy Gate",
        "privacyConsent": True,
    })
    require(status == 201 and isinstance(auth, dict) and auth.get("token"), "consented registration failed")
    token = str(auth["token"])
    user_id = str(auth["userId"])

    status, session = call("GET", "accounts/me", token=token)
    require(status == 200 and isinstance(session, dict) and session.get("privacyPolicyVersion") == "2026-08-v1",
            "account session must retain consent version")

    status, profile = call("PUT", "accounts/profile", {
        "name": "Hồ sơ kiểm thử riêng tư",
        "regionCode": "VN-01",
        "netMonthlyIncome": 45_000_000,
        "rentHousing": 8_000_000,
        "essentialExpenses": 12_000_000,
        "otherFixedDebt": 2_000_000,
        "savingsTarget": 6_000_000,
        "monthlyKilometres": 1_200,
        "parkingMonthly": 1_500_000,
        "householdBaseKwh": 250,
        "policy": "Balanced",
    }, token)
    require(status == 200 and isinstance(profile, dict) and profile.get("regionCode") == "VN-01", "profile persistence failed")

    status, catalog = call("GET", "cars?pageSize=100")
    require(status == 200 and isinstance(catalog, dict) and len(catalog.get("data", [])) >= 2, "live catalog is unavailable")
    trims = [str(item["trimId"]) for item in catalog["data"]]

    status, comparison = call("POST", "accounts/comparisons", {
        "name": "Gate comparison",
        "trimIds": trims[:2],
        "regionCode": "VN-01",
        "profilePreset": "city-balanced",
        "financingPreset": "standard-loan",
    }, token)
    require(status == 201 and isinstance(comparison, dict) and len(comparison.get("trimIds", [])) == 2,
            "saved comparison failed")

    for trim_id in trims:
        status, _ = call("PUT", "accounts/watchlist", {
            "trimId": trim_id,
            "regionCode": "VN",
            "targetPrice": None,
            "priceAlerts": True,
            "promotionAlerts": True,
            "dealerOfferAlerts": True,
        }, token)
        require(status == 200, f"watchlist persistence failed for {trim_id}")

    status, alerts = call("GET", "accounts/alerts", token=token)
    require(status == 200 and isinstance(alerts, list), "alert feed failed")
    kinds = sorted({str(item["kind"]) for item in alerts})
    require("Price" in kinds, "price alert signal missing from live data")
    require(all(item.get("source", {}).get("sourceFactId") for item in alerts), "alert provenance is incomplete")

    status, export = call("GET", "accounts/export", token=token)
    require(status == 200 and isinstance(export, dict), "data export failed")
    require(export.get("profile", {}).get("netMonthlyIncome") == 45_000_000, "export omitted private profile")
    require(len(export.get("savedComparisons", [])) == 1, "export omitted saved comparison")
    require(len(export.get("watchlist", [])) == len(trims), "export omitted watchlist entries")
    before = database_count(user_id, email)
    require(all(before[key] > 0 for key in before), f"pre-delete persistence evidence incomplete: {before}")

    status, _ = call("DELETE", "accounts/me", {"password": password, "confirmation": "DELETE"}, token)
    require(status == 204, "account deletion failed")
    status, _ = call("GET", "accounts/me", token=token)
    require(status == 401, "deleted session must be invalid immediately")
    after = database_count(user_id, email)
    require(all(value == 0 for value in after.values()), f"private rows remain after deletion: {after}")

    print(json.dumps({
        "gate": "V3.2",
        "status": "PASS",
        "anonymousRejected": True,
        "consentRequired": True,
        "privacyPolicyVersion": session["privacyPolicyVersion"],
        "savedComparisonCount": 1,
        "watchlistCount": len(trims),
        "alertKinds": kinds,
        "promotionLiveDataAvailable": "Promotion" in kinds,
        "promotionPolicyCoveredByUnitGate": True,
        "dealerOfferLiveDataAvailable": "DealerOffer" in kinds,
        "dealerOfferPolicyCoveredByUnitGate": True,
        "alertCount": len(alerts),
        "exportComplete": True,
        "rowsBeforeDelete": before,
        "rowsAfterDelete": after,
    }, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    try:
        main()
    except Exception:
        cleanup_gate_accounts()
        raise
