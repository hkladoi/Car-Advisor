from __future__ import annotations

import hashlib
import json
import uuid
from datetime import datetime, timedelta
from enum import StrEnum
from pathlib import Path
from typing import Any

import psycopg
from pydantic import BaseModel, ConfigDict, Field, model_validator

from ingestion.fetcher import Snapshot
from ingestion.gate import normalize_text
from ingestion.registry import Authority, SourceRegistry
from ingestion.search_sync import enqueue_catalog_search_sync


_NAMESPACE = uuid.UUID("a784c861-142c-4c73-96fb-efb241ac77c4")
_REQUIRED_EXCLUSIONS = {"ferrari", "lamborghini", "lotus"}
_ACTIVE_STATUSES = {"Active", "Upcoming", "Announced"}
_CORE_SPECS = {
    "SEATS": ("Số chỗ ngồi", None, "Identity"),
    "LENGTH_MM": ("Chiều dài", "mm", "Dimensions"),
    "WIDTH_MM": ("Chiều rộng", "mm", "Dimensions"),
    "HEIGHT_MM": ("Chiều cao", "mm", "Dimensions"),
    "WHEELBASE_MM": ("Chiều dài cơ sở", "mm", "Dimensions"),
}


class Resolution(StrEnum):
    PUBLISHED = "Published"
    BLOCKED = "BlockedWithReason"


class TrimInventory(StrEnum):
    COMPLETE = "Complete"
    BLOCKED = "BlockedWithReason"


class MarketTrimCandidate(BaseModel):
    model_config = ConfigDict(extra="forbid")

    external_key: str | None = Field(default=None, pattern=r"^[a-z0-9]+(?:-[a-z0-9]+)*$", max_length=300)
    name: str = Field(min_length=1, max_length=240)
    slug: str = Field(pattern=r"^[a-z0-9]+(?:-[a-z0-9]+)*$", max_length=220)
    market_status: str = Field(default="Active", pattern=r"^(Active|Upcoming|Announced)$")
    resolution: Resolution = Resolution.PUBLISHED
    blocked_reason: str | None = Field(default=None, min_length=12, max_length=2000)
    source_id: str | None = None

    @model_validator(mode="after")
    def resolution_is_closed(self) -> "MarketTrimCandidate":
        self.external_key = self.external_key or self.slug
        if self.resolution is Resolution.BLOCKED and not self.blocked_reason:
            raise ValueError("Blocked trim candidates require blocked_reason")
        if self.resolution is Resolution.PUBLISHED and self.blocked_reason:
            raise ValueError("Published trim candidates cannot carry blocked_reason")
        return self


class MarketModelCandidate(BaseModel):
    model_config = ConfigDict(extra="forbid")

    external_key: str | None = Field(default=None, pattern=r"^[a-z0-9]+(?:-[a-z0-9]+)*$", max_length=300)
    name: str = Field(min_length=1, max_length=240)
    slug: str = Field(pattern=r"^[a-z0-9]+(?:-[a-z0-9]+)*$", max_length=180)
    market_status: str = Field(default="Active", pattern=r"^(Active|Upcoming|Announced)$")
    resolution: Resolution = Resolution.PUBLISHED
    blocked_reason: str | None = Field(default=None, min_length=12, max_length=2000)
    source_id: str | None = None
    trim_inventory_status: TrimInventory = TrimInventory.BLOCKED
    trim_inventory_reason: str | None = Field(
        default="The official brand registry confirms the model but does not expose a stable, complete trim inventory on the reviewed listing page; no trim identity is invented.",
        min_length=12,
        max_length=2000,
    )
    trims: list[MarketTrimCandidate] = Field(default_factory=list)

    @model_validator(mode="after")
    def inventory_is_explicit(self) -> "MarketModelCandidate":
        self.external_key = self.external_key or self.slug
        if self.resolution is Resolution.BLOCKED and not self.blocked_reason:
            raise ValueError("Blocked model candidates require blocked_reason")
        if self.resolution is Resolution.PUBLISHED and self.blocked_reason:
            raise ValueError("Published model candidates cannot carry blocked_reason")
        if self.trim_inventory_status is TrimInventory.COMPLETE:
            if not self.trims:
                raise ValueError("Complete trim inventory requires at least one trim candidate")
            if self.trim_inventory_reason:
                raise ValueError("Complete trim inventory cannot carry a gap reason")
        elif not self.trim_inventory_reason:
            raise ValueError("Blocked trim inventory requires trim_inventory_reason")
        keys = [trim.external_key for trim in self.trims]
        if len(keys) != len(set(keys)):
            raise ValueError("Trim candidate external keys must be unique within a model")
        return self


class MarketBrandScope(BaseModel):
    model_config = ConfigDict(extra="forbid")

    name: str = Field(min_length=1, max_length=160)
    slug: str = Field(pattern=r"^[a-z0-9]+(?:-[a-z0-9]+)*$", max_length=180)
    country_code: str | None = Field(default=None, pattern=r"^[A-Z]{2}$")
    official_url: str
    included: bool
    reason: str = Field(min_length=12, max_length=500)
    source_id: str
    models: list[MarketModelCandidate] = Field(default_factory=list)

    @model_validator(mode="after")
    def included_brand_has_inventory(self) -> "MarketBrandScope":
        if self.included and not self.models:
            raise ValueError("Included brands require at least one official model candidate")
        if not self.included and self.models:
            raise ValueError("Excluded brands cannot publish market candidates")
        keys = [model.external_key for model in self.models]
        if len(keys) != len(set(keys)):
            raise ValueError("Model candidate external keys must be unique within a brand")
        return self


class MarketScopeManifest(BaseModel):
    model_config = ConfigDict(extra="forbid")

    schema_version: str = Field(pattern=r"^v2\.8$")
    market: str = Field(pattern=r"^VN$")
    observed_at: datetime
    reviewed_at: datetime
    reviewed_by: str = Field(min_length=3, max_length=320)
    review_reason: str = Field(min_length=20, max_length=2000)
    brands: list[MarketBrandScope] = Field(min_length=1)

    @model_validator(mode="after")
    def full_market_policy_is_present(self) -> "MarketScopeManifest":
        if self.observed_at.tzinfo is None or self.observed_at.utcoffset() is None:
            raise ValueError("observed_at must include a timezone")
        if self.reviewed_at.tzinfo is None or self.reviewed_at.utcoffset() is None:
            raise ValueError("reviewed_at must include a timezone")
        if self.reviewed_at < self.observed_at:
            raise ValueError("reviewed_at cannot precede observed_at")
        slugs = [brand.slug for brand in self.brands]
        if len(slugs) != len(set(slugs)):
            raise ValueError("Brand slugs must be unique")
        by_slug = {brand.slug: brand for brand in self.brands}
        if not by_slug.get("porsche") or not by_slug["porsche"].included:
            raise ValueError("Porsche must be included in Vietnam BrandScope")
        missing_exclusions = sorted(
            slug for slug in _REQUIRED_EXCLUSIONS if slug not in by_slug or by_slug[slug].included
        )
        if missing_exclusions:
            raise ValueError("Configured supercar exclusions missing: " + ", ".join(missing_exclusions))
        return self

    @property
    def source_ids(self) -> set[str]:
        values = {brand.source_id for brand in self.brands}
        for brand in self.brands:
            for model in brand.models:
                values.add(model.source_id or brand.source_id)
                values.update(trim.source_id or model.source_id or brand.source_id for trim in model.trims)
        return values


def load_market_scope(path: Path) -> MarketScopeManifest:
    return MarketScopeManifest.model_validate_json(path.read_text(encoding="utf-8"))


def validate_market_scope(manifest: MarketScopeManifest, registry: SourceRegistry) -> dict[str, Any]:
    unknown_sources = sorted(manifest.source_ids - {source.id for source in registry.sources})
    if unknown_sources:
        raise ValueError("Market scope references unregistered sources: " + ", ".join(unknown_sources))
    included = [brand for brand in manifest.brands if brand.included]
    for brand in manifest.brands:
        source = registry.by_id(brand.source_id)
        if brand.included and source.authority not in {Authority.BRAND_OFFICIAL, Authority.DISTRIBUTOR_OFFICIAL}:
            raise ValueError(f"Included brand {brand.name} must use an official brand/distributor source")
        for model in brand.models:
            model_source = registry.by_id(model.source_id or brand.source_id)
            if model_source.authority not in {Authority.BRAND_OFFICIAL, Authority.DISTRIBUTOR_OFFICIAL}:
                raise ValueError(f"Model candidate {brand.name}/{model.name} must use an official source")
            for trim in model.trims:
                trim_source = registry.by_id(trim.source_id or model.source_id or brand.source_id)
                if trim_source.authority not in {Authority.BRAND_OFFICIAL, Authority.DISTRIBUTOR_OFFICIAL}:
                    raise ValueError(f"Trim candidate {brand.name}/{model.name}/{trim.name} must use an official source")
    return {
        "schema_version": manifest.schema_version,
        "market": manifest.market,
        "reviewed_brands": len(manifest.brands),
        "included_brands": len(included),
        "excluded_brands": len(manifest.brands) - len(included),
        "model_candidates": sum(len(brand.models) for brand in included),
        "trim_candidates": sum(len(model.trims) for brand in included for model in brand.models),
        "blocked_models": sum(model.resolution is Resolution.BLOCKED for brand in included for model in brand.models),
        "blocked_trims": sum(trim.resolution is Resolution.BLOCKED for brand in included for model in brand.models for trim in model.trims),
        "trim_inventory_gaps": sum(model.trim_inventory_status is TrimInventory.BLOCKED for brand in included for model in brand.models),
        "source_count": len(manifest.source_ids),
    }


class MarketScopePublisher:
    def __init__(self, dsn: str) -> None:
        self._dsn = dsn

    def publish(
        self,
        manifest: MarketScopeManifest,
        registry: SourceRegistry,
        snapshots: dict[str, Snapshot],
    ) -> dict[str, Any]:
        report = validate_market_scope(manifest, registry)
        missing = sorted(manifest.source_ids - snapshots.keys())
        if missing:
            raise ValueError("Missing immutable market snapshots for: " + ", ".join(missing))
        stale = sorted(
            source_id
            for source_id in manifest.source_ids
            if snapshots[source_id].fetched_at + timedelta(hours=registry.by_id(source_id).refresh_hours) < manifest.reviewed_at
        )
        if stale:
            raise ValueError("Market snapshots exceeded source freshness SLA: " + ", ".join(stale))

        with psycopg.connect(self._dsn) as connection, connection.transaction():
            source_ids = self._upsert_sources(connection, manifest, registry, snapshots)
            snapshot_ids = self._upsert_snapshots(connection, manifest, source_ids, snapshots)
            audit_id = self._insert_audit(connection, manifest, report)
            self._upsert_scope_review(connection, manifest, report, source_ids, snapshot_ids)
            # The manifest is a closed, point-in-time market inventory. Rebuild
            # its candidate rows transactionally so renamed or re-keyed entries
            # cannot survive as silent stale candidates.
            with connection.cursor() as cursor:
                cursor.execute("DELETE FROM market_candidates WHERE market=%s", (manifest.market,))
            for brand in manifest.brands:
                self._publish_brand(
                    connection, manifest, brand, source_ids, snapshot_ids, snapshots
                )
            enqueue_catalog_search_sync(
                connection,
                "MarketScopePublished",
                "MarketScopeManifest",
                correlation_id=f"market-scope:{audit_id}",
                payload={
                    "market": manifest.market,
                    "model_candidates": report["model_candidates"],
                    "trim_candidates": report["trim_candidates"],
                    "observed_at": manifest.observed_at.isoformat(),
                },
            )
        return {**report, "audit_event_id": str(audit_id)}

    @staticmethod
    def _upsert_scope_review(
        connection: psycopg.Connection[Any],
        manifest: MarketScopeManifest,
        report: dict[str, Any],
        source_ids: dict[str, uuid.UUID],
        snapshot_ids: dict[str, uuid.UUID],
    ) -> None:
        policy_key = "full-market-scope-policy"
        canonical = json.dumps(
            manifest.model_dump(mode="json"), ensure_ascii=False, sort_keys=True, separators=(",", ":")
        ).encode("utf-8")
        manifest_hash = hashlib.sha256(canonical).hexdigest()
        review_id = _stable_id("market-scope-review", manifest.market, manifest.schema_version)
        with connection.cursor() as cursor:
            cursor.execute(
                """
                INSERT INTO market_scope_reviews
                    (id,market,schema_version,manifest_hash,reviewed_brand_count,included_brand_count,
                     excluded_brand_count,model_candidate_count,trim_candidate_count,policy_source_id,
                     policy_snapshot_id,observed_at,reviewed_at,reviewed_by,review_reason,created_at,updated_at)
                VALUES (%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s)
                ON CONFLICT (market,schema_version) DO UPDATE SET manifest_hash=EXCLUDED.manifest_hash,
                    reviewed_brand_count=EXCLUDED.reviewed_brand_count,included_brand_count=EXCLUDED.included_brand_count,
                    excluded_brand_count=EXCLUDED.excluded_brand_count,model_candidate_count=EXCLUDED.model_candidate_count,
                    trim_candidate_count=EXCLUDED.trim_candidate_count,policy_source_id=EXCLUDED.policy_source_id,
                    policy_snapshot_id=EXCLUDED.policy_snapshot_id,observed_at=EXCLUDED.observed_at,
                    reviewed_at=EXCLUDED.reviewed_at,reviewed_by=EXCLUDED.reviewed_by,
                    review_reason=EXCLUDED.review_reason,updated_at=EXCLUDED.updated_at
                """,
                (
                    review_id, manifest.market, manifest.schema_version, manifest_hash,
                    report["reviewed_brands"], report["included_brands"], report["excluded_brands"],
                    report["model_candidates"], report["trim_candidates"], source_ids[policy_key],
                    snapshot_ids[policy_key], manifest.observed_at, manifest.reviewed_at,
                    manifest.reviewed_by, manifest.review_reason, manifest.reviewed_at, manifest.reviewed_at,
                ),
            )

    @staticmethod
    def _upsert_sources(
        connection: psycopg.Connection[Any],
        manifest: MarketScopeManifest,
        registry: SourceRegistry,
        snapshots: dict[str, Snapshot],
    ) -> dict[str, uuid.UUID]:
        source_ids: dict[str, uuid.UUID] = {}
        with connection.cursor() as cursor:
            for registry_id in sorted(manifest.source_ids):
                source = registry.by_id(registry_id)
                snapshot = snapshots[registry_id]
                source_id = _stable_id("source", source.url)
                cursor.execute(
                    """
                    INSERT INTO sources
                        (id, name, url, domain, category, authority_level, content_type, robots_note,
                         terms_note, active, priority, refresh_interval, last_fetched_at, created_at, updated_at)
                    VALUES (%s,%s,%s,%s,%s,%s,%s,%s,%s,TRUE,%s,%s,%s,%s,%s)
                    ON CONFLICT (url) DO UPDATE SET name=EXCLUDED.name, domain=EXCLUDED.domain,
                        category=EXCLUDED.category, authority_level=EXCLUDED.authority_level,
                        content_type=EXCLUDED.content_type, robots_note=EXCLUDED.robots_note,
                        terms_note=EXCLUDED.terms_note, active=TRUE, priority=EXCLUDED.priority,
                        refresh_interval=EXCLUDED.refresh_interval, last_fetched_at=EXCLUDED.last_fetched_at,
                        updated_at=EXCLUDED.updated_at
                    RETURNING id
                    """,
                    (
                        source_id, source.name, source.url, source.allowed_domains[0], source.category,
                        source.authority.value, source.content_type.value, source.robots_note, source.terms_note,
                        source.priority, timedelta(hours=source.refresh_hours), snapshot.fetched_at,
                        manifest.reviewed_at, manifest.reviewed_at,
                    ),
                )
                source_ids[registry_id] = cursor.fetchone()[0]
            for source in registry.sources:
                cursor.execute(
                    "UPDATE sources SET category=%s,updated_at=%s WHERE url=%s",
                    (source.category, manifest.reviewed_at, source.url),
                )
        return source_ids

    @staticmethod
    def _upsert_snapshots(
        connection: psycopg.Connection[Any],
        manifest: MarketScopeManifest,
        source_ids: dict[str, uuid.UUID],
        snapshots: dict[str, Snapshot],
    ) -> dict[str, uuid.UUID]:
        snapshot_ids: dict[str, uuid.UUID] = {}
        with connection.cursor() as cursor:
            for registry_id, snapshot in snapshots.items():
                if registry_id not in source_ids:
                    continue
                source_id = source_ids[registry_id]
                proposed_id = _stable_id("snapshot", str(source_id), snapshot.content_hash)
                cursor.execute(
                    """
                    INSERT INTO source_snapshots
                        (id, source_id, fetched_at, content_hash, object_key, http_status, parser_version,
                         etag, last_modified_at, fetch_error, created_at, updated_at)
                    VALUES (%s,%s,%s,%s,%s,%s,%s,%s,%s,NULL,%s,%s)
                    ON CONFLICT (source_id, content_hash) DO NOTHING
                    """,
                    (
                        proposed_id, source_id, snapshot.fetched_at, snapshot.content_hash, snapshot.object_key,
                        snapshot.http_status, f"market-scope/{manifest.schema_version}/{snapshot.fetch_method}",
                        snapshot.etag, _parse_http_date(snapshot.last_modified), manifest.reviewed_at, manifest.reviewed_at,
                    ),
                )
                cursor.execute(
                    "SELECT id FROM source_snapshots WHERE source_id=%s AND content_hash=%s",
                    (source_id, snapshot.content_hash),
                )
                snapshot_ids[registry_id] = cursor.fetchone()[0]
        return snapshot_ids

    @staticmethod
    def _insert_audit(
        connection: psycopg.Connection[Any], manifest: MarketScopeManifest, report: dict[str, Any]
    ) -> uuid.UUID:
        audit_id = _stable_id("market-scope-audit", manifest.schema_version, manifest.reviewed_at.isoformat())
        with connection.cursor() as cursor:
            cursor.execute(
                """
                INSERT INTO audit_events
                    (id,actor,action,entity_type,entity_id,before_json,after_json,reason,occurred_at,
                     correlation_id,created_at,updated_at)
                VALUES (%s,%s,'market-scope.publish','MarketScope',%s,NULL,%s::jsonb,%s,%s,%s,%s,%s)
                ON CONFLICT (id) DO NOTHING
                """,
                (
                    audit_id, manifest.reviewed_by, _stable_id("market-scope", manifest.market),
                    json.dumps(report), manifest.review_reason, manifest.reviewed_at,
                    f"market-scope-{manifest.schema_version}", manifest.reviewed_at, manifest.reviewed_at,
                ),
            )
        return audit_id

    def _publish_brand(
        self,
        connection: psycopg.Connection[Any],
        manifest: MarketScopeManifest,
        brand: MarketBrandScope,
        source_ids: dict[str, uuid.UUID],
        snapshot_ids: dict[str, uuid.UUID],
        snapshots: dict[str, Snapshot],
    ) -> None:
        brand_id = _stable_id("brand", brand.slug)
        scope_source_id = source_ids[brand.source_id]
        scope_snapshot_id = snapshot_ids[brand.source_id]
        with connection.cursor() as cursor:
            cursor.execute(
                """
                INSERT INTO brands (id,name,slug,country_code,official_url,active,created_at,updated_at)
                VALUES (%s,%s,%s,%s,%s,%s,%s,%s)
                ON CONFLICT (slug) DO UPDATE SET name=EXCLUDED.name,country_code=EXCLUDED.country_code,
                    official_url=EXCLUDED.official_url,active=EXCLUDED.active,updated_at=EXCLUDED.updated_at
                RETURNING id
                """,
                (
                    brand_id, brand.name, brand.slug, brand.country_code, brand.official_url, brand.included,
                    manifest.reviewed_at, manifest.reviewed_at,
                ),
            )
            brand_id = cursor.fetchone()[0]
            cursor.execute(
                """
                UPDATE brand_scopes SET effective_to=%s,updated_at=%s
                WHERE brand_id=%s AND market=%s AND effective_to IS NULL AND effective_from < %s
                """,
                (manifest.observed_at, manifest.reviewed_at, brand_id, manifest.market, manifest.observed_at),
            )
            cursor.execute(
                """
                INSERT INTO brand_scopes
                    (id,brand_id,market,included,reason,source_id,evidence_snapshot_id,reviewed_at,reviewed_by,
                     effective_from,effective_to,created_at,updated_at)
                VALUES (%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,NULL,%s,%s)
                ON CONFLICT (market,brand_id,effective_from) DO UPDATE SET included=EXCLUDED.included,
                    reason=EXCLUDED.reason,source_id=EXCLUDED.source_id,evidence_snapshot_id=EXCLUDED.evidence_snapshot_id,
                    reviewed_at=EXCLUDED.reviewed_at,reviewed_by=EXCLUDED.reviewed_by,updated_at=EXCLUDED.updated_at
                """,
                (
                    _stable_id("brand-scope", manifest.market, str(brand_id), manifest.observed_at.isoformat()),
                    brand_id, manifest.market, brand.included, brand.reason, scope_source_id, scope_snapshot_id,
                    manifest.reviewed_at, manifest.reviewed_by, manifest.observed_at,
                    manifest.reviewed_at, manifest.reviewed_at,
                ),
            )
        for model in brand.models:
            self._publish_model_candidate(
                connection,
                manifest,
                brand,
                brand_id,
                model,
                source_ids,
                snapshot_ids,
                snapshots,
            )

    def _publish_model_candidate(
        self,
        connection: psycopg.Connection[Any],
        manifest: MarketScopeManifest,
        brand: MarketBrandScope,
        brand_id: uuid.UUID,
        candidate: MarketModelCandidate,
        source_ids: dict[str, uuid.UUID],
        snapshot_ids: dict[str, uuid.UUID],
        snapshots: dict[str, Snapshot],
    ) -> None:
        registry_id = candidate.source_id or brand.source_id
        source_id = source_ids[registry_id]
        snapshot_id = snapshot_ids[registry_id]
        model_id: uuid.UUID | None = None
        if candidate.resolution is Resolution.PUBLISHED:
            proposed_id = _stable_id("model", brand.slug, candidate.slug)
            with connection.cursor() as cursor:
                cursor.execute(
                    """
                    INSERT INTO models (id,brand_id,name,slug,body_type,segment,search_text,created_at,updated_at)
                    VALUES (%s,%s,%s,%s,'Unknown','Unknown',%s,%s,%s)
                    ON CONFLICT (brand_id,slug) DO UPDATE SET name=EXCLUDED.name,
                        search_text=EXCLUDED.search_text,updated_at=EXCLUDED.updated_at
                    RETURNING id
                    """,
                    (
                        proposed_id, brand_id, candidate.name, candidate.slug,
                        normalize_text(f"{brand.name} {candidate.name}"), manifest.reviewed_at, manifest.reviewed_at,
                    ),
                )
                model_id = cursor.fetchone()[0]
        self._upsert_candidate(
            connection, manifest, brand_id, source_id, snapshot_id, "Model", candidate.external_key,
            candidate.name, None, candidate.market_status, candidate.resolution.value, model_id, None,
            candidate.blocked_reason, candidate.trim_inventory_status.value, candidate.trim_inventory_reason,
            max(manifest.observed_at, snapshots[registry_id].fetched_at),
        )
        for trim in candidate.trims:
            self._publish_trim_candidate(
                connection,
                manifest,
                brand,
                brand_id,
                candidate,
                model_id,
                trim,
                source_ids,
                snapshot_ids,
                snapshots,
            )

    def _publish_trim_candidate(
        self,
        connection: psycopg.Connection[Any],
        manifest: MarketScopeManifest,
        brand: MarketBrandScope,
        brand_id: uuid.UUID,
        model: MarketModelCandidate,
        model_id: uuid.UUID | None,
        candidate: MarketTrimCandidate,
        source_ids: dict[str, uuid.UUID],
        snapshot_ids: dict[str, uuid.UUID],
        snapshots: dict[str, Snapshot],
    ) -> None:
        registry_id = candidate.source_id or model.source_id or brand.source_id
        source_id = source_ids[registry_id]
        snapshot_id = snapshot_ids[registry_id]
        trim_id: uuid.UUID | None = None
        if candidate.resolution is Resolution.PUBLISHED:
            if model_id is None:
                raise ValueError(f"Published trim {candidate.external_key} cannot map to a blocked model")
            trim_id = self._upsert_unknown_trim(
                connection, manifest, brand, model, model_id, candidate, snapshot_id
            )
        trim_external_key = f"{model.external_key}-trim-{candidate.external_key}"
        self._upsert_candidate(
            connection, manifest, brand_id, source_id, snapshot_id, "Trim", trim_external_key,
            candidate.name, model.external_key, candidate.market_status, candidate.resolution.value,
            model_id, trim_id, candidate.blocked_reason, "NotApplicable", None,
            max(manifest.observed_at, snapshots[registry_id].fetched_at),
        )

    @staticmethod
    def _upsert_unknown_trim(
        connection: psycopg.Connection[Any],
        manifest: MarketScopeManifest,
        brand: MarketBrandScope,
        model: MarketModelCandidate,
        model_id: uuid.UUID,
        candidate: MarketTrimCandidate,
        snapshot_id: uuid.UUID,
    ) -> uuid.UUID:
        now = manifest.reviewed_at
        generation_id = _stable_id("market-generation", str(model_id), "VN-CURRENT")
        model_year_id = _stable_id("market-model-year", str(generation_id), str(manifest.observed_at.year), "VN")
        trim_id = _stable_id("market-trim", str(model_year_id), candidate.slug)
        with connection.cursor() as cursor:
            cursor.execute(
                """
                SELECT trim.id
                FROM trims trim
                JOIN model_years model_year ON model_year.id=trim.model_year_id
                JOIN generations generation ON generation.id=model_year.generation_id
                WHERE generation.model_id=%s AND trim.slug=%s
                ORDER BY trim.updated_at DESC
                LIMIT 1
                """,
                (model_id, candidate.slug),
            )
            existing = cursor.fetchone()
            if existing:
                cursor.execute(
                    "UPDATE trims SET market_status=%s,updated_at=%s WHERE id=%s",
                    (candidate.market_status, now, existing[0]),
                )
                return existing[0]
            cursor.execute(
                """
                INSERT INTO generations (id,model_id,code,name,start_year,end_year,created_at,updated_at)
                VALUES (%s,%s,'VN-CURRENT','Vietnam current official listing',%s,NULL,%s,%s)
                ON CONFLICT (model_id,code) DO UPDATE SET start_year=EXCLUDED.start_year,updated_at=EXCLUDED.updated_at
                RETURNING id
                """,
                (generation_id, model_id, manifest.observed_at.year, now, now),
            )
            generation_id = cursor.fetchone()[0]
            cursor.execute(
                """
                INSERT INTO model_years (id,generation_id,year,market,created_at,updated_at)
                VALUES (%s,%s,%s,'VN',%s,%s)
                ON CONFLICT (generation_id,year,market) DO UPDATE SET updated_at=EXCLUDED.updated_at
                RETURNING id
                """,
                (model_year_id, generation_id, manifest.observed_at.year, now, now),
            )
            model_year_id = cursor.fetchone()[0]
            normalized_key = normalize_text(candidate.name).replace(" ", "-")
            cursor.execute(
                """
                INSERT INTO trims
                    (id,model_year_id,name,slug,normalized_key,market_status,launched_at,discontinued_at,
                     search_text,created_at,updated_at)
                VALUES (%s,%s,%s,%s,%s,%s,NULL,NULL,%s,%s,%s)
                ON CONFLICT (model_year_id,normalized_key) DO UPDATE SET name=EXCLUDED.name,slug=EXCLUDED.slug,
                    market_status=EXCLUDED.market_status,search_text=EXCLUDED.search_text,updated_at=EXCLUDED.updated_at
                RETURNING id
                """,
                (
                    trim_id, model_year_id, candidate.name, candidate.slug, normalized_key,
                    candidate.market_status, normalize_text(f"{brand.name} {model.name} {candidate.name}"), now, now,
                ),
            )
            trim_id = cursor.fetchone()[0]
            fact_ids: dict[str, uuid.UUID] = {}
            for field_path in ("core.price", "core.powertrain", "core.seats", "core.length_mm", "core.width_mm", "core.height_mm", "core.wheelbase_mm"):
                fact_id = _stable_id("market-unknown-fact", str(snapshot_id), str(trim_id), field_path)
                cursor.execute(
                    """
                    INSERT INTO source_facts
                        (id,snapshot_id,entity_type,entity_id,field_path,raw_value,normalized_value,status,
                         confidence,extraction_context,created_at,updated_at)
                    VALUES (%s,%s,'Trim',%s,%s,NULL,NULL,'Unknown','Unknown',%s,%s,%s)
                    ON CONFLICT (id) DO UPDATE SET extraction_context=EXCLUDED.extraction_context,
                        updated_at=EXCLUDED.updated_at
                    """,
                    (
                        fact_id, snapshot_id, trim_id, field_path,
                        "Explicit UNKNOWN: the reviewed official listing identifies this trim but does not disclose this canonical fact on the inventory page.",
                        now, now,
                    ),
                )
                fact_ids[field_path] = fact_id
            powertrain_id = _stable_id("market-powertrain", str(trim_id))
            cursor.execute(
                """
                INSERT INTO powertrain_profiles
                    (id,trim_id,type,fuel_type,engine_displacement_cc,engine_power_kw,motor_power_kw,
                     combined_power_kw,torque_nm,gearbox,drivetrain,source_fact_id,manual_override_reason,
                     created_at,updated_at)
                VALUES (%s,%s,'Unknown',NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,%s,NULL,%s,%s)
                ON CONFLICT (trim_id) DO UPDATE SET source_fact_id=EXCLUDED.source_fact_id,
                    updated_at=EXCLUDED.updated_at
                """,
                (powertrain_id, trim_id, fact_ids["core.powertrain"], now, now),
            )
            for code, (label, unit, group) in _CORE_SPECS.items():
                definition_id = _stable_id("spec-definition", code)
                cursor.execute(
                    """
                    INSERT INTO spec_definitions
                        (id,code,label,data_type,canonical_unit,"group",minimum_numeric_value,maximum_numeric_value,
                         created_at,updated_at)
                    VALUES (%s,%s,%s,'Number',%s,%s,NULL,NULL,%s,%s)
                    ON CONFLICT (code) DO UPDATE SET label=EXCLUDED.label,canonical_unit=EXCLUDED.canonical_unit,
                        "group"=EXCLUDED."group",updated_at=EXCLUDED.updated_at
                    RETURNING id
                    """,
                    (definition_id, code, label, unit, group, now, now),
                )
                definition_id = cursor.fetchone()[0]
                field_name = code.lower()
                spec_id = _stable_id("market-trim-spec", str(trim_id), code, manifest.observed_at.isoformat())
                cursor.execute(
                    """
                INSERT INTO trim_specs
                    (id,trim_id,spec_definition_id,status,numeric_value,text_value,enum_value,original_value,
                     original_unit,source_fact_id,manual_override_reason,created_at,updated_at)
                VALUES (%s,%s,%s,'Unknown',NULL,NULL,NULL,NULL,NULL,%s,NULL,%s,%s)
                ON CONFLICT (trim_id,spec_definition_id) DO UPDATE SET
                        status='Unknown',source_fact_id=EXCLUDED.source_fact_id,updated_at=EXCLUDED.updated_at
                """,
                    (spec_id, trim_id, definition_id, fact_ids[f"core.{field_name}"], now, now),
                )
            price_id = _stable_id("market-unannounced-price", str(trim_id), manifest.observed_at.isoformat())
            cursor.execute(
                """
                INSERT INTO prices
                    (id,trim_id,price_type,amount,currency,region_scope,status,priority,version,
                     source_fact_id,manual_override_reason,effective_from,effective_to,created_at,updated_at)
                VALUES (%s,%s,'Unannounced',NULL,'VND','VN','Unknown',100,1,%s,NULL,%s,NULL,%s,%s)
                ON CONFLICT (trim_id,price_type,region_scope,version) DO UPDATE SET
                    source_fact_id=EXCLUDED.source_fact_id,effective_from=EXCLUDED.effective_from,
                    updated_at=EXCLUDED.updated_at
                """,
                (price_id, trim_id, fact_ids["core.price"], manifest.observed_at, now, now),
            )
        return trim_id

    @staticmethod
    def _upsert_candidate(
        connection: psycopg.Connection[Any],
        manifest: MarketScopeManifest,
        brand_id: uuid.UUID,
        source_id: uuid.UUID,
        snapshot_id: uuid.UUID,
        kind: str,
        external_key: str,
        name: str,
        parent_external_key: str | None,
        market_status: str,
        resolution: str,
        model_id: uuid.UUID | None,
        trim_id: uuid.UUID | None,
        blocked_reason: str | None,
        trim_inventory_status: str,
        trim_inventory_reason: str | None,
        last_seen_at: datetime,
    ) -> None:
        candidate_id = _stable_id("market-candidate", manifest.market, str(brand_id), kind, external_key)
        with connection.cursor() as cursor:
            cursor.execute(
                """
                INSERT INTO market_candidates
                    (id,market,brand_id,source_id,evidence_snapshot_id,external_key,name,parent_external_key,
                     kind,market_status,resolution,model_id,trim_id,blocked_reason,trim_inventory_status,
                     trim_inventory_reason,discovered_at,last_seen_at,reviewed_at,reviewed_by,created_at,updated_at)
                VALUES (%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s)
                ON CONFLICT (market,brand_id,kind,external_key) DO UPDATE SET source_id=EXCLUDED.source_id,
                    evidence_snapshot_id=EXCLUDED.evidence_snapshot_id,name=EXCLUDED.name,
                    parent_external_key=EXCLUDED.parent_external_key,market_status=EXCLUDED.market_status,
                    resolution=EXCLUDED.resolution,model_id=EXCLUDED.model_id,trim_id=EXCLUDED.trim_id,
                    blocked_reason=EXCLUDED.blocked_reason,trim_inventory_status=EXCLUDED.trim_inventory_status,
                    trim_inventory_reason=EXCLUDED.trim_inventory_reason,last_seen_at=EXCLUDED.last_seen_at,
                    reviewed_at=EXCLUDED.reviewed_at,reviewed_by=EXCLUDED.reviewed_by,updated_at=EXCLUDED.updated_at
                """,
                (
                    candidate_id, manifest.market, brand_id, source_id, snapshot_id, external_key, name,
                    parent_external_key, kind, market_status, resolution, model_id, trim_id, blocked_reason,
                    trim_inventory_status, trim_inventory_reason, manifest.observed_at, last_seen_at,
                    manifest.reviewed_at, manifest.reviewed_by, manifest.reviewed_at, manifest.reviewed_at,
                ),
            )

def _stable_id(*parts: str) -> uuid.UUID:
    return uuid.uuid5(_NAMESPACE, "|".join(parts))


def _parse_http_date(value: str | None) -> datetime | None:
    if not value:
        return None
    try:
        from email.utils import parsedate_to_datetime

        parsed = parsedate_to_datetime(value)
        return parsed if parsed.tzinfo else parsed.astimezone()
    except (TypeError, ValueError, OverflowError):
        return None
