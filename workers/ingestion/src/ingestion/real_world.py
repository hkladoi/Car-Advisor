from __future__ import annotations

import csv
import hashlib
import json
import uuid
from datetime import UTC, datetime, timedelta
from decimal import Decimal, InvalidOperation
from email.utils import parsedate_to_datetime
from io import StringIO
from typing import Any

import psycopg
from pydantic import BaseModel, ConfigDict, Field, model_validator

from ingestion.fetcher import Snapshot
from ingestion.registry import RegistrySource
from ingestion.storage import ObjectStorage


DATASET_VERSION = "eea-real-world-co2-2024-v03-r00"
DATASET_REPORTING_YEAR = 2024
METHODOLOGY_URL = (
    "https://sdi.eea.europa.eu/catalogue/srv/api/records/"
    "d12422cc-f1b9-4a20-b31e-94fff4d997ed/attachments/"
    "Real%20world%20emissions%20for%20cars%20and%20vans_Statistical%20metadata_2024.pdf"
)
ATTRIBUTION = "European Environment Agency (EEA), real-world emissions from cars and vans."
GEOGRAPHY = "EU/EEA reporting Member States"
AGGREGATION_SCOPE = "ManufacturerFuelRegistrationYear"

_NAMESPACE = uuid.UUID("f5a25127-52e6-4f59-a72c-d568ff5dca6e")

_HEADERS = {
    "Year",
    "Manufacturer",
    "Fuel Type",
    "Number of vehicles",
    "OBFCM Fuel consumption (l/100 km)",
    "WLTP Fuel consumption (l/100 km)",
    "absolute gap Fuel consumption (l/100 km)",
    "percentage gap Fuel consumption (%)",
    "OBFCM CO2 emissions (g/km)",
    "WLTP CO2 emissions (g/km)",
    "absolute gap CO2 emissions (g/km)",
    "percentage gap CO2 emissions (%)",
    "OBFCM Fuel consumption weighted (l/100 km)",
    "WLTP Fuel consumption weighted (l/100 km)",
    "absolute gap Fuel consumption weighted (l/100 km)",
    "percentage gap Fuel consumption weighted (%)",
    "OBFCM CO2 emissions weighted (g/km)",
    "WLTP CO2 emissions weighted (g/km)",
    "absolute gap CO2 emissions weighted (g/km)",
    "percentage gap CO2 emissions weighted (%)",
}

# Deliberately reviewed, exact manufacturer-to-brand mappings only. Corporate groups
# that mix multiple brands (PSA, Stellantis, SAIC, JLR) remain unmapped.
REVIEWED_MANUFACTURER_BRAND_SLUGS: dict[str, str] = {
    "AUDI AG": "audi",
    "AUDI HUNGARIA": "audi",
    "AUDI SPORT": "audi",
    "BMW AG": "bmw",
    "BMW GMBH": "bmw",
    "FORD MOTOR AUSTRALIA": "ford",
    "FORD MOTOR COMPANY": "ford",
    "FORD WERKE GMBH": "ford",
    "GEELY": "geely",
    "HONDA MOTOR CO": "honda",
    "HYUNDAI": "hyundai",
    "HYUNDAI ASSAN": "hyundai",
    "HYUNDAI CZECH": "hyundai",
    "KIA": "kia",
    "KIA SLOVAKIA": "kia",
    "MAGYAR SUZUKI": "suzuki",
    "MAZDA": "mazda",
    "MAZDA EUROPE": "mazda",
    "MERCEDES AMG": "mercedes-benz",
    "MERCEDES-BENZ AG": "mercedes-benz",
    "MITSUBISHI MOTORS CORPORATION": "mitsubishi",
    "MITSUBISHI MOTORS THAILAND": "mitsubishi",
    "NISSAN AUTOMOTIVE EUROPE": "nissan",
    "AUTOMOBILES PEUGEOT": "peugeot",
    "PORSCHE": "porsche",
    "SKODA": "skoda",
    "SUBARU": "subaru",
    "SUZUKI MOTOR CORPORATION": "suzuki",
    "TOYOTA": "toyota",
    "TOYOTA MOTOR CORPORATION": "toyota",
    "VOLKSWAGEN": "volkswagen",
    "VOLVO": "volvo",
}


class RealWorldConsumptionRow(BaseModel):
    model_config = ConfigDict(extra="forbid")

    vehicle_registration_year: int = Field(ge=2000, le=2200)
    manufacturer: str = Field(min_length=1, max_length=240)
    normalized_manufacturer: str = Field(min_length=1, max_length=240)
    fuel_type: str = Field(min_length=1, max_length=80)
    sample_size: int = Field(gt=0)
    real_world_fuel_litres_per100km: Decimal | None
    official_wltp_fuel_litres_per100km: Decimal | None
    fuel_absolute_gap_litres_per100km: Decimal | None
    fuel_percentage_gap: Decimal | None
    real_world_co2_grams_per_km: Decimal | None
    official_wltp_co2_grams_per_km: Decimal | None
    co2_absolute_gap_grams_per_km: Decimal | None
    co2_percentage_gap: Decimal | None
    real_world_fuel_weighted_litres_per100km: Decimal | None
    official_wltp_fuel_weighted_litres_per100km: Decimal | None
    fuel_weighted_absolute_gap_litres_per100km: Decimal | None
    fuel_weighted_percentage_gap: Decimal | None
    real_world_co2_weighted_grams_per_km: Decimal | None
    official_wltp_co2_weighted_grams_per_km: Decimal | None
    co2_weighted_absolute_gap_grams_per_km: Decimal | None
    co2_weighted_percentage_gap: Decimal | None

    @model_validator(mode="after")
    def require_observed_and_official_metrics(self) -> "RealWorldConsumptionRow":
        if self.vehicle_registration_year > DATASET_REPORTING_YEAR:
            raise ValueError("Vehicle registration year cannot exceed the dataset reporting year")
        if self.real_world_fuel_litres_per100km is None or self.official_wltp_fuel_litres_per100km is None:
            raise ValueError("Every cohort requires separate OBFCM and WLTP fuel consumption")
        non_gap = (
            self.real_world_fuel_litres_per100km,
            self.official_wltp_fuel_litres_per100km,
            self.real_world_co2_grams_per_km,
            self.official_wltp_co2_grams_per_km,
            self.real_world_fuel_weighted_litres_per100km,
            self.official_wltp_fuel_weighted_litres_per100km,
            self.real_world_co2_weighted_grams_per_km,
            self.official_wltp_co2_weighted_grams_per_km,
        )
        if any(value is not None and value < 0 for value in non_gap):
            raise ValueError("Consumption and emission metrics cannot be negative")
        return self


def normalize_manufacturer(value: str) -> str:
    return " ".join(value.upper().split())


def parse_eea_aggregate(content: bytes) -> list[RealWorldConsumptionRow]:
    try:
        text = content.decode("utf-8-sig")
    except UnicodeDecodeError as error:
        raise ValueError("EEA aggregate CSV must be UTF-8 encoded") from error
    reader = csv.DictReader(StringIO(text, newline=""))
    headers = set(reader.fieldnames or [])
    missing = sorted(_HEADERS - headers)
    if missing:
        raise ValueError("EEA aggregate CSV is missing required columns: " + ", ".join(missing))

    rows: list[RealWorldConsumptionRow] = []
    identities: set[tuple[int, str, str]] = set()
    for line_number, raw in enumerate(reader, start=2):
        try:
            manufacturer = (raw["Manufacturer"] or "").strip()
            normalized = normalize_manufacturer(manufacturer)
            row = RealWorldConsumptionRow(
                vehicle_registration_year=int((raw["Year"] or "").strip()),
                manufacturer=manufacturer,
                normalized_manufacturer=normalized,
                fuel_type=normalize_manufacturer(raw["Fuel Type"] or ""),
                sample_size=int((raw["Number of vehicles"] or "").strip()),
                real_world_fuel_litres_per100km=_decimal(raw, "OBFCM Fuel consumption (l/100 km)"),
                official_wltp_fuel_litres_per100km=_decimal(raw, "WLTP Fuel consumption (l/100 km)"),
                fuel_absolute_gap_litres_per100km=_decimal(raw, "absolute gap Fuel consumption (l/100 km)"),
                fuel_percentage_gap=_decimal(raw, "percentage gap Fuel consumption (%)"),
                real_world_co2_grams_per_km=_decimal(raw, "OBFCM CO2 emissions (g/km)"),
                official_wltp_co2_grams_per_km=_decimal(raw, "WLTP CO2 emissions (g/km)"),
                co2_absolute_gap_grams_per_km=_decimal(raw, "absolute gap CO2 emissions (g/km)"),
                co2_percentage_gap=_decimal(raw, "percentage gap CO2 emissions (%)"),
                real_world_fuel_weighted_litres_per100km=_decimal(raw, "OBFCM Fuel consumption weighted (l/100 km)"),
                official_wltp_fuel_weighted_litres_per100km=_decimal(raw, "WLTP Fuel consumption weighted (l/100 km)"),
                fuel_weighted_absolute_gap_litres_per100km=_decimal(raw, "absolute gap Fuel consumption weighted (l/100 km)"),
                fuel_weighted_percentage_gap=_decimal(raw, "percentage gap Fuel consumption weighted (%)"),
                real_world_co2_weighted_grams_per_km=_decimal(raw, "OBFCM CO2 emissions weighted (g/km)"),
                official_wltp_co2_weighted_grams_per_km=_decimal(raw, "WLTP CO2 emissions weighted (g/km)"),
                co2_weighted_absolute_gap_grams_per_km=_decimal(raw, "absolute gap CO2 emissions weighted (g/km)"),
                co2_weighted_percentage_gap=_decimal(raw, "percentage gap CO2 emissions weighted (%)"),
            )
        except (KeyError, TypeError, ValueError) as error:
            raise ValueError(f"Invalid EEA aggregate row at CSV line {line_number}: {error}") from error
        identity = (row.vehicle_registration_year, row.normalized_manufacturer, row.fuel_type)
        if identity in identities:
            raise ValueError(f"Duplicate EEA manufacturer/fuel/year cohort at CSV line {line_number}")
        identities.add(identity)
        rows.append(row)
    if not rows:
        raise ValueError("EEA aggregate CSV contains no data rows")
    return rows


def _decimal(row: dict[str, str | None], key: str) -> Decimal | None:
    raw = (row.get(key) or "").strip()
    if not raw:
        return None
    try:
        return Decimal(raw)
    except InvalidOperation as error:
        raise ValueError(f"{key} must be decimal") from error


def stable_id(*parts: str) -> uuid.UUID:
    return uuid.uuid5(_NAMESPACE, "|".join(parts))


class RealWorldConsumptionPublisher:
    def __init__(self, dsn: str, storage: ObjectStorage) -> None:
        self._dsn = dsn
        self._storage = storage

    def publish(
        self,
        source: RegistrySource,
        snapshot: Snapshot,
        rows: list[RealWorldConsumptionRow],
    ) -> dict[str, Any]:
        if source.id != snapshot.source_id:
            raise ValueError("Snapshot source does not match the requested EEA registry source")
        content = self._storage.get_bytes(snapshot.object_key)
        if hashlib.sha256(content).hexdigest() != snapshot.content_hash:
            raise ValueError("EEA snapshot content hash does not match its immutable manifest")
        parsed_rows = parse_eea_aggregate(content)
        if parsed_rows != rows:
            raise ValueError("Published rows must be parsed from the immutable snapshot")

        with psycopg.connect(self._dsn) as connection, connection.transaction():
            source_id = self._upsert_source(connection, source, snapshot)
            snapshot_id = self._upsert_snapshot(connection, source_id, snapshot)
            brands = self._load_brands(connection)
            mapped_rows = 0
            mapped_slugs: set[str] = set()
            unmapped: set[str] = set()
            published_ids: list[uuid.UUID] = []
            for row in rows:
                slug = REVIEWED_MANUFACTURER_BRAND_SLUGS.get(row.normalized_manufacturer)
                brand_id = brands.get(slug) if slug else None
                if brand_id is None:
                    unmapped.add(row.normalized_manufacturer)
                else:
                    mapped_rows += 1
                    mapped_slugs.add(slug)
                published_ids.append(self._publish_row(connection, snapshot_id, row, brand_id))
            self._delete_stale_rows(connection, published_ids)
            audit_id = stable_id("real-world-consumption-audit", str(snapshot_id))
            now = snapshot.fetched_at
            with connection.cursor() as cursor:
                cursor.execute(
                    """
                    INSERT INTO audit_events
                        (id,actor,action,entity_type,entity_id,before_json,after_json,reason,
                         occurred_at,correlation_id,created_at,updated_at)
                    VALUES (%s,'worker:eea-obfcm','RealWorldConsumptionPublished','SourceSnapshot',%s,
                            NULL,%s,%s,%s,%s,%s,%s)
                    ON CONFLICT (id) DO NOTHING
                    """,
                    (
                        audit_id,
                        snapshot_id,
                        json.dumps({"rows": len(rows), "mapped_rows": mapped_rows, "dataset_version": DATASET_VERSION}),
                        "Published the official EEA aggregate without trim-level inference; only reviewed exact manufacturer mappings are linked.",
                        now,
                        f"real-world:{snapshot.content_hash[:16]}",
                        now,
                        now,
                    ),
                )
        return {
            "dataset_version": DATASET_VERSION,
            "rows": len(rows),
            "mapped_rows": mapped_rows,
            "mapped_brands": sorted(mapped_slugs),
            "unmapped_manufacturers": sorted(unmapped),
            "sample_size_total": sum(row.sample_size for row in rows),
            "vehicle_registration_years": sorted({row.vehicle_registration_year for row in rows}),
            "snapshot_id": str(snapshot_id),
            "audit_event_id": str(audit_id),
        }

    @staticmethod
    def _upsert_source(
        connection: psycopg.Connection[Any], source: RegistrySource, snapshot: Snapshot
    ) -> uuid.UUID:
        proposed = stable_id("source", source.url)
        with connection.cursor() as cursor:
            cursor.execute(
                """
                INSERT INTO sources
                    (id,name,url,domain,category,authority_level,content_type,robots_note,terms_note,
                     active,priority,refresh_interval,last_fetched_at,created_at,updated_at)
                VALUES (%s,%s,%s,%s,%s,%s,%s,%s,%s,TRUE,%s,%s,%s,%s,%s)
                ON CONFLICT (url) DO UPDATE SET name=EXCLUDED.name,domain=EXCLUDED.domain,
                    category=EXCLUDED.category,authority_level=EXCLUDED.authority_level,
                    content_type=EXCLUDED.content_type,robots_note=EXCLUDED.robots_note,
                    terms_note=EXCLUDED.terms_note,priority=EXCLUDED.priority,
                    refresh_interval=EXCLUDED.refresh_interval,last_fetched_at=EXCLUDED.last_fetched_at,
                    updated_at=EXCLUDED.updated_at
                RETURNING id
                """,
                (
                    proposed, source.name, source.url, source.allowed_domains[0], source.category,
                    source.authority.value, source.content_type.value, source.robots_note,
                    source.terms_note, source.priority, timedelta(hours=source.refresh_hours),
                    snapshot.fetched_at, snapshot.fetched_at, snapshot.fetched_at,
                ),
            )
            return cursor.fetchone()[0]

    @staticmethod
    def _upsert_snapshot(
        connection: psycopg.Connection[Any], source_id: uuid.UUID, snapshot: Snapshot
    ) -> uuid.UUID:
        proposed = stable_id("snapshot", str(source_id), snapshot.content_hash)
        last_modified = parsedate_to_datetime(snapshot.last_modified).astimezone(UTC) if snapshot.last_modified else None
        with connection.cursor() as cursor:
            cursor.execute(
                """
                INSERT INTO source_snapshots
                    (id,source_id,fetched_at,content_hash,object_key,http_status,parser_version,etag,
                     last_modified_at,fetch_error,created_at,updated_at)
                VALUES (%s,%s,%s,%s,%s,%s,'eea-obfcm-aggregate/3.3.0',%s,%s,NULL,%s,%s)
                ON CONFLICT (source_id,content_hash) DO UPDATE SET fetched_at=EXCLUDED.fetched_at,
                    parser_version=EXCLUDED.parser_version,etag=EXCLUDED.etag,
                    last_modified_at=EXCLUDED.last_modified_at,updated_at=EXCLUDED.updated_at
                RETURNING id
                """,
                (
                    proposed, source_id, snapshot.fetched_at, snapshot.content_hash,
                    snapshot.object_key, snapshot.http_status, snapshot.etag, last_modified,
                    snapshot.fetched_at, snapshot.fetched_at,
                ),
            )
            return cursor.fetchone()[0]

    @staticmethod
    def _load_brands(connection: psycopg.Connection[Any]) -> dict[str, uuid.UUID]:
        with connection.cursor() as cursor:
            cursor.execute("SELECT slug,id FROM brands WHERE active")
            return {row[0]: row[1] for row in cursor.fetchall()}

    @staticmethod
    def _publish_row(
        connection: psycopg.Connection[Any],
        snapshot_id: uuid.UUID,
        row: RealWorldConsumptionRow,
        brand_id: uuid.UUID | None,
    ) -> uuid.UUID:
        key = f"{DATASET_VERSION}|{row.vehicle_registration_year}|{row.normalized_manufacturer}|{row.fuel_type}"
        entity_id = stable_id("real-world-consumption", key)
        fact_id = stable_id("source-fact", str(snapshot_id), "RealWorldConsumptionAggregate", key)
        normalized = row.model_dump(mode="json")
        now = datetime.now(UTC)
        with connection.cursor() as cursor:
            cursor.execute(
                """
                INSERT INTO source_facts
                    (id,snapshot_id,entity_type,entity_id,field_path,raw_value,normalized_value,status,
                     confidence,extraction_context,created_at,updated_at)
                VALUES (%s,%s,'RealWorldConsumptionAggregate',%s,'aggregate',%s,%s,'Official',
                        'VerifiedOfficial',%s,%s,%s)
                ON CONFLICT (id) DO UPDATE SET raw_value=EXCLUDED.raw_value,
                    normalized_value=EXCLUDED.normalized_value,extraction_context=EXCLUDED.extraction_context,
                    updated_at=EXCLUDED.updated_at
                """,
                (
                    fact_id, snapshot_id, entity_id, json.dumps(normalized, ensure_ascii=False),
                    json.dumps(normalized, ensure_ascii=False),
                    "EEA-published manufacturer × fuel × registration-year aggregate; no trim-level inference.",
                    now, now,
                ),
            )
            cursor.execute(
                """
                INSERT INTO real_world_consumption_aggregates
                    (id,brand_id,dataset_reporting_year,vehicle_registration_year,dataset_version,
                     manufacturer,normalized_manufacturer,fuel_type,sample_size,
                     real_world_fuel_litres_per100km,official_wltp_fuel_litres_per100km,
                     fuel_absolute_gap_litres_per100km,fuel_percentage_gap,
                     real_world_co2_grams_per_km,official_wltp_co2_grams_per_km,
                     co2_absolute_gap_grams_per_km,co2_percentage_gap,
                     real_world_fuel_weighted_litres_per100km,official_wltp_fuel_weighted_litres_per100km,
                     fuel_weighted_absolute_gap_litres_per100km,fuel_weighted_percentage_gap,
                     real_world_co2_weighted_grams_per_km,official_wltp_co2_weighted_grams_per_km,
                     co2_weighted_absolute_gap_grams_per_km,co2_weighted_percentage_gap,
                     geography,aggregation_scope,methodology_url,attribution,source_fact_id,
                     manual_override_reason,created_at,updated_at)
                VALUES (%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,
                        %s,%s,%s,%s,%s,%s,NULL,%s,%s)
                ON CONFLICT (dataset_version,vehicle_registration_year,normalized_manufacturer,fuel_type)
                DO UPDATE SET brand_id=EXCLUDED.brand_id,sample_size=EXCLUDED.sample_size,
                    real_world_fuel_litres_per100km=EXCLUDED.real_world_fuel_litres_per100km,
                    official_wltp_fuel_litres_per100km=EXCLUDED.official_wltp_fuel_litres_per100km,
                    fuel_absolute_gap_litres_per100km=EXCLUDED.fuel_absolute_gap_litres_per100km,
                    fuel_percentage_gap=EXCLUDED.fuel_percentage_gap,
                    real_world_co2_grams_per_km=EXCLUDED.real_world_co2_grams_per_km,
                    official_wltp_co2_grams_per_km=EXCLUDED.official_wltp_co2_grams_per_km,
                    co2_absolute_gap_grams_per_km=EXCLUDED.co2_absolute_gap_grams_per_km,
                    co2_percentage_gap=EXCLUDED.co2_percentage_gap,
                    real_world_fuel_weighted_litres_per100km=EXCLUDED.real_world_fuel_weighted_litres_per100km,
                    official_wltp_fuel_weighted_litres_per100km=EXCLUDED.official_wltp_fuel_weighted_litres_per100km,
                    fuel_weighted_absolute_gap_litres_per100km=EXCLUDED.fuel_weighted_absolute_gap_litres_per100km,
                    fuel_weighted_percentage_gap=EXCLUDED.fuel_weighted_percentage_gap,
                    real_world_co2_weighted_grams_per_km=EXCLUDED.real_world_co2_weighted_grams_per_km,
                    official_wltp_co2_weighted_grams_per_km=EXCLUDED.official_wltp_co2_weighted_grams_per_km,
                    co2_weighted_absolute_gap_grams_per_km=EXCLUDED.co2_weighted_absolute_gap_grams_per_km,
                    co2_weighted_percentage_gap=EXCLUDED.co2_weighted_percentage_gap,
                    source_fact_id=EXCLUDED.source_fact_id,manual_override_reason=NULL,
                    methodology_url=EXCLUDED.methodology_url,attribution=EXCLUDED.attribution,
                    updated_at=EXCLUDED.updated_at
                """,
                (
                    entity_id, brand_id, DATASET_REPORTING_YEAR, row.vehicle_registration_year,
                    DATASET_VERSION, row.manufacturer, row.normalized_manufacturer, row.fuel_type,
                    row.sample_size, row.real_world_fuel_litres_per100km,
                    row.official_wltp_fuel_litres_per100km, row.fuel_absolute_gap_litres_per100km,
                    row.fuel_percentage_gap, row.real_world_co2_grams_per_km,
                    row.official_wltp_co2_grams_per_km, row.co2_absolute_gap_grams_per_km,
                    row.co2_percentage_gap, row.real_world_fuel_weighted_litres_per100km,
                    row.official_wltp_fuel_weighted_litres_per100km,
                    row.fuel_weighted_absolute_gap_litres_per100km,
                    row.fuel_weighted_percentage_gap, row.real_world_co2_weighted_grams_per_km,
                    row.official_wltp_co2_weighted_grams_per_km,
                    row.co2_weighted_absolute_gap_grams_per_km, row.co2_weighted_percentage_gap,
                    GEOGRAPHY, AGGREGATION_SCOPE, METHODOLOGY_URL, ATTRIBUTION, fact_id, now, now,
                ),
            )
        return entity_id

    @staticmethod
    def _delete_stale_rows(
        connection: psycopg.Connection[Any],
        published_ids: list[uuid.UUID],
    ) -> None:
        # The aggregate table is the current read model. Source snapshots and
        # source facts remain immutable history, while rows removed by a later
        # official revision must not survive as current cohorts.
        with connection.cursor() as cursor:
            cursor.execute(
                """
                DELETE FROM real_world_consumption_aggregates
                WHERE dataset_version = %s
                  AND NOT (id = ANY(%s::uuid[]))
                """,
                (DATASET_VERSION, published_ids),
            )
