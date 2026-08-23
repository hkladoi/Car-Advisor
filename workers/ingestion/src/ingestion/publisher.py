from __future__ import annotations

import json
import uuid
from datetime import UTC, datetime, timedelta
from typing import Any

import psycopg

from ingestion.contracts import CoreFact, FactStatus, ManualImportBatch, PriceFact, VehicleSeedRecord
from ingestion.fetcher import Snapshot
from ingestion.gate import evaluate_seed_gate, normalize_text
from ingestion.registry import RegistrySource, SourceRegistry
from ingestion.risk import classify_change


_NAMESPACE = uuid.UUID("f5a25127-52e6-4f59-a72c-d568ff5dca6e")
_SPEC_DEFINITIONS = {
    "seats": ("SEATS", "Số chỗ ngồi", None, "Identity"),
    "length_mm": ("LENGTH_MM", "Chiều dài", "mm", "Dimensions"),
    "width_mm": ("WIDTH_MM", "Chiều rộng", "mm", "Dimensions"),
    "height_mm": ("HEIGHT_MM", "Chiều cao", "mm", "Dimensions"),
    "wheelbase_mm": ("WHEELBASE_MM", "Chiều dài cơ sở", "mm", "Dimensions"),
}


class PostgresPublisher:
    """Transactional, idempotent publisher for human-reviewed seed imports."""

    def __init__(self, dsn: str) -> None:
        self._dsn = dsn

    def publish(
        self,
        batch: ManualImportBatch,
        registry: SourceRegistry,
        snapshots: dict[str, Snapshot],
    ) -> dict[str, Any]:
        report = evaluate_seed_gate(batch, registry)
        if not report.passed:
            raise ValueError("Seed gate failed: " + "; ".join(report.errors))
        missing_snapshots = sorted({record.source_id for record in batch.records} - snapshots.keys())
        if missing_snapshots:
            raise ValueError(f"Missing immutable snapshots for: {', '.join(missing_snapshots)}")

        with psycopg.connect(self._dsn) as connection, connection.transaction():
            source_ids = self._upsert_sources(connection, batch, registry, snapshots)
            snapshot_ids = self._upsert_snapshots(connection, batch, source_ids, snapshots)
            audit_id = self._insert_audit_event(connection, batch)
            for record in batch.records:
                self._publish_record(
                    connection,
                    batch,
                    record,
                    source_ids[record.source_id],
                    snapshot_ids[record.source_id],
                    audit_id,
                )
            self._refresh_catalog_read_model(connection)

        return {
            "seeded_brands": report.seeded_brands,
            "seeded_trims": report.seeded_trims,
            "transparency_ratio": report.transparency_ratio,
            "snapshots": len(snapshot_ids),
            "audit_event_id": str(audit_id),
        }

    @staticmethod
    def _refresh_catalog_read_model(connection: psycopg.Connection[Any]) -> None:
        """Refresh only when the V1.3 read-model migration is present.

        Keeping this inside the reviewed publish transaction ensures catalog readers
        never see a partially published batch.
        """
        with connection.cursor() as cursor:
            cursor.execute("SELECT to_regprocedure('refresh_current_searchable_trims()') IS NOT NULL")
            if cursor.fetchone()[0]:
                cursor.execute("SELECT refresh_current_searchable_trims()")

    def _upsert_sources(
        self,
        connection: psycopg.Connection[Any],
        batch: ManualImportBatch,
        registry: SourceRegistry,
        snapshots: dict[str, Snapshot],
    ) -> dict[str, uuid.UUID]:
        source_ids: dict[str, uuid.UUID] = {}
        for source in registry.sources:
            registry_id = source.id
            source_id = _stable_id("source", source.url)
            snapshot = snapshots.get(registry_id)
            with connection.cursor() as cursor:
                cursor.execute(
                    """
                    INSERT INTO sources
                        (id, name, url, domain, category, authority_level, content_type, robots_note,
                         terms_note, active, priority, refresh_interval, last_fetched_at,
                         created_at, updated_at)
                    VALUES (%s, %s, %s, %s, %s, %s, %s, %s, %s, TRUE, %s, %s, %s, %s, %s)
                    ON CONFLICT (url) DO UPDATE SET
                        name = EXCLUDED.name,
                        domain = EXCLUDED.domain,
                        category = EXCLUDED.category,
                        authority_level = EXCLUDED.authority_level,
                        content_type = EXCLUDED.content_type,
                        robots_note = EXCLUDED.robots_note,
                        terms_note = EXCLUDED.terms_note,
                        priority = EXCLUDED.priority,
                        refresh_interval = EXCLUDED.refresh_interval,
                        last_fetched_at = EXCLUDED.last_fetched_at,
                        updated_at = EXCLUDED.updated_at
                    RETURNING id
                    """,
                    (
                        source_id,
                        source.name,
                        source.url,
                        source.allowed_domains[0],
                        source.category,
                        source.authority.value,
                        source.content_type.value,
                        source.robots_note,
                        source.terms_note,
                        source.priority,
                        timedelta(hours=source.refresh_hours),
                        snapshot.fetched_at if snapshot else None,
                        batch.observed_at,
                        batch.observed_at,
                    ),
                )
                source_ids[registry_id] = cursor.fetchone()[0]
        return source_ids

    def _upsert_snapshots(
        self,
        connection: psycopg.Connection[Any],
        batch: ManualImportBatch,
        source_ids: dict[str, uuid.UUID],
        snapshots: dict[str, Snapshot],
    ) -> dict[str, uuid.UUID]:
        snapshot_ids: dict[str, uuid.UUID] = {}
        for registry_id, snapshot in snapshots.items():
            source_id = source_ids[registry_id]
            proposed_id = _stable_id("snapshot", str(source_id), snapshot.content_hash)
            with connection.cursor() as cursor:
                cursor.execute(
                    """
                    INSERT INTO source_snapshots
                        (id, source_id, fetched_at, content_hash, object_key, http_status,
                         parser_version, etag, last_modified_at, fetch_error, created_at, updated_at)
                    VALUES (%s, %s, %s, %s, %s, %s, 'manual-seed/v1.2', %s, NULL, NULL, %s, %s)
                    ON CONFLICT (source_id, content_hash) DO NOTHING
                    """,
                    (
                        proposed_id,
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
                snapshot_ids[registry_id] = cursor.fetchone()[0]
        return snapshot_ids

    def _insert_audit_event(self, connection: psycopg.Connection[Any], batch: ManualImportBatch) -> uuid.UUID:
        audit_id = _stable_id("audit", batch.schema_version, batch.observed_at.isoformat(), batch.reviewed_by)
        with connection.cursor() as cursor:
            cursor.execute(
                """
                INSERT INTO audit_events
                    (id, actor, action, entity_type, entity_id, before_json, after_json,
                     reason, occurred_at, correlation_id, created_at, updated_at)
                VALUES (%s, %s, 'seed.publish', 'SeedBatch', %s, NULL, %s::jsonb, %s, %s, %s, %s, %s)
                ON CONFLICT (id) DO NOTHING
                """,
                (
                    audit_id,
                    batch.reviewed_by,
                    _stable_id("seed-batch", batch.observed_at.isoformat()),
                    json.dumps({"records": len(batch.records), "schema_version": batch.schema_version}),
                    batch.review_reason,
                    batch.observed_at,
                    f"seed-{batch.observed_at:%Y%m%d}",
                    batch.observed_at,
                    batch.observed_at,
                ),
            )
        return audit_id

    def _publish_record(
        self,
        connection: psycopg.Connection[Any],
        batch: ManualImportBatch,
        record: VehicleSeedRecord,
        source_id: uuid.UUID,
        snapshot_id: uuid.UUID,
        audit_id: uuid.UUID,
    ) -> None:
        brand_id = _stable_id("brand", record.brand_slug)
        model_id = _stable_id("model", record.brand_slug, record.model_slug)
        generation_id = _stable_id("generation", str(model_id), record.generation_code)
        model_year_id = _stable_id("model-year", str(generation_id), str(record.model_year), "VN")
        trim_id = _stable_id("trim", str(model_year_id), record.trim_slug)
        now = batch.observed_at

        with connection.cursor() as cursor:
            cursor.execute(
                """
                INSERT INTO brands (id, name, slug, country_code, official_url, active, created_at, updated_at)
                VALUES (%s, %s, %s, %s, %s, TRUE, %s, %s)
                ON CONFLICT (slug) DO UPDATE SET name = EXCLUDED.name, official_url = EXCLUDED.official_url,
                    country_code = EXCLUDED.country_code, active = TRUE, updated_at = EXCLUDED.updated_at
                """,
                (brand_id, record.brand_name, record.brand_slug, record.brand_country_code, record.brand_official_url, now, now),
            )
            cursor.execute(
                """
                INSERT INTO brand_scopes
                    (id, brand_id, included, reason, effective_from, effective_to, created_at, updated_at)
                VALUES (%s, %s, TRUE, %s, %s, NULL, %s, %s)
                ON CONFLICT (brand_id, effective_from) DO UPDATE SET included = TRUE, reason = EXCLUDED.reason,
                    updated_at = EXCLUDED.updated_at
                """,
                (_stable_id("brand-scope", str(brand_id), now.isoformat()), brand_id, batch.review_reason, now, now, now),
            )
            cursor.execute(
                """
                INSERT INTO models (id, brand_id, name, slug, body_type, segment, search_text, created_at, updated_at)
                VALUES (%s, %s, %s, %s, %s, %s, %s, %s, %s)
                ON CONFLICT (brand_id, slug) DO UPDATE SET name = EXCLUDED.name, body_type = EXCLUDED.body_type,
                    segment = EXCLUDED.segment, search_text = EXCLUDED.search_text, updated_at = EXCLUDED.updated_at
                """,
                (
                    model_id,
                    brand_id,
                    record.model_name,
                    record.model_slug,
                    _fact_enum(record.core.body_type, "Unknown"),
                    _fact_enum(record.core.segment, "Unknown"),
                    normalize_text(f"{record.brand_name} {record.model_name}"),
                    now,
                    now,
                ),
            )
            cursor.execute(
                """
                INSERT INTO generations (id, model_id, code, name, start_year, end_year, created_at, updated_at)
                VALUES (%s, %s, %s, NULL, %s, NULL, %s, %s)
                ON CONFLICT (model_id, code) DO UPDATE SET start_year = EXCLUDED.start_year, updated_at = EXCLUDED.updated_at
                """,
                (generation_id, model_id, record.generation_code, record.generation_start_year, now, now),
            )
            cursor.execute(
                """
                INSERT INTO model_years (id, generation_id, year, market, created_at, updated_at)
                VALUES (%s, %s, %s, 'VN', %s, %s)
                ON CONFLICT (generation_id, year, market) DO UPDATE SET updated_at = EXCLUDED.updated_at
                """,
                (model_year_id, generation_id, record.model_year, now, now),
            )
            cursor.execute(
                """
                INSERT INTO trims
                    (id, model_year_id, name, slug, normalized_key, market_status, launched_at,
                     discontinued_at, search_text, created_at, updated_at)
                VALUES (%s, %s, %s, %s, %s, %s, NULL, NULL, %s, %s, %s)
                ON CONFLICT (model_year_id, normalized_key) DO UPDATE SET name = EXCLUDED.name,
                    slug = EXCLUDED.slug, market_status = EXCLUDED.market_status,
                    search_text = EXCLUDED.search_text, updated_at = EXCLUDED.updated_at
                """,
                (
                    trim_id,
                    model_year_id,
                    record.trim_name,
                    record.trim_slug,
                    normalize_text(record.trim_name).replace(" ", "-"),
                    _fact_enum(record.core.market_status, "Unknown"),
                    normalize_text(f"{record.brand_name} {record.model_name} {record.trim_name}"),
                    now,
                    now,
                ),
            )
            compact_alias = normalize_text(record.model_name).replace(" ", "")
            cursor.execute(
                """
                INSERT INTO model_aliases (id, model_id, alias, normalized_alias, source_id, created_at, updated_at)
                VALUES (%s, %s, %s, %s, %s, %s, %s)
                ON CONFLICT (model_id, normalized_alias) DO UPDATE SET source_id = EXCLUDED.source_id,
                    updated_at = EXCLUDED.updated_at
                """,
                (_stable_id("model-alias", str(model_id), compact_alias), model_id, compact_alias, compact_alias, source_id, now, now),
            )

        facts = self._insert_source_facts(connection, record, snapshot_id, trim_id, now)
        self._upsert_powertrain(connection, record, trim_id, facts["powertrain"], now)
        self._upsert_specs(connection, record, trim_id, facts, now)
        self._upsert_features(connection, record, trim_id, facts, now)
        self._upsert_price(connection, record, trim_id, facts["price"], now)
        self._insert_changes(connection, record, trim_id, facts, audit_id, now)
        self._insert_coverage(connection, record, brand_id, model_id, trim_id, now)

    def _insert_source_facts(
        self,
        connection: psycopg.Connection[Any],
        record: VehicleSeedRecord,
        snapshot_id: uuid.UUID,
        trim_id: uuid.UUID,
        now: datetime,
    ) -> dict[str, uuid.UUID]:
        fact_ids: dict[str, uuid.UUID] = {}
        with connection.cursor() as cursor:
            for name, fact in record.core.facts().items():
                fact_id = _stable_id("source-fact", str(snapshot_id), str(trim_id), name)
                normalized_value = _fact_value(fact)
                cursor.execute(
                    """
                    INSERT INTO source_facts
                        (id, snapshot_id, entity_type, entity_id, field_path, raw_value,
                         normalized_value, status, confidence, extraction_context, created_at, updated_at)
                    VALUES (%s, %s, 'Trim', %s, %s, %s, %s, %s, %s, %s, %s, %s)
                    ON CONFLICT (id) DO UPDATE SET raw_value = EXCLUDED.raw_value,
                        normalized_value = EXCLUDED.normalized_value, status = EXCLUDED.status,
                        confidence = EXCLUDED.confidence, extraction_context = EXCLUDED.extraction_context,
                        updated_at = EXCLUDED.updated_at
                    """,
                    (
                        fact_id,
                        snapshot_id,
                        trim_id,
                        f"core.{name}",
                        fact.raw_value,
                        normalized_value,
                        fact.status.value,
                        fact.confidence.value,
                        json.dumps({"source_url": record.source_url, "method": "reviewed-manual-import"}),
                        now,
                        now,
                    ),
                )
                fact_ids[name] = fact_id
            for feature in record.features:
                key = f"feature:{feature.code}"
                fact_id = _stable_id("source-fact", str(snapshot_id), str(trim_id), key)
                cursor.execute(
                    """
                    INSERT INTO source_facts
                        (id, snapshot_id, entity_type, entity_id, field_path, raw_value,
                         normalized_value, status, confidence, extraction_context, created_at, updated_at)
                    VALUES (%s, %s, 'Trim', %s, %s, %s, %s, %s, %s, %s, %s, %s)
                    ON CONFLICT (id) DO UPDATE SET raw_value = EXCLUDED.raw_value,
                        normalized_value = EXCLUDED.normalized_value, status = EXCLUDED.status,
                        confidence = EXCLUDED.confidence, extraction_context = EXCLUDED.extraction_context,
                        updated_at = EXCLUDED.updated_at
                    """,
                    (
                        fact_id,
                        snapshot_id,
                        trim_id,
                        f"features.{feature.code}",
                        feature.fact.raw_value,
                        _fact_value(feature.fact),
                        feature.fact.status.value,
                        feature.fact.confidence.value,
                        json.dumps({"source_url": record.source_url, "method": "reviewed-manual-import"}),
                        now,
                        now,
                    ),
                )
                fact_ids[key] = fact_id
        return fact_ids

    def _upsert_powertrain(
        self,
        connection: psycopg.Connection[Any],
        record: VehicleSeedRecord,
        trim_id: uuid.UUID,
        fact_id: uuid.UUID,
        now: datetime,
    ) -> None:
        with connection.cursor() as cursor:
            cursor.execute(
                """
                INSERT INTO powertrain_profiles
                    (id, trim_id, type, fuel_type, engine_displacement_cc, engine_power_kw,
                     motor_power_kw, combined_power_kw, torque_nm, gearbox, drivetrain,
                     source_fact_id, manual_override_reason, created_at, updated_at)
                VALUES (%s, %s, %s, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, %s, NULL, %s, %s)
                ON CONFLICT (trim_id) DO UPDATE SET type = EXCLUDED.type,
                    source_fact_id = EXCLUDED.source_fact_id, updated_at = EXCLUDED.updated_at
                """,
                (_stable_id("powertrain", str(trim_id)), trim_id, _fact_enum(record.core.powertrain, "Unknown"), fact_id, now, now),
            )

    def _upsert_specs(
        self,
        connection: psycopg.Connection[Any],
        record: VehicleSeedRecord,
        trim_id: uuid.UUID,
        facts: dict[str, uuid.UUID],
        now: datetime,
    ) -> None:
        with connection.cursor() as cursor:
            for field_name, (code, label, unit, group) in _SPEC_DEFINITIONS.items():
                definition_id = _stable_id("spec-definition", code)
                cursor.execute(
                    """
                    INSERT INTO spec_definitions
                        (id, code, label, data_type, canonical_unit, "group", minimum_numeric_value,
                         maximum_numeric_value, created_at, updated_at)
                    VALUES (%s, %s, %s, 'Number', %s, %s, 0, NULL, %s, %s)
                    ON CONFLICT (code) DO UPDATE SET label = EXCLUDED.label,
                        canonical_unit = EXCLUDED.canonical_unit, updated_at = EXCLUDED.updated_at
                    """,
                    (definition_id, code, label, unit, group, now, now),
                )
                fact = getattr(record.core, field_name)
                cursor.execute(
                    """
                    INSERT INTO trim_specs
                        (id, trim_id, spec_definition_id, status, numeric_value, text_value,
                         enum_value, original_value, original_unit, source_fact_id,
                         manual_override_reason, created_at, updated_at)
                    VALUES (%s, %s, %s, %s, %s, NULL, NULL, %s, %s, %s, NULL, %s, %s)
                    ON CONFLICT (trim_id, spec_definition_id) DO UPDATE SET status = EXCLUDED.status,
                        numeric_value = EXCLUDED.numeric_value, original_value = EXCLUDED.original_value,
                        original_unit = EXCLUDED.original_unit, source_fact_id = EXCLUDED.source_fact_id,
                        updated_at = EXCLUDED.updated_at
                    """,
                    (
                        _stable_id("trim-spec", str(trim_id), code),
                        trim_id,
                        definition_id,
                        fact.status.value,
                        fact.value,
                        fact.raw_value,
                        unit,
                        facts[field_name],
                        now,
                        now,
                    ),
                )

    def _upsert_price(
        self,
        connection: psycopg.Connection[Any],
        record: VehicleSeedRecord,
        trim_id: uuid.UUID,
        fact_id: uuid.UUID,
        now: datetime,
    ) -> None:
        price = record.core.price
        price_id = _stable_id("price", str(trim_id), price.price_type, "VN", now.isoformat())
        with connection.cursor() as cursor:
            cursor.execute(
                """
                INSERT INTO prices
                    (id, trim_id, price_type, amount, currency, region_scope, status, priority,
                     version, effective_from, effective_to, source_fact_id, manual_override_reason,
                     created_at, updated_at)
                VALUES (%s, %s, %s, %s, %s, 'VN', %s, 0, 1, %s, NULL, %s, NULL, %s, %s)
                ON CONFLICT (id) DO UPDATE SET amount = EXCLUDED.amount, status = EXCLUDED.status,
                    effective_from = EXCLUDED.effective_from, source_fact_id = EXCLUDED.source_fact_id,
                    updated_at = EXCLUDED.updated_at
                """,
                (price_id, trim_id, price.price_type, price.amount, price.currency, price.status.value, price.effective_from, fact_id, now, now),
            )

    def _upsert_features(
        self,
        connection: psycopg.Connection[Any],
        record: VehicleSeedRecord,
        trim_id: uuid.UUID,
        facts: dict[str, uuid.UUID],
        now: datetime,
    ) -> None:
        with connection.cursor() as cursor:
            for feature in record.features:
                definition_id = _stable_id("feature-definition", feature.code)
                cursor.execute(
                    """
                    INSERT INTO feature_definitions
                        (id, code, "group", data_type, label, minimum_numeric_value,
                         maximum_numeric_value, created_at, updated_at)
                    VALUES (%s, %s, %s, 'Boolean', %s, NULL, NULL, %s, %s)
                    ON CONFLICT (code) DO UPDATE SET "group" = EXCLUDED."group",
                        label = EXCLUDED.label, data_type = 'Boolean', updated_at = EXCLUDED.updated_at
                    """,
                    (definition_id, feature.code, feature.group, feature.label, now, now),
                )
                cursor.execute(
                    """
                    INSERT INTO trim_features
                        (id, trim_id, feature_definition_id, status, boolean_value,
                         numeric_value, text_value, enum_value, marketing_name,
                         source_fact_id, manual_override_reason, created_at, updated_at)
                    VALUES (%s, %s, %s, %s, %s, NULL, NULL, NULL, NULL, %s, NULL, %s, %s)
                    ON CONFLICT (trim_id, feature_definition_id) DO UPDATE SET
                        status = EXCLUDED.status, boolean_value = EXCLUDED.boolean_value,
                        source_fact_id = EXCLUDED.source_fact_id, updated_at = EXCLUDED.updated_at
                    """,
                    (
                        _stable_id("trim-feature", str(trim_id), feature.code),
                        trim_id,
                        definition_id,
                        feature.fact.status.value,
                        feature.fact.value,
                        facts[f"feature:{feature.code}"],
                        now,
                        now,
                    ),
                )

    def _insert_changes(
        self,
        connection: psycopg.Connection[Any],
        record: VehicleSeedRecord,
        trim_id: uuid.UUID,
        facts: dict[str, uuid.UUID],
        audit_id: uuid.UUID,
        now: datetime,
    ) -> None:
        with connection.cursor() as cursor:
            for field_name, fact in record.core.facts().items():
                new_value = _fact_value(fact)
                cursor.execute(
                    """
                    INSERT INTO data_changes
                        (id, entity_type, entity_id, field_path, old_value, new_value, risk_level,
                         status, detected_at, source_fact_id, reviewed_audit_event_id,
                         created_at, updated_at)
                    VALUES (%s, 'Trim', %s, %s, NULL, %s, %s, 'Approved', %s, %s, %s, %s, %s)
                    ON CONFLICT (id) DO NOTHING
                    """,
                    (
                        _stable_id("data-change", str(trim_id), field_name, now.isoformat()),
                        trim_id,
                        f"core.{field_name}",
                        new_value,
                        classify_change(field_name, None, new_value).value,
                        now,
                        facts[field_name],
                        audit_id,
                        now,
                        now,
                    ),
                )
            for feature in record.features:
                field_name = f"features.{feature.code}"
                new_value = _fact_value(feature.fact)
                cursor.execute(
                    """
                    INSERT INTO data_changes
                        (id, entity_type, entity_id, field_path, old_value, new_value, risk_level,
                         status, detected_at, source_fact_id, reviewed_audit_event_id,
                         created_at, updated_at)
                    VALUES (%s, 'Trim', %s, %s, NULL, %s, %s, 'Approved', %s, %s, %s, %s, %s)
                    ON CONFLICT (id) DO NOTHING
                    """,
                    (
                        _stable_id("data-change", str(trim_id), field_name, now.isoformat()),
                        trim_id,
                        field_name,
                        new_value,
                        classify_change(field_name, None, new_value).value,
                        now,
                        facts[f"feature:{feature.code}"],
                        audit_id,
                        now,
                        now,
                    ),
                )

    def _insert_coverage(
        self,
        connection: psycopg.Connection[Any],
        record: VehicleSeedRecord,
        brand_id: uuid.UUID,
        model_id: uuid.UUID,
        trim_id: uuid.UUID,
        now: datetime,
    ) -> None:
        facts = list(record.core.facts().values())
        mapped = sum(1 for fact in facts if fact.status.value != "Unknown")
        missing = len(facts) - mapped
        with connection.cursor() as cursor:
            cursor.execute(
                """
                INSERT INTO coverage_metrics
                    (id, brand_id, model_id, trim_id, completeness, freshness, missing_core_count,
                     discovered_count, mapped_count, published_count, blocked_count, stale_count,
                     calculated_at, created_at, updated_at)
                VALUES (%s, %s, %s, %s, 1.0, 1.0, %s, %s, %s, %s, 0, 0, %s, %s, %s)
                ON CONFLICT (id) DO UPDATE SET missing_core_count = EXCLUDED.missing_core_count,
                    mapped_count = EXCLUDED.mapped_count, published_count = EXCLUDED.published_count,
                    calculated_at = EXCLUDED.calculated_at, updated_at = EXCLUDED.updated_at
                """,
                (
                    _stable_id("coverage", str(trim_id), now.isoformat()),
                    brand_id,
                    model_id,
                    trim_id,
                    missing,
                    len(facts),
                    mapped,
                    len(facts),
                    now,
                    now,
                    now,
                ),
            )


def _stable_id(*parts: str) -> uuid.UUID:
    return uuid.uuid5(_NAMESPACE, "|".join(parts))


def _fact_enum(fact: CoreFact, default: str) -> str:
    return str(fact.value) if fact.value is not None else default


def _fact_value(fact: CoreFact | PriceFact) -> str | None:
    if isinstance(fact, PriceFact):
        if fact.amount is not None:
            return str(fact.amount)
        return "UNANNOUNCED" if fact.status in {FactStatus.OFFICIAL, FactStatus.EXPECTED} else None
    if fact.value is None:
        return None
    return str(fact.value)
