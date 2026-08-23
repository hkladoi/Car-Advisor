from __future__ import annotations

import csv
import json
from datetime import datetime
from enum import StrEnum
from pathlib import Path
from typing import Any
from urllib.parse import urlsplit

from pydantic import BaseModel, ConfigDict, Field, model_validator


class FactStatus(StrEnum):
    UNKNOWN = "Unknown"
    NOT_AVAILABLE = "NotAvailable"
    NOT_APPLICABLE = "NotApplicable"
    EXPECTED = "Expected"
    OFFICIAL = "Official"


class Confidence(StrEnum):
    UNKNOWN = "Unknown"
    ESTIMATED = "Estimated"
    TRUSTED_SINGLE_SOURCE = "TrustedSingleSource"
    VERIFIED_MULTI_SOURCE = "VerifiedMultiSource"
    VERIFIED_OFFICIAL = "VerifiedOfficial"


class CoreFact(BaseModel):
    model_config = ConfigDict(extra="forbid")

    status: FactStatus
    confidence: Confidence
    value: str | int | float | bool | None = None
    raw_value: str | None = None

    @model_validator(mode="after")
    def enforce_unknown_semantics(self) -> "CoreFact":
        has_value = self.value is not None
        if self.status in {FactStatus.OFFICIAL, FactStatus.EXPECTED} and not has_value:
            raise ValueError(f"{self.status} facts require a value")
        if self.status in {
            FactStatus.UNKNOWN,
            FactStatus.NOT_AVAILABLE,
            FactStatus.NOT_APPLICABLE,
        } and has_value:
            raise ValueError(f"{self.status} facts cannot carry a concrete value")
        if self.status is FactStatus.UNKNOWN and self.confidence is not Confidence.UNKNOWN:
            raise ValueError("Unknown facts must use Unknown confidence")
        return self


class PriceFact(BaseModel):
    model_config = ConfigDict(extra="forbid")

    status: FactStatus
    confidence: Confidence
    price_type: str = "Msrp"
    amount: int | None = Field(default=None, ge=0)
    currency: str = Field(default="VND", min_length=3, max_length=3)
    effective_from: datetime
    raw_value: str | None = None

    @model_validator(mode="after")
    def enforce_price_semantics(self) -> "PriceFact":
        if self.effective_from.tzinfo is None or self.effective_from.utcoffset() is None:
            raise ValueError("price effective_from must include a timezone")
        if self.price_type not in {"Msrp", "Unannounced"}:
            raise ValueError("Seed price must be Msrp or Unannounced")
        if self.price_type == "Msrp":
            if self.status is not FactStatus.OFFICIAL or not self.amount:
                raise ValueError("MSRP requires an official positive amount")
            if self.confidence not in {
                Confidence.TRUSTED_SINGLE_SOURCE,
                Confidence.VERIFIED_MULTI_SOURCE,
                Confidence.VERIFIED_OFFICIAL,
            }:
                raise ValueError("MSRP requires trusted or verified confidence")
        elif self.amount is not None:
            raise ValueError("Unannounced price cannot carry an amount")
        return self


class CoreFields(BaseModel):
    model_config = ConfigDict(extra="forbid")

    body_type: CoreFact
    segment: CoreFact
    market_status: CoreFact
    powertrain: CoreFact
    seats: CoreFact
    length_mm: CoreFact
    width_mm: CoreFact
    height_mm: CoreFact
    wheelbase_mm: CoreFact
    price: PriceFact

    def facts(self) -> dict[str, CoreFact | PriceFact]:
        return {name: getattr(self, name) for name in type(self).model_fields}


_CANONICAL_FEATURE_CODES = {
    "ACC", "AEB", "FCW", "LKA", "LCC_LFA", "BSD", "RCTA", "TSR",
    "REMOTE_START", "REMOTE_CLIMATE", "APP_CONTROL", "HUD", "CAMERA_360",
    "VENTILATED_FRONT", "HEATED_REAR", "SEAT_MEMORY", "PANORAMIC_ROOF",
}


class FeatureSeedFact(BaseModel):
    model_config = ConfigDict(extra="forbid")

    code: str = Field(pattern=r"^[A-Z0-9_]+$", max_length=100)
    label: str = Field(min_length=1, max_length=200)
    group: str = Field(min_length=1, max_length=100)
    fact: CoreFact

    @model_validator(mode="after")
    def enforce_canonical_boolean_feature(self) -> "FeatureSeedFact":
        if self.code not in _CANONICAL_FEATURE_CODES:
            raise ValueError(f"Unsupported canonical feature code: {self.code}")
        if self.fact.value is not None and not isinstance(self.fact.value, bool):
            raise ValueError("V1.3 seed features must be canonical booleans")
        return self


class VehicleSeedRecord(BaseModel):
    model_config = ConfigDict(extra="forbid")

    brand_name: str = Field(min_length=1, max_length=160)
    brand_slug: str = Field(pattern=r"^[a-z0-9]+(?:-[a-z0-9]+)*$")
    brand_country_code: str = Field(min_length=2, max_length=2)
    brand_official_url: str
    model_name: str = Field(min_length=1, max_length=160)
    model_slug: str = Field(pattern=r"^[a-z0-9]+(?:-[a-z0-9]+)*$")
    generation_code: str = Field(min_length=1, max_length=80)
    generation_start_year: int = Field(ge=1950, le=2100)
    model_year: int = Field(ge=1950, le=2100)
    trim_name: str = Field(min_length=1, max_length=240)
    trim_slug: str = Field(pattern=r"^[a-z0-9]+(?:-[a-z0-9]+)*$")
    source_id: str = Field(pattern=r"^[a-z0-9]+(?:-[a-z0-9]+)*$")
    source_url: str
    core: CoreFields
    features: list[FeatureSeedFact] = Field(default_factory=list)

    @model_validator(mode="after")
    def validate_urls_and_enums(self) -> "VehicleSeedRecord":
        for label, value in {
            "brand_official_url": self.brand_official_url,
            "source_url": self.source_url,
        }.items():
            parsed = urlsplit(value)
            if parsed.scheme != "https" or not parsed.hostname:
                raise ValueError(f"{label} must be an absolute HTTPS URL")

        allowed = {
            "body_type": {"Sedan", "Hatchback", "Suv", "Crossover", "Mpv", "Pickup", "Coupe", "Convertible", "Van", "Wagon"},
            "segment": {"A", "B", "C", "D", "E", "F", "Luxury", "Utility"},
            "market_status": {"Upcoming", "Announced", "Active", "Discontinued"},
            "powertrain": {"Ice", "Hev", "Phev", "Erev", "Bev"},
        }
        for name, values in allowed.items():
            fact = getattr(self.core, name)
            if fact.value is not None and fact.value not in values:
                raise ValueError(f"Unsupported {name}: {fact.value}")
        for name in ("seats", "length_mm", "width_mm", "height_mm", "wheelbase_mm"):
            value = getattr(self.core, name).value
            if value is not None and (not isinstance(value, (int, float)) or value <= 0):
                raise ValueError(f"{name} must be positive when known")
        feature_codes = [feature.code for feature in self.features]
        if len(feature_codes) != len(set(feature_codes)):
            raise ValueError("Duplicate canonical feature code on one trim")
        return self

    @property
    def normalized_identity(self) -> tuple[str, str, int, str]:
        return (self.brand_slug, self.model_slug, self.model_year, self.trim_slug)


class ManualImportBatch(BaseModel):
    model_config = ConfigDict(extra="forbid")

    schema_version: str = "v1.2"
    market: str = "VN"
    observed_at: datetime
    reviewed_by: str = Field(min_length=3, max_length=320)
    review_reason: str = Field(min_length=10, max_length=2000)
    records: list[VehicleSeedRecord] = Field(min_length=1)

    @model_validator(mode="after")
    def reject_duplicates(self) -> "ManualImportBatch":
        if self.observed_at.tzinfo is None or self.observed_at.utcoffset() is None:
            raise ValueError("observed_at must include a timezone")
        identities = [record.normalized_identity for record in self.records]
        if len(identities) != len(set(identities)):
            raise ValueError("Duplicate model-year/trim identity in import batch")
        return self


def load_manual_import(path: Path) -> ManualImportBatch:
    suffix = path.suffix.lower()
    if suffix == ".json":
        return ManualImportBatch.model_validate_json(path.read_text(encoding="utf-8"))
    if suffix == ".csv":
        with path.open(encoding="utf-8-sig", newline="") as handle:
            rows = list(csv.DictReader(handle))
        return ManualImportBatch.model_validate(_batch_from_csv(rows))
    raise ValueError("Manual import must be JSON or CSV")


def _batch_from_csv(rows: list[dict[str, str]]) -> dict[str, Any]:
    if not rows:
        raise ValueError("CSV import is empty")
    metadata = rows[0]
    records: list[dict[str, Any]] = []
    core_names = ("body_type", "segment", "market_status", "powertrain", "seats", "length_mm", "width_mm", "height_mm", "wheelbase_mm")
    for row in rows:
        core: dict[str, Any] = {}
        for name in core_names:
            raw = (row.get(name) or "").strip()
            core[name] = (
                {"status": "Unknown", "confidence": "Unknown"}
                if not raw
                else {
                    "status": "Official",
                    "confidence": "TrustedSingleSource",
                    "value": _parse_number(raw),
                    "raw_value": raw,
                }
            )
        price_raw = (row.get("msrp_amount") or "").strip()
        price_type = (row.get("price_type") or "").strip()
        price_effective_from = (row.get("price_effective_from") or "").strip()
        if not price_effective_from:
            raise ValueError("CSV price_effective_from is required")
        if price_type == "Msrp" and price_raw:
            core["price"] = {"status": "Official", "confidence": "TrustedSingleSource", "price_type": "Msrp", "amount": int(price_raw), "effective_from": price_effective_from, "raw_value": price_raw}
        elif price_type == "Unannounced" and not price_raw:
            core["price"] = {"status": "Official", "confidence": "VerifiedOfficial", "price_type": "Unannounced", "effective_from": price_effective_from, "raw_value": "UNANNOUNCED"}
        else:
            raise ValueError("CSV price_type must be Msrp with an amount or explicit Unannounced without an amount")
        records.append({key: value for key, value in row.items() if key not in {*core_names, "price_type", "price_effective_from", "msrp_amount", "observed_at", "reviewed_by", "review_reason"}} | {"core": core})
    return {
        "schema_version": "v1.2",
        "market": "VN",
        "observed_at": metadata.get("observed_at"),
        "reviewed_by": metadata.get("reviewed_by"),
        "review_reason": metadata.get("review_reason"),
        "records": records,
    }


def _parse_number(raw: str) -> str | int | float:
    try:
        return int(raw)
    except ValueError:
        try:
            return float(raw)
        except ValueError:
            return raw
