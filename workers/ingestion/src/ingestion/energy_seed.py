from __future__ import annotations

import hashlib
import json
import uuid
from datetime import datetime, timedelta
from decimal import Decimal
from pathlib import Path
from typing import Any

import psycopg
from pydantic import BaseModel, ConfigDict, Field, model_validator

from ingestion.fetcher import Snapshot
from ingestion.registration_seed import stable_id
from ingestion.registry import SourceRegistry
from ingestion.storage import ObjectStorage


_ENERGY_TYPES = {"Ron92E5", "E10Ron95III", "Diesel", "Electricity"}
_PROMOTION_BENEFITS = {"Free", "PercentageDiscount", "FixedDiscount", "KwhCredit", "SessionCredit"}


class EffectiveSeed(BaseModel):
    model_config = ConfigDict(extra="forbid")

    key: str = Field(pattern=r"^[a-z0-9]+(?:-[a-z0-9]+)*$")
    effective_from: datetime
    effective_to: datetime | None = None
    source_id: str
    citation: str = Field(min_length=20)

    @model_validator(mode="after")
    def validate_period(self) -> "EffectiveSeed":
        if self.effective_from.tzinfo is None or (
            self.effective_to is not None and self.effective_to.tzinfo is None
        ):
            raise ValueError("Effective dates must include a timezone")
        if self.effective_to is not None and self.effective_from >= self.effective_to:
            raise ValueError("effective_to must be later than effective_from")
        return self


class EnergyPriceSeed(EffectiveSeed):
    energy_type: str
    provider: str = Field(min_length=1, max_length=200)
    region_code: str = "VN"
    amount: Decimal = Field(ge=0)
    unit: str = Field(min_length=1, max_length=40)
    currency: str = Field(default="VND", min_length=3, max_length=3)
    tier_from_inclusive: int = Field(default=0, ge=0)
    tier_to_inclusive: int | None = Field(default=None, ge=0)
    tax_rate: Decimal = Field(default=Decimal("0"), ge=0, le=1)
    tax_included: bool = True

    @model_validator(mode="after")
    def validate_energy_price(self) -> "EnergyPriceSeed":
        if self.energy_type not in _ENERGY_TYPES:
            raise ValueError(f"Unsupported energy type: {self.energy_type}")
        if self.tier_to_inclusive is not None and self.tier_to_inclusive <= self.tier_from_inclusive:
            raise ValueError("Energy tier upper boundary must be greater than its lower boundary")
        return self


class ChargingProviderSeed(BaseModel):
    model_config = ConfigDict(extra="forbid")

    key: str = Field(pattern=r"^[a-z0-9]+(?:-[a-z0-9]+)*$")
    name: str = Field(min_length=1, max_length=240)
    slug: str = Field(pattern=r"^[a-z0-9]+(?:-[a-z0-9]+)*$")
    network_type: str
    official_url: str
    source_id: str
    citation: str = Field(min_length=20)


class ChargingTariffSeed(EffectiveSeed):
    provider_key: str
    connector_type: str | None = None
    minimum_power_kw: Decimal | None = Field(default=None, ge=0)
    maximum_power_kw: Decimal | None = Field(default=None, ge=0)
    amount_per_kwh: Decimal | None = Field(default=None, ge=0)
    amount_per_session: Decimal | None = Field(default=None, ge=0)
    overstay_rules: dict[str, Any] = Field(default_factory=dict)
    overstay_cap_per_session: Decimal | None = Field(default=None, ge=0)
    tax_included: bool = True
    currency: str = Field(default="VND", min_length=3, max_length=3)
    region_scope: str = "VN"

    @model_validator(mode="after")
    def validate_tariff(self) -> "ChargingTariffSeed":
        if self.amount_per_kwh is None and self.amount_per_session is None and not self.overstay_rules:
            raise ValueError("Charging tariff needs a kWh, session, or overstay component")
        if self.minimum_power_kw is not None and self.maximum_power_kw is not None:
            if self.minimum_power_kw > self.maximum_power_kw:
                raise ValueError("Charging power band is reversed")
        return self


class ChargingPromotionSeed(EffectiveSeed):
    provider_key: str | None = None
    brand_slug: str | None = None
    model_slug: str | None = None
    benefit: str
    eligibility: dict[str, Any]
    caps: dict[str, Any]
    benefit_value: Decimal | None = Field(default=None, ge=0)
    currency: str | None = None

    @model_validator(mode="after")
    def validate_promotion(self) -> "ChargingPromotionSeed":
        if self.benefit not in _PROMOTION_BENEFITS:
            raise ValueError(f"Unsupported charging promotion benefit: {self.benefit}")
        if not any((self.provider_key, self.brand_slug, self.model_slug)):
            raise ValueError("Charging promotion must have provider, brand, or model scope")
        return self


class VehicleEnergyProfileSeed(BaseModel):
    model_config = ConfigDict(extra="forbid")

    key: str = Field(pattern=r"^[a-z0-9]+(?:-[a-z0-9]+)*$")
    brand_slug: str
    model_slug: str
    model_year: int = Field(ge=1950, le=2100)
    trim_slug: str
    recommended_fuel: str | None = None
    official_fuel_litres_per_100_km: Decimal | None = Field(default=None, gt=0)
    official_electric_kwh_per_100_km: Decimal | None = Field(default=None, gt=0)
    fuel_consumption_condition: str | None = None
    electric_consumption_condition: str | None = None
    usable_battery_kwh: Decimal | None = Field(default=None, gt=0)
    official_range_km: Decimal | None = Field(default=None, gt=0)
    test_cycle: str | None = None
    ac_max_kw: Decimal | None = Field(default=None, gt=0)
    dc_max_kw: Decimal | None = Field(default=None, gt=0)
    port_type: str | None = None
    consumption_notes: str = Field(min_length=20, max_length=2000)
    source_id: str
    citation: str = Field(min_length=20)

    @model_validator(mode="after")
    def validate_profile(self) -> "VehicleEnergyProfileSeed":
        if self.official_fuel_litres_per_100_km is None and self.official_electric_kwh_per_100_km is None:
            raise ValueError("Energy profile needs an official fuel or electric consumption fact")
        if self.official_fuel_litres_per_100_km is not None and not self.fuel_consumption_condition:
            raise ValueError("Fuel consumption must carry its test condition")
        if self.official_electric_kwh_per_100_km is not None and not self.electric_consumption_condition:
            raise ValueError("Electric consumption must carry its test condition")
        return self


class EnergySeedBatch(BaseModel):
    model_config = ConfigDict(extra="forbid")

    schema_version: str = "v1.6"
    observed_at: datetime
    reviewed_by: str = Field(min_length=3)
    review_reason: str = Field(min_length=20)
    energy_prices: list[EnergyPriceSeed] = Field(min_length=1)
    charging_providers: list[ChargingProviderSeed] = Field(min_length=1)
    charging_tariffs: list[ChargingTariffSeed] = Field(min_length=1)
    charging_promotions: list[ChargingPromotionSeed] = Field(min_length=1)
    vehicle_profiles: list[VehicleEnergyProfileSeed] = Field(min_length=1)

    @model_validator(mode="after")
    def validate_batch(self) -> "EnergySeedBatch":
        if self.observed_at.tzinfo is None:
            raise ValueError("observed_at must include a timezone")
        keyed = [
            *self.energy_prices,
            *self.charging_providers,
            *self.charging_tariffs,
            *self.charging_promotions,
            *self.vehicle_profiles,
        ]
        keys = [item.key for item in keyed]
        if len(keys) != len(set(keys)):
            raise ValueError("V1.6 energy seed keys must be globally unique")
        provider_keys = {provider.key for provider in self.charging_providers}
        referenced = {
            *(tariff.provider_key for tariff in self.charging_tariffs),
            *(promotion.provider_key for promotion in self.charging_promotions if promotion.provider_key),
        }
        unknown = sorted(referenced - provider_keys)
        if unknown:
            raise ValueError("Unknown charging provider keys: " + ", ".join(unknown))
        return self

    @property
    def source_ids(self) -> set[str]:
        return {
            *(item.source_id for item in self.energy_prices),
            *(item.source_id for item in self.charging_providers),
            *(item.source_id for item in self.charging_tariffs),
            *(item.source_id for item in self.charging_promotions),
            *(item.source_id for item in self.vehicle_profiles),
        }


def load_energy_seed(path: Path) -> EnergySeedBatch:
    return EnergySeedBatch.model_validate_json(path.read_text(encoding="utf-8"))


def validate_energy_seed(batch: EnergySeedBatch, registry: SourceRegistry) -> dict[str, Any]:
    registered = {source.id for source in registry.sources}
    unknown = sorted(batch.source_ids - registered)
    if unknown:
        raise ValueError("Unknown energy source IDs: " + ", ".join(unknown))
    electricity = [price for price in batch.energy_prices if price.energy_type == "Electricity"]
    fuel = [price for price in batch.energy_prices if price.energy_type != "Electricity"]
    if {price.energy_type for price in fuel} != {"Ron92E5", "E10Ron95III", "Diesel"}:
        raise ValueError("V1.6 current fuel seed must include E5RON92, E10RON95-III, and diesel")
    if len(electricity) != 6:
        raise ValueError("V1.6 household tariff must contain exactly six tiers")
    boundaries = [(price.tier_from_inclusive, price.tier_to_inclusive) for price in sorted(electricity, key=lambda item: item.tier_from_inclusive)]
    if boundaries != [(0, 50), (50, 100), (100, 200), (200, 300), (300, 400), (400, None)]:
        raise ValueError("Household tariff boundaries must cover the canonical six EVN marginal tiers")
    return {
        "passed": True,
        "schema_version": batch.schema_version,
        "fuel_prices": len(fuel),
        "electricity_tiers": len(electricity),
        "charging_tariffs": len(batch.charging_tariffs),
        "charging_promotions": len(batch.charging_promotions),
        "vehicle_profiles": len(batch.vehicle_profiles),
        "sources": len(batch.source_ids),
    }


class EnergySeedPublisher:
    def __init__(self, dsn: str, storage: ObjectStorage) -> None:
        self._dsn = dsn
        self._storage = storage

    def publish(
        self,
        batch: EnergySeedBatch,
        registry: SourceRegistry,
        snapshots: dict[str, Snapshot],
    ) -> dict[str, Any]:
        validate_energy_seed(batch, registry)
        missing = sorted(batch.source_ids - snapshots.keys())
        if missing:
            raise ValueError("Missing immutable snapshots for: " + ", ".join(missing))
        for source_id in batch.source_ids:
            snapshot = snapshots[source_id]
            content = self._storage.get_bytes(snapshot.object_key)
            if hashlib.sha256(content).hexdigest() != snapshot.content_hash:
                raise ValueError(f"Snapshot content hash mismatch for {source_id}")

        with psycopg.connect(self._dsn) as connection, connection.transaction():
            source_ids = self._upsert_sources(connection, batch, registry, snapshots)
            snapshot_ids = self._upsert_snapshots(connection, batch, source_ids, snapshots)
            providers = {
                provider.key: self._publish_provider(connection, batch, provider, snapshot_ids[provider.source_id])
                for provider in batch.charging_providers
            }
            for price in batch.energy_prices:
                self._publish_energy_price(connection, batch, price, snapshot_ids[price.source_id])
            for tariff in batch.charging_tariffs:
                self._publish_tariff(connection, batch, tariff, providers[tariff.provider_key], snapshot_ids[tariff.source_id])
            for promotion in batch.charging_promotions:
                self._publish_promotion(connection, batch, promotion, providers, snapshot_ids[promotion.source_id])
            for profile in batch.vehicle_profiles:
                self._publish_profile(connection, batch, profile, snapshot_ids[profile.source_id])
            audit_id = self._publish_audit(connection, batch)

        return {
            "energy_prices": len(batch.energy_prices),
            "charging_providers": len(batch.charging_providers),
            "charging_tariffs": len(batch.charging_tariffs),
            "charging_promotions": len(batch.charging_promotions),
            "vehicle_profiles": len(batch.vehicle_profiles),
            "snapshots": len(snapshot_ids),
            "audit_event_id": str(audit_id),
        }

    @staticmethod
    def _upsert_sources(
        connection: psycopg.Connection[Any],
        batch: EnergySeedBatch,
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
                        source_id, source.name, source.url, source.allowed_domains[0], source.authority.value,
                        source.content_type.value, source.robots_note, source.terms_note, source.priority,
                        timedelta(hours=source.refresh_hours), snapshot.fetched_at, batch.observed_at, batch.observed_at,
                    ),
                )
                result[registry_id] = cursor.fetchone()[0]
        return result

    @staticmethod
    def _upsert_snapshots(
        connection: psycopg.Connection[Any],
        batch: EnergySeedBatch,
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
                    VALUES (%s, %s, %s, %s, %s, %s, 'energy-seed/v1.6', %s, NULL, NULL, %s, %s)
                    ON CONFLICT (source_id, content_hash) DO NOTHING
                    """,
                    (snapshot_id, source_id, snapshot.fetched_at, snapshot.content_hash, snapshot.object_key,
                     snapshot.http_status, snapshot.etag, batch.observed_at, batch.observed_at),
                )
                cursor.execute(
                    "SELECT id FROM source_snapshots WHERE source_id = %s AND content_hash = %s",
                    (source_id, snapshot.content_hash),
                )
                result[registry_id] = cursor.fetchone()[0]
        return result

    @staticmethod
    def _publish_fact(
        connection: psycopg.Connection[Any],
        batch: EnergySeedBatch,
        snapshot_id: uuid.UUID,
        entity_type: str,
        entity_id: uuid.UUID,
        key: str,
        citation: str,
        normalized: dict[str, Any],
        context: str,
    ) -> uuid.UUID:
        fact_id = stable_id("source-fact", str(snapshot_id), entity_type, key)
        with connection.cursor() as cursor:
            cursor.execute(
                """
                INSERT INTO source_facts
                    (id, snapshot_id, entity_type, entity_id, field_path, raw_value, normalized_value,
                     status, confidence, extraction_context, created_at, updated_at)
                VALUES (%s, %s, %s, %s, 'energy', %s, %s, 'Official', 'VerifiedOfficial', %s, %s, %s)
                ON CONFLICT (id) DO UPDATE SET raw_value = EXCLUDED.raw_value,
                    normalized_value = EXCLUDED.normalized_value, extraction_context = EXCLUDED.extraction_context,
                    updated_at = EXCLUDED.updated_at
                """,
                (fact_id, snapshot_id, entity_type, entity_id, citation,
                 json.dumps(normalized, ensure_ascii=False, default=str), context,
                 batch.observed_at, batch.observed_at),
            )
        return fact_id

    @staticmethod
    def _publish_energy_price(
        connection: psycopg.Connection[Any],
        batch: EnergySeedBatch,
        price: EnergyPriceSeed,
        snapshot_id: uuid.UUID,
    ) -> None:
        entity_id = stable_id("energy-price", price.key)
        fact_id = EnergySeedPublisher._publish_fact(
            connection, batch, snapshot_id, "EnergyPrice", entity_id, price.key, price.citation,
            price.model_dump(mode="json"), f"Reviewed V1.6 effective energy price: {price.key}",
        )
        with connection.cursor() as cursor:
            cursor.execute(
                """
                INSERT INTO energy_prices
                    (id, energy_type, provider, region_code, amount, unit, currency,
                     tier_from_inclusive, tier_to_inclusive, tax_rate, tax_included,
                     effective_from, effective_to, source_fact_id, manual_override_reason, created_at, updated_at)
                VALUES (%s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, NULL, %s, %s)
                ON CONFLICT (id) DO UPDATE SET energy_type = EXCLUDED.energy_type, provider = EXCLUDED.provider,
                    region_code = EXCLUDED.region_code, amount = EXCLUDED.amount, unit = EXCLUDED.unit,
                    currency = EXCLUDED.currency, tier_from_inclusive = EXCLUDED.tier_from_inclusive,
                    tier_to_inclusive = EXCLUDED.tier_to_inclusive, tax_rate = EXCLUDED.tax_rate,
                    tax_included = EXCLUDED.tax_included, effective_from = EXCLUDED.effective_from,
                    effective_to = EXCLUDED.effective_to, source_fact_id = EXCLUDED.source_fact_id,
                    manual_override_reason = NULL, updated_at = EXCLUDED.updated_at
                """,
                (entity_id, price.energy_type, price.provider, price.region_code, price.amount,
                 price.unit, price.currency, price.tier_from_inclusive, price.tier_to_inclusive,
                 price.tax_rate, price.tax_included, price.effective_from, price.effective_to,
                 fact_id, batch.observed_at, batch.observed_at),
            )

    @staticmethod
    def _publish_provider(
        connection: psycopg.Connection[Any],
        batch: EnergySeedBatch,
        provider: ChargingProviderSeed,
        snapshot_id: uuid.UUID,
    ) -> uuid.UUID:
        entity_id = stable_id("charging-provider", provider.key)
        fact_id = EnergySeedPublisher._publish_fact(
            connection, batch, snapshot_id, "ChargingProvider", entity_id, provider.key,
            provider.citation, provider.model_dump(mode="json"), f"Reviewed V1.6 provider: {provider.key}",
        )
        with connection.cursor() as cursor:
            cursor.execute(
                """
                INSERT INTO charging_providers
                    (id, name, slug, network_type, official_url, source_fact_id, manual_override_reason, created_at, updated_at)
                VALUES (%s, %s, %s, %s, %s, %s, NULL, %s, %s)
                ON CONFLICT (slug) DO UPDATE SET name = EXCLUDED.name, network_type = EXCLUDED.network_type,
                    official_url = EXCLUDED.official_url, source_fact_id = EXCLUDED.source_fact_id,
                    manual_override_reason = NULL, updated_at = EXCLUDED.updated_at
                RETURNING id
                """,
                (entity_id, provider.name, provider.slug, provider.network_type, provider.official_url,
                 fact_id, batch.observed_at, batch.observed_at),
            )
            return cursor.fetchone()[0]

    @staticmethod
    def _publish_tariff(
        connection: psycopg.Connection[Any],
        batch: EnergySeedBatch,
        tariff: ChargingTariffSeed,
        provider_id: uuid.UUID,
        snapshot_id: uuid.UUID,
    ) -> None:
        entity_id = stable_id("charging-tariff", tariff.key)
        fact_id = EnergySeedPublisher._publish_fact(
            connection, batch, snapshot_id, "ChargingTariff", entity_id, tariff.key,
            tariff.citation, tariff.model_dump(mode="json"), f"Reviewed V1.6 tariff: {tariff.key}",
        )
        with connection.cursor() as cursor:
            cursor.execute(
                """
                INSERT INTO charging_tariffs
                    (id, provider_id, connector_type, minimum_power_kw, maximum_power_kw,
                     amount_per_kwh, amount_per_session, overstay_amount_per_minute,
                     overstay_rules_json, overstay_cap_per_session, tax_included,
                     currency, region_scope, effective_from, effective_to, source_fact_id,
                     manual_override_reason, created_at, updated_at)
                VALUES (%s, %s, %s, %s, %s, %s, %s, NULL, %s::jsonb, %s, %s, %s, %s, %s, %s, %s, NULL, %s, %s)
                ON CONFLICT (id) DO UPDATE SET provider_id = EXCLUDED.provider_id,
                    connector_type = EXCLUDED.connector_type, minimum_power_kw = EXCLUDED.minimum_power_kw,
                    maximum_power_kw = EXCLUDED.maximum_power_kw, amount_per_kwh = EXCLUDED.amount_per_kwh,
                    amount_per_session = EXCLUDED.amount_per_session,
                    overstay_rules_json = EXCLUDED.overstay_rules_json,
                    overstay_cap_per_session = EXCLUDED.overstay_cap_per_session,
                    tax_included = EXCLUDED.tax_included, currency = EXCLUDED.currency,
                    region_scope = EXCLUDED.region_scope, effective_from = EXCLUDED.effective_from,
                    effective_to = EXCLUDED.effective_to, source_fact_id = EXCLUDED.source_fact_id,
                    manual_override_reason = NULL, updated_at = EXCLUDED.updated_at
                """,
                (entity_id, provider_id, tariff.connector_type, tariff.minimum_power_kw,
                 tariff.maximum_power_kw, tariff.amount_per_kwh, tariff.amount_per_session,
                 json.dumps(tariff.overstay_rules), tariff.overstay_cap_per_session,
                 tariff.tax_included, tariff.currency, tariff.region_scope, tariff.effective_from,
                 tariff.effective_to, fact_id, batch.observed_at, batch.observed_at),
            )

    @staticmethod
    def _publish_promotion(
        connection: psycopg.Connection[Any],
        batch: EnergySeedBatch,
        promotion: ChargingPromotionSeed,
        providers: dict[str, uuid.UUID],
        snapshot_id: uuid.UUID,
    ) -> None:
        entity_id = stable_id("charging-promotion", promotion.key)
        fact_id = EnergySeedPublisher._publish_fact(
            connection, batch, snapshot_id, "ChargingPromotion", entity_id, promotion.key,
            promotion.citation, promotion.model_dump(mode="json"), f"Reviewed V1.6 promotion: {promotion.key}",
        )
        provider_id = providers.get(promotion.provider_key or "")
        brand_id: uuid.UUID | None = None
        model_id: uuid.UUID | None = None
        with connection.cursor() as cursor:
            if promotion.brand_slug:
                cursor.execute("SELECT id FROM brands WHERE slug = %s", (promotion.brand_slug,))
                row = cursor.fetchone()
                if row is None:
                    raise ValueError(f"Promotion brand not found: {promotion.brand_slug}")
                brand_id = row[0]
            if promotion.model_slug:
                cursor.execute(
                    "SELECT id FROM models WHERE slug = %s AND (%s::uuid IS NULL OR brand_id = %s)",
                    (promotion.model_slug, brand_id, brand_id),
                )
                row = cursor.fetchone()
                if row is None:
                    raise ValueError(f"Promotion model not found: {promotion.model_slug}")
                model_id = row[0]
            cursor.execute(
                """
                INSERT INTO charging_promotions
                    (id, provider_id, brand_id, model_id, benefit, eligibility_json, caps_json,
                     benefit_value, currency, effective_from, effective_to, source_fact_id,
                     manual_override_reason, created_at, updated_at)
                VALUES (%s, %s, %s, %s, %s, %s::jsonb, %s::jsonb, %s, %s, %s, %s, %s, NULL, %s, %s)
                ON CONFLICT (id) DO UPDATE SET provider_id = EXCLUDED.provider_id,
                    brand_id = EXCLUDED.brand_id, model_id = EXCLUDED.model_id, benefit = EXCLUDED.benefit,
                    eligibility_json = EXCLUDED.eligibility_json, caps_json = EXCLUDED.caps_json,
                    benefit_value = EXCLUDED.benefit_value, currency = EXCLUDED.currency,
                    effective_from = EXCLUDED.effective_from, effective_to = EXCLUDED.effective_to,
                    source_fact_id = EXCLUDED.source_fact_id, manual_override_reason = NULL,
                    updated_at = EXCLUDED.updated_at
                """,
                (entity_id, provider_id, brand_id, model_id, promotion.benefit,
                 json.dumps(promotion.eligibility), json.dumps(promotion.caps), promotion.benefit_value,
                 promotion.currency, promotion.effective_from, promotion.effective_to, fact_id,
                 batch.observed_at, batch.observed_at),
            )

    @staticmethod
    def _publish_profile(
        connection: psycopg.Connection[Any],
        batch: EnergySeedBatch,
        profile: VehicleEnergyProfileSeed,
        snapshot_id: uuid.UUID,
    ) -> None:
        with connection.cursor() as cursor:
            cursor.execute(
                """
                SELECT t.id
                FROM trims t
                JOIN model_years my ON my.id = t.model_year_id
                JOIN generations g ON g.id = my.generation_id
                JOIN models m ON m.id = g.model_id
                JOIN brands b ON b.id = m.brand_id
                WHERE b.slug = %s AND m.slug = %s AND my.year = %s AND t.slug = %s
                """,
                (profile.brand_slug, profile.model_slug, profile.model_year, profile.trim_slug),
            )
            row = cursor.fetchone()
            if row is None:
                raise ValueError(f"Energy profile trim not found: {profile.key}")
            trim_id: uuid.UUID = row[0]
        entity_id = stable_id("energy-profile", profile.key)
        fact_id = EnergySeedPublisher._publish_fact(
            connection, batch, snapshot_id, "EnergyProfile", entity_id, profile.key,
            profile.citation, profile.model_dump(mode="json"), f"Reviewed V1.6 vehicle energy profile: {profile.key}",
        )
        with connection.cursor() as cursor:
            cursor.execute(
                """
                INSERT INTO energy_profiles
                    (id, trim_id, recommended_fuel, official_fuel_litres_per100km,
                     official_electric_kwh_per100km, fuel_consumption_condition,
                     electric_consumption_condition, usable_battery_kwh, official_range_km,
                     test_cycle, ac_max_kw, dc_max_kw, port_type, consumption_notes,
                     source_fact_id, manual_override_reason, created_at, updated_at)
                VALUES (%s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, NULL, %s, %s)
                ON CONFLICT (trim_id) DO UPDATE SET recommended_fuel = EXCLUDED.recommended_fuel,
                    official_fuel_litres_per100km = EXCLUDED.official_fuel_litres_per100km,
                    official_electric_kwh_per100km = EXCLUDED.official_electric_kwh_per100km,
                    fuel_consumption_condition = EXCLUDED.fuel_consumption_condition,
                    electric_consumption_condition = EXCLUDED.electric_consumption_condition,
                    usable_battery_kwh = EXCLUDED.usable_battery_kwh,
                    official_range_km = EXCLUDED.official_range_km, test_cycle = EXCLUDED.test_cycle,
                    ac_max_kw = EXCLUDED.ac_max_kw, dc_max_kw = EXCLUDED.dc_max_kw,
                    port_type = EXCLUDED.port_type, consumption_notes = EXCLUDED.consumption_notes,
                    source_fact_id = EXCLUDED.source_fact_id, manual_override_reason = NULL,
                    updated_at = EXCLUDED.updated_at
                """,
                (entity_id, trim_id, profile.recommended_fuel,
                 profile.official_fuel_litres_per_100_km, profile.official_electric_kwh_per_100_km,
                 profile.fuel_consumption_condition, profile.electric_consumption_condition,
                 profile.usable_battery_kwh, profile.official_range_km, profile.test_cycle,
                 profile.ac_max_kw, profile.dc_max_kw, profile.port_type, profile.consumption_notes,
                 fact_id, batch.observed_at, batch.observed_at),
            )

    @staticmethod
    def _publish_audit(connection: psycopg.Connection[Any], batch: EnergySeedBatch) -> uuid.UUID:
        audit_id = stable_id("audit", "energy-seed", batch.observed_at.isoformat())
        summary = {
            "energy_prices": len(batch.energy_prices),
            "charging_tariffs": len(batch.charging_tariffs),
            "charging_promotions": len(batch.charging_promotions),
            "vehicle_profiles": len(batch.vehicle_profiles),
        }
        with connection.cursor() as cursor:
            cursor.execute(
                """
                INSERT INTO audit_events
                    (id, actor, action, entity_type, entity_id, before_json, after_json, reason,
                     occurred_at, correlation_id, created_at, updated_at)
                VALUES (%s, %s, 'energy-seed.publish', 'EnergySeed', %s, NULL, %s::jsonb,
                        %s, %s, %s, %s, %s)
                ON CONFLICT (id) DO NOTHING
                """,
                (audit_id, batch.reviewed_by, stable_id("energy-seed", batch.schema_version),
                 json.dumps(summary), batch.review_reason, batch.observed_at,
                 f"energy-seed-{batch.observed_at:%Y%m%d}", batch.observed_at, batch.observed_at),
            )
        return audit_id
