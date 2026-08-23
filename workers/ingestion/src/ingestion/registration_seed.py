from __future__ import annotations

import hashlib
import json
import uuid
from datetime import datetime, timedelta
from pathlib import Path
from typing import Any

import psycopg
from pydantic import BaseModel, ConfigDict, Field, model_validator

from ingestion.fetcher import Snapshot
from ingestion.registry import SourceRegistry
from ingestion.storage import ObjectStorage


_NAMESPACE = uuid.UUID("f5a25127-52e6-4f59-a72c-d568ff5dca6e")
_AREA_I_CODES = {1, 79}
_COMPONENTS = {
    "FirstRegistrationTax",
    "PlateAndRegistrationFee",
    "CompulsoryInsurance",
    "InspectionFee",
    "RoadUsageFee",
    "Other",
}
_CALCULATION_TYPES = {"Fixed", "Percentage", "Tiered", "Formula"}


def stable_id(*parts: str) -> uuid.UUID:
    return uuid.uuid5(_NAMESPACE, "|".join(parts))


class RegistrationRuleSeed(BaseModel):
    model_config = ConfigDict(extra="forbid")

    key: str = Field(pattern=r"^[a-z0-9]+(?:-[a-z0-9]+)*$")
    component: str
    scope: dict[str, Any]
    calculation_type: str
    parameters: dict[str, Any]
    priority: int = Field(ge=0)
    version: int = Field(gt=0)
    effective_from: datetime
    effective_to: datetime | None = None
    source_id: str
    citation: str = Field(min_length=20)

    @model_validator(mode="after")
    def validate_rule(self) -> "RegistrationRuleSeed":
        if self.component not in _COMPONENTS:
            raise ValueError(f"Unsupported registration component: {self.component}")
        if self.calculation_type not in _CALCULATION_TYPES:
            raise ValueError(f"Unsupported calculation type: {self.calculation_type}")
        if self.effective_from.tzinfo is None or (
            self.effective_to is not None and self.effective_to.tzinfo is None
        ):
            raise ValueError("Rule effective dates must include a timezone")
        if self.effective_to is not None and self.effective_from >= self.effective_to:
            raise ValueError("effective_to must be later than effective_from")
        return self


class RegistrationSeedBatch(BaseModel):
    model_config = ConfigDict(extra="forbid")

    schema_version: str = "v1.5"
    observed_at: datetime
    reviewed_by: str = Field(min_length=3)
    review_reason: str = Field(min_length=20)
    province_source_id: str
    rules: list[RegistrationRuleSeed] = Field(min_length=1)

    @model_validator(mode="after")
    def validate_batch(self) -> "RegistrationSeedBatch":
        if self.observed_at.tzinfo is None:
            raise ValueError("observed_at must include a timezone")
        keys = [rule.key for rule in self.rules]
        if len(keys) != len(set(keys)):
            raise ValueError("Registration rule keys must be unique")
        return self

    @property
    def source_ids(self) -> set[str]:
        return {self.province_source_id, *(rule.source_id for rule in self.rules)}


def load_registration_seed(path: Path) -> RegistrationSeedBatch:
    return RegistrationSeedBatch.model_validate_json(path.read_text(encoding="utf-8"))


def validate_registration_seed(batch: RegistrationSeedBatch, registry: SourceRegistry) -> dict[str, Any]:
    unknown = sorted(source_id for source_id in batch.source_ids if source_id not in {source.id for source in registry.sources})
    if unknown:
        raise ValueError("Unknown registration source IDs: " + ", ".join(unknown))
    return {
        "passed": True,
        "schema_version": batch.schema_version,
        "rules": len(batch.rules),
        "sources": len(batch.source_ids),
    }


def normalize_provinces(content: bytes, expected_count: int = 34) -> list[dict[str, Any]]:
    payload = json.loads(content.decode("utf-8"))
    if not isinstance(payload, list) or len(payload) != expected_count:
        raise ValueError(f"Province Open API v2 must contain exactly {expected_count} provinces")
    normalized: list[dict[str, Any]] = []
    seen_codes: set[int] = set()
    ward_codes: set[int] = set()
    for province in payload:
        code = int(province["code"])
        if code in seen_codes:
            raise ValueError(f"Duplicate province code: {code}")
        seen_codes.add(code)
        wards = []
        for ward in province.get("wards", []):
            ward_code = int(ward["code"])
            if ward_code in ward_codes:
                raise ValueError(f"Duplicate ward code: {ward_code}")
            ward_codes.add(ward_code)
            wards.append({"code": f"VN-W-{ward_code:05d}", "name": str(ward["name"]).strip()})
        normalized.append(
            {
                "code": f"VN-{code:02d}",
                "name": str(province["name"]).strip(),
                "area_class": "I" if code in _AREA_I_CODES else "II",
                "wards": wards,
            }
        )
    return normalized


class RegistrationSeedPublisher:
    def __init__(self, dsn: str, storage: ObjectStorage) -> None:
        self._dsn = dsn
        self._storage = storage

    def publish(
        self,
        batch: RegistrationSeedBatch,
        registry: SourceRegistry,
        snapshots: dict[str, Snapshot],
    ) -> dict[str, Any]:
        validate_registration_seed(batch, registry)
        missing = sorted(batch.source_ids - snapshots.keys())
        if missing:
            raise ValueError("Missing immutable snapshots for: " + ", ".join(missing))
        province_snapshot = snapshots[batch.province_source_id]
        province_content = self._storage.get_bytes(province_snapshot.object_key)
        if hashlib.sha256(province_content).hexdigest() != province_snapshot.content_hash:
            raise ValueError("Province snapshot content hash does not match its immutable manifest")
        provinces = normalize_provinces(province_content)

        with psycopg.connect(self._dsn) as connection, connection.transaction():
            source_ids = self._upsert_sources(connection, batch, registry, snapshots)
            snapshot_ids = self._upsert_snapshots(connection, batch, source_ids, snapshots)
            region_count, ward_count = self._publish_regions(
                connection,
                batch,
                provinces,
                snapshot_ids[batch.province_source_id],
            )
            for rule in batch.rules:
                self._publish_rule(connection, batch, rule, snapshot_ids[rule.source_id])
            audit_id = self._publish_audit(connection, batch, region_count, ward_count)

        return {
            "provinces": region_count,
            "wards": ward_count,
            "rules": len(batch.rules),
            "snapshots": len(snapshot_ids),
            "audit_event_id": str(audit_id),
        }

    @staticmethod
    def _upsert_sources(
        connection: psycopg.Connection[Any],
        batch: RegistrationSeedBatch,
        registry: SourceRegistry,
        snapshots: dict[str, Snapshot],
    ) -> dict[str, uuid.UUID]:
        result: dict[str, uuid.UUID] = {}
        for registry_id in sorted(batch.source_ids):
            source = registry.by_id(registry_id)
            snapshot = snapshots[registry_id]
            source_id = stable_id("source", source.url)
            with connection.cursor() as cursor:
                cursor.execute(
                    """
                    INSERT INTO sources
                        (id, name, url, domain, authority_level, content_type, robots_note,
                         terms_note, active, priority, refresh_interval, last_fetched_at, created_at, updated_at)
                    VALUES (%s, %s, %s, %s, %s, %s, %s, %s, TRUE, %s, %s, %s, %s, %s)
                    ON CONFLICT (url) DO UPDATE SET name = EXCLUDED.name, authority_level = EXCLUDED.authority_level,
                        content_type = EXCLUDED.content_type, robots_note = EXCLUDED.robots_note,
                        terms_note = EXCLUDED.terms_note, priority = EXCLUDED.priority,
                        refresh_interval = EXCLUDED.refresh_interval, last_fetched_at = EXCLUDED.last_fetched_at,
                        updated_at = EXCLUDED.updated_at
                    RETURNING id
                    """,
                    (
                        source_id,
                        source.name,
                        source.url,
                        source.allowed_domains[0],
                        source.authority.value,
                        source.content_type.value,
                        source.robots_note,
                        source.terms_note,
                        source.priority,
                        timedelta(hours=source.refresh_hours),
                        snapshot.fetched_at,
                        batch.observed_at,
                        batch.observed_at,
                    ),
                )
                result[registry_id] = cursor.fetchone()[0]
        return result

    @staticmethod
    def _upsert_snapshots(
        connection: psycopg.Connection[Any],
        batch: RegistrationSeedBatch,
        source_ids: dict[str, uuid.UUID],
        snapshots: dict[str, Snapshot],
    ) -> dict[str, uuid.UUID]:
        result: dict[str, uuid.UUID] = {}
        for registry_id in sorted(batch.source_ids):
            snapshot = snapshots[registry_id]
            source_id = source_ids[registry_id]
            snapshot_id = stable_id("snapshot", str(source_id), snapshot.content_hash)
            with connection.cursor() as cursor:
                cursor.execute(
                    """
                    INSERT INTO source_snapshots
                        (id, source_id, fetched_at, content_hash, object_key, http_status, parser_version,
                         etag, last_modified_at, fetch_error, created_at, updated_at)
                    VALUES (%s, %s, %s, %s, %s, %s, 'registration-seed/v1.5', %s, NULL, NULL, %s, %s)
                    ON CONFLICT (source_id, content_hash) DO NOTHING
                    """,
                    (
                        snapshot_id,
                        source_id,
                        snapshot.fetched_at,
                        snapshot.content_hash,
                        snapshot.object_key,
                        snapshot.http_status,
                        snapshot.etag,
                        batch.observed_at,
                        batch.observed_at,
                    ),
                )
                cursor.execute(
                    "SELECT id FROM source_snapshots WHERE source_id = %s AND content_hash = %s",
                    (source_id, snapshot.content_hash),
                )
                result[registry_id] = cursor.fetchone()[0]
        return result

    @staticmethod
    def _publish_regions(
        connection: psycopg.Connection[Any],
        batch: RegistrationSeedBatch,
        provinces: list[dict[str, Any]],
        snapshot_id: uuid.UUID,
    ) -> tuple[int, int]:
        ward_count = 0
        for province in provinces:
            RegistrationSeedPublisher._upsert_region(connection, batch, province, snapshot_id, None, "Province")
            for ward in province["wards"]:
                ward["area_class"] = province["area_class"]
                RegistrationSeedPublisher._upsert_region(
                    connection, batch, ward, snapshot_id, province["code"], "Ward"
                )
                ward_count += 1
        return len(provinces), ward_count

    @staticmethod
    def _upsert_region(
        connection: psycopg.Connection[Any],
        batch: RegistrationSeedBatch,
        region: dict[str, Any],
        snapshot_id: uuid.UUID,
        parent_code: str | None,
        region_type: str,
    ) -> None:
        region_id = stable_id("region", region["code"])
        fact_id = stable_id("source-fact", str(snapshot_id), "Region", region["code"])
        with connection.cursor() as cursor:
            cursor.execute(
                """
                INSERT INTO source_facts
                    (id, snapshot_id, entity_type, entity_id, field_path, raw_value, normalized_value,
                     status, confidence, extraction_context, created_at, updated_at)
                VALUES (%s, %s, 'Region', %s, 'identity', %s, %s, 'Official', 'TrustedSingleSource',
                        'Province Open API v2 normalized by numeric code; labels never determine fee area.', %s, %s)
                ON CONFLICT (id) DO UPDATE SET raw_value = EXCLUDED.raw_value,
                    normalized_value = EXCLUDED.normalized_value, updated_at = EXCLUDED.updated_at
                """,
                (
                    fact_id,
                    snapshot_id,
                    region_id,
                    region["name"],
                    json.dumps({"code": region["code"], "name": region["name"]}, ensure_ascii=False),
                    batch.observed_at,
                    batch.observed_at,
                ),
            )
            cursor.execute(
                """
                INSERT INTO regions
                    (id, code, name, type, area_class, parent_code, active, source_fact_id,
                     manual_override_reason, created_at, updated_at)
                VALUES (%s, %s, %s, %s, %s, %s, TRUE, %s, NULL, %s, %s)
                ON CONFLICT (code) DO UPDATE SET name = EXCLUDED.name, type = EXCLUDED.type,
                    area_class = EXCLUDED.area_class, parent_code = EXCLUDED.parent_code, active = TRUE,
                    source_fact_id = EXCLUDED.source_fact_id, manual_override_reason = NULL,
                    updated_at = EXCLUDED.updated_at
                """,
                (
                    region_id,
                    region["code"],
                    region["name"],
                    region_type,
                    region["area_class"],
                    parent_code,
                    fact_id,
                    batch.observed_at,
                    batch.observed_at,
                ),
            )

    @staticmethod
    def _publish_rule(
        connection: psycopg.Connection[Any],
        batch: RegistrationSeedBatch,
        rule: RegistrationRuleSeed,
        snapshot_id: uuid.UUID,
    ) -> None:
        rule_id = stable_id("registration-rule", rule.key)
        fact_id = stable_id("source-fact", str(snapshot_id), "RegistrationRule", rule.key)
        with connection.cursor() as cursor:
            cursor.execute(
                """
                INSERT INTO source_facts
                    (id, snapshot_id, entity_type, entity_id, field_path, raw_value, normalized_value,
                     status, confidence, extraction_context, created_at, updated_at)
                VALUES (%s, %s, 'RegistrationRule', %s, 'calculation', %s, %s, 'Official',
                        'VerifiedOfficial', %s, %s, %s)
                ON CONFLICT (id) DO UPDATE SET raw_value = EXCLUDED.raw_value,
                    normalized_value = EXCLUDED.normalized_value, extraction_context = EXCLUDED.extraction_context,
                    updated_at = EXCLUDED.updated_at
                """,
                (
                    fact_id,
                    snapshot_id,
                    rule_id,
                    rule.citation,
                    json.dumps(rule.model_dump(mode="json"), ensure_ascii=False),
                    f"Reviewed V1.5 rule key: {rule.key}",
                    batch.observed_at,
                    batch.observed_at,
                ),
            )
            cursor.execute(
                """
                INSERT INTO registration_rules
                    (id, component, scope_json, calculation_type, parameters_json, priority, version,
                     effective_from, effective_to, source_fact_id, manual_override_reason, created_at, updated_at)
                VALUES (%s, %s, %s::jsonb, %s, %s::jsonb, %s, %s, %s, %s, %s, NULL, %s, %s)
                ON CONFLICT (id) DO UPDATE SET component = EXCLUDED.component, scope_json = EXCLUDED.scope_json,
                    calculation_type = EXCLUDED.calculation_type, parameters_json = EXCLUDED.parameters_json,
                    priority = EXCLUDED.priority, version = EXCLUDED.version,
                    effective_from = EXCLUDED.effective_from, effective_to = EXCLUDED.effective_to,
                    source_fact_id = EXCLUDED.source_fact_id, manual_override_reason = NULL,
                    updated_at = EXCLUDED.updated_at
                """,
                (
                    rule_id,
                    rule.component,
                    json.dumps(rule.scope),
                    rule.calculation_type,
                    json.dumps(rule.parameters),
                    rule.priority,
                    rule.version,
                    rule.effective_from,
                    rule.effective_to,
                    fact_id,
                    batch.observed_at,
                    batch.observed_at,
                ),
            )

    @staticmethod
    def _publish_audit(
        connection: psycopg.Connection[Any],
        batch: RegistrationSeedBatch,
        province_count: int,
        ward_count: int,
    ) -> uuid.UUID:
        audit_id = stable_id("audit", "registration-seed", batch.observed_at.isoformat())
        with connection.cursor() as cursor:
            cursor.execute(
                """
                INSERT INTO audit_events
                    (id, actor, action, entity_type, entity_id, before_json, after_json, reason,
                     occurred_at, correlation_id, created_at, updated_at)
                VALUES (%s, %s, 'registration-seed.publish', 'RegistrationSeed', %s, NULL, %s::jsonb,
                        %s, %s, %s, %s, %s)
                ON CONFLICT (id) DO NOTHING
                """,
                (
                    audit_id,
                    batch.reviewed_by,
                    stable_id("registration-seed", batch.schema_version),
                    json.dumps({"provinces": province_count, "wards": ward_count, "rules": len(batch.rules)}),
                    batch.review_reason,
                    batch.observed_at,
                    f"registration-seed-{batch.observed_at:%Y%m%d}",
                    batch.observed_at,
                    batch.observed_at,
                ),
            )
        return audit_id
