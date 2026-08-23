#!/usr/bin/env python3
"""V2.4 gate: snapshot evidence -> typed publish -> immutable rollback."""

from __future__ import annotations

import json
import os
import subprocess
import uuid
from decimal import Decimal
from urllib.error import HTTPError
from urllib.request import Request, urlopen


API = os.getenv("VCP_API_BASE", "http://127.0.0.1:8080")
ADMIN_EMAIL = os.getenv("ADMIN_BOOTSTRAP_EMAIL", "admin@vcp.local")
ADMIN_PASSWORD = os.getenv("ADMIN_BOOTSTRAP_PASSWORD", "vcp-admin-local-dev-only")


def call(path: str, *, method: str = "GET", body: dict | None = None, token: str | None = None) -> tuple[int, object | None]:
    headers = {"Accept": "application/json"}
    if token:
        headers["Authorization"] = f"Bearer {token}"
    data = None
    if body is not None:
        data = json.dumps(body).encode("utf-8")
        headers["Content-Type"] = "application/json"
    request = Request(f"{API}{path}", data=data, headers=headers, method=method)  # noqa: S310
    try:
        with urlopen(request, timeout=60) as response:  # noqa: S310
            raw = response.read()
            return response.status, json.loads(raw) if raw else None
    except HTTPError as error:
        raw = error.read()
        return error.code, json.loads(raw) if raw else None


def psql(sql: str) -> str:
    process = subprocess.run(  # noqa: S603 - fixed local Docker Compose command
        [
            "docker", "compose", "exec", "-T", "postgres", "psql",
            "-v", "ON_ERROR_STOP=1", "-U", "vcp", "-d", "vietnam_car_platform",
            "-At", "-F", "|",
        ],
        input=sql,
        text=True,
        capture_output=True,
        check=False,
    )
    assert process.returncode == 0, process.stderr
    return process.stdout.strip()


def require(status: int, expected: int, payload: object | None) -> None:
    assert status == expected, (status, expected, payload)


def main() -> None:
    evidence = psql(
        """
        SELECT sf.id, sf.entity_id, ts.numeric_value::text, COALESCE(ts.source_fact_id::text, '')
        FROM source_facts sf
        JOIN trim_specs ts ON ts.trim_id = sf.entity_id
        JOIN spec_definitions sd ON sd.id = ts.spec_definition_id AND sd.code = 'SEATS'
        WHERE sf.entity_type = 'Trim' AND sf.field_path = 'spec.seats'
          AND sf.normalized_value::numeric = ts.numeric_value
        ORDER BY sf.created_at DESC
        LIMIT 1;
        """
    )
    assert evidence, "V2.3 official spec.seats evidence is required before V2.4"
    source_fact_id, trim_id, old_value_raw, before_source_fact_id = evidence.split("|")
    old_value = str(Decimal(old_value_raw).normalize())
    edited_value = str(int(Decimal(old_value)) + 1)
    change_id = str(uuid.uuid4())
    reason = "V2.4 gate verifies typed canonical publication from immutable official evidence."
    rollback_reason = "V2.4 gate restores the exact prior canonical value and provenance after publication."

    psql(
        f"""
        INSERT INTO data_changes
            (id, entity_type, entity_id, field_path, old_value, new_value, risk_level,
             status, detected_at, anomaly_code, detection_context, source_fact_id,
             created_at, updated_at)
        VALUES
            ('{change_id}', 'Trim', '{trim_id}', 'spec.seats', '{old_value}', '{old_value}',
             'Medium', 'PendingReview', CURRENT_TIMESTAMP, 'V2_4_GATE_REVIEW',
             '{{"gate":"typed-edit-publish-rollback"}}'::jsonb, '{source_fact_id}',
             CURRENT_TIMESTAMP, CURRENT_TIMESTAMP);
        """
    )

    status, login = call(
        "/api/v1/admin/auth/login",
        method="POST",
        body={"email": ADMIN_EMAIL, "password": ADMIN_PASSWORD},
    )
    require(status, 200, login)
    assert isinstance(login, dict)
    token = login["token"]

    status, queue = call("/api/v1/admin/review-queue", token=token)
    require(status, 200, queue)
    assert isinstance(queue, list)
    review = next(item for item in queue if item["id"] == change_id)
    assert review["anomalyCode"] == "V2_4_GATE_REVIEW"
    assert review["source"]["sourceFactId"] == source_fact_id
    assert review["source"]["snapshotId"]
    assert review["source"]["objectKey"] and review["source"]["parserVersion"]
    assert review["source"]["rawValue"] and review["source"]["extractionContext"]

    status, payload = call(
        f"/api/v1/admin/changes/{change_id}/approve",
        method="POST",
        body={"reason": reason, "editedValue": edited_value},
        token=token,
    )
    require(status, 204, payload)
    published_state = psql(
        f"""
        SELECT ts.numeric_value::text, COALESCE(ts.source_fact_id::text, ''), dc.status
        FROM trim_specs ts
        JOIN spec_definitions sd ON sd.id = ts.spec_definition_id AND sd.code = 'SEATS'
        JOIN data_changes dc ON dc.id = '{change_id}'
        WHERE ts.trim_id = '{trim_id}';
        """
    ).split("|")
    assert Decimal(published_state[0]) == Decimal(edited_value)
    assert published_state[1] == source_fact_id and published_state[2] == "Published"

    status, publications = call("/api/v1/admin/publications?take=500", token=token)
    require(status, 200, publications)
    assert isinstance(publications, list)
    publication = next(item for item in publications if item["dataChangeId"] == change_id)
    assert Decimal(publication["beforeValue"]) == Decimal(old_value)
    assert Decimal(publication["afterValue"]) == Decimal(edited_value)
    assert publication["beforeSourceFactId"] == (before_source_fact_id or None)
    assert publication["sourceFactId"] == source_fact_id and publication["status"] == "Published"

    status, payload = call(
        f"/api/v1/admin/publications/{publication['id']}/rollback",
        method="POST",
        body={"reason": rollback_reason},
        token=token,
    )
    require(status, 204, payload)
    restored = psql(
        f"""
        SELECT ts.numeric_value::text, COALESCE(ts.source_fact_id::text, ''),
               pv.status, dc.status,
               EXISTS (SELECT 1 FROM audit_events ae WHERE ae.entity_id = pv.id AND ae.action = 'PublicationRolledBack')
        FROM trim_specs ts
        JOIN spec_definitions sd ON sd.id = ts.spec_definition_id AND sd.code = 'SEATS'
        JOIN publication_versions pv ON pv.data_change_id = '{change_id}'
        JOIN data_changes dc ON dc.id = pv.data_change_id
        WHERE ts.trim_id = '{trim_id}';
        """
    ).split("|")
    assert Decimal(restored[0]) == Decimal(old_value)
    assert restored[1] == before_source_fact_id
    assert restored[2:] == ["RolledBack", "RolledBack", "t"]

    status, _ = call(
        f"/api/v1/admin/publications/{publication['id']}/rollback",
        method="POST",
        body={"reason": "V2.4 gate verifies repeat rollback is rejected safely."},
        token=token,
    )
    require(status, 409, None)
    call(
        "/api/v1/admin/auth/logout",
        method="POST",
        body={"reason": "V2.4 gate revokes its administrator session after verification."},
        token=token,
    )
    print(
        "PASS V2.4: immutable snapshot evidence, typed edit-and-publish, publication lineage, "
        "exact value/provenance rollback, audit, and repeat-rollback conflict all verified."
    )


if __name__ == "__main__":
    main()
