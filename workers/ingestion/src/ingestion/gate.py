from __future__ import annotations

import re
import unicodedata
from dataclasses import dataclass

from ingestion.contracts import Confidence, FactStatus, ManualImportBatch
from ingestion.registry import Authority, SourceRegistry


@dataclass(frozen=True, slots=True)
class SeedGateReport:
    passed: bool
    seeded_brands: int
    seeded_trims: int
    core_fact_count: int
    transparent_core_fact_count: int
    transparency_ratio: float
    duplicate_count: int
    price_gate_failures: tuple[str, ...]
    errors: tuple[str, ...]


def evaluate_seed_gate(batch: ManualImportBatch, registry: SourceRegistry) -> SeedGateReport:
    errors: list[str] = []
    price_failures: list[str] = []
    identities: set[tuple[str, str, int, str]] = set()
    duplicates = 0
    transparent = 0
    total = 0

    for record in batch.records:
        source = registry.by_id(record.source_id)
        if source.url != record.source_url:
            errors.append(f"{record.trim_name}: source URL does not match registry ID {record.source_id}")
        if source.authority is Authority.DISCOVERY_ONLY:
            errors.append(f"{record.trim_name}: discovery-only source cannot publish facts")

        normalized_identity = (
            normalize_text(record.brand_name),
            normalize_text(record.model_name),
            record.model_year,
            normalize_text(record.trim_name),
        )
        if normalized_identity in identities:
            duplicates += 1
        identities.add(normalized_identity)

        for fact in record.core.facts().values():
            total += 1
            if fact.status in {FactStatus.UNKNOWN, FactStatus.NOT_AVAILABLE, FactStatus.NOT_APPLICABLE}:
                transparent += int(fact.confidence is Confidence.UNKNOWN and getattr(fact, "value", None) is None)
            else:
                value = getattr(fact, "value", getattr(fact, "amount", None))
                transparent += int(value is not None and fact.confidence is not Confidence.UNKNOWN)

        price = record.core.price
        if not (
            (price.price_type == "Msrp" and price.status is FactStatus.OFFICIAL and price.amount is not None)
            or (price.price_type == "Unannounced" and price.amount is None)
        ):
            price_failures.append(f"{record.brand_name} {record.model_name} {record.trim_name}")

    brand_count = len({record.brand_slug for record in batch.records})
    ratio = transparent / total if total else 0.0
    if not 10 <= brand_count <= 15:
        errors.append(f"Initial batch must contain 10–15 brands; found {brand_count}")
    if ratio < 0.90:
        errors.append(f"Core transparency ratio {ratio:.2%} is below 90%")
    if duplicates:
        errors.append(f"Found {duplicates} normalized duplicate trim identities")
    if price_failures:
        errors.append(f"Found {len(price_failures)} trims without official MSRP or transparent Unannounced status")

    return SeedGateReport(
        passed=not errors,
        seeded_brands=brand_count,
        seeded_trims=len(batch.records),
        core_fact_count=total,
        transparent_core_fact_count=transparent,
        transparency_ratio=ratio,
        duplicate_count=duplicates,
        price_gate_failures=tuple(price_failures),
        errors=tuple(errors),
    )


def normalize_text(value: str) -> str:
    decomposed = unicodedata.normalize("NFD", value.casefold().replace("đ", "d"))
    ascii_text = "".join(character for character in decomposed if unicodedata.category(character) != "Mn")
    return re.sub(r"[^a-z0-9]+", " ", ascii_text).strip()
