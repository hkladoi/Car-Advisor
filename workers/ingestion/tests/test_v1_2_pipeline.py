from __future__ import annotations

import asyncio
import csv
import json
from pathlib import Path

import httpx
import pytest
from pydantic import ValidationError

from ingestion.contracts import CoreFact, PriceFact, load_manual_import
from ingestion.fetcher import KnownUrlFetcher
from ingestion.gate import evaluate_seed_gate, normalize_text
from ingestion.registry import SourceRegistry
from ingestion.publisher import _fact_value
from ingestion.risk import ChangeRisk, classify_change, requires_human_review
from ingestion.metadata import is_energy_category
from ingestion.storage import StoredObject


ROOT = next(
    candidate
    for candidate in (Path.cwd(), *Path(__file__).resolve().parents)
    if (candidate / "data" / "source-registry.v1.json").exists()
)
REGISTRY_PATH = ROOT / "data" / "source-registry.v1.json"
SEED_PATH = ROOT / "data" / "seed" / "v1.2-initial-vehicles.json"


class MemoryStorage:
    def __init__(self) -> None:
        self.objects: dict[str, bytes] = {}

    def ensure_bucket(self) -> None:
        return None

    def put_bytes(self, key: str, content: bytes, content_type: str) -> StoredObject:
        self.objects[key] = content
        return StoredObject(bucket="test", key=key, etag=None, version_id=None)

    def get_bytes(self, key: str) -> bytes:
        return self.objects[key]

    def exists(self, key: str) -> bool:
        return key in self.objects


def test_real_seed_batch_passes_v1_2_gate() -> None:
    registry = SourceRegistry.load(REGISTRY_PATH)
    batch = load_manual_import(SEED_PATH)

    report = evaluate_seed_gate(batch, registry)

    assert report.passed
    assert report.seeded_brands == 11
    assert report.seeded_trims == 12
    assert report.transparency_ratio >= 0.90
    assert report.duplicate_count == 0
    assert not report.price_gate_failures


def test_missing_public_price_remains_explicit_unknown_unannounced() -> None:
    batch = load_manual_import(SEED_PATH)
    tucson = next(record for record in batch.records if record.model_slug == "tucson")

    assert tucson.core.price.price_type == "Unannounced"
    assert tucson.core.price.status.value == "Unknown"
    assert tucson.core.price.confidence.value == "Unknown"
    assert tucson.core.price.amount is None
    assert _fact_value(tucson.core.price) is None


def test_seed_features_use_only_canonical_sourced_booleans() -> None:
    batch = load_manual_import(SEED_PATH)
    ex5 = next(record for record in batch.records if record.model_slug == "ex5")

    assert {feature.code for feature in ex5.features} == {"CAMERA_360", "PANORAMIC_ROOF"}
    assert all(feature.fact.value is True for feature in ex5.features)
    assert all(feature.fact.raw_value for feature in ex5.features)


def test_official_unannounced_price_has_explicit_normalized_state() -> None:
    price = PriceFact.model_validate(
        {
            "status": "Official",
            "confidence": "VerifiedOfficial",
            "price_type": "Unannounced",
            "effective_from": "2026-08-22T10:00:00+07:00",
            "raw_value": "Giá bán sẽ được công bố sau",
        }
    )

    assert _fact_value(price) == "UNANNOUNCED"


def test_unknown_fact_cannot_smuggle_a_concrete_false() -> None:
    with pytest.raises(ValidationError):
        CoreFact.model_validate(
            {"status": "Unknown", "confidence": "Unknown", "value": False}
        )


def test_registry_prevents_discovery_snippets_from_becoming_fact_sources() -> None:
    registry = SourceRegistry.load(REGISTRY_PATH)
    discovery = registry.by_id("brave-discovery")

    assert not discovery.automated_fetch
    assert discovery.authority.value == "DiscoveryOnly"


def test_duplicate_trim_is_rejected_before_publish(tmp_path: Path) -> None:
    payload = json.loads(SEED_PATH.read_text(encoding="utf-8"))
    payload["records"].append(payload["records"][0])
    candidate = tmp_path / "duplicate.json"
    candidate.write_text(json.dumps(payload), encoding="utf-8")

    with pytest.raises(ValidationError, match="Duplicate model-year/trim identity"):
        load_manual_import(candidate)


def test_csv_manual_import_uses_the_same_validation_contract(tmp_path: Path) -> None:
    path = tmp_path / "vinfast.csv"
    row = {
        "observed_at": "2026-08-22T10:00:00+07:00",
        "reviewed_by": "bootstrap-review@vietnam-car-platform.local",
        "review_reason": "Official VinFast product page transcription for CSV contract verification.",
        "brand_name": "VinFast",
        "brand_slug": "vinfast",
        "brand_country_code": "VN",
        "brand_official_url": "https://vinfastauto.com/vn_vi",
        "model_name": "VF 6",
        "model_slug": "vf-6",
        "generation_code": "VF6-1",
        "generation_start_year": "2023",
        "model_year": "2026",
        "trim_name": "VF 6 Eco",
        "trim_slug": "eco",
        "source_id": "vinfast-vf6",
        "source_url": "https://shop.vinfastauto.com/vn_vi/dat-coc-xe-dien-vf6.html",
        "body_type": "Suv",
        "segment": "B",
        "market_status": "Active",
        "powertrain": "Bev",
        "seats": "5",
        "length_mm": "",
        "width_mm": "",
        "height_mm": "",
        "wheelbase_mm": "2730",
        "price_type": "Msrp",
        "price_effective_from": "2026-08-22T10:00:00+07:00",
        "msrp_amount": "646000000",
    }
    with path.open("w", encoding="utf-8", newline="") as handle:
        writer = csv.DictWriter(handle, fieldnames=list(row))
        writer.writeheader()
        writer.writerow(row)

    batch = load_manual_import(path)

    assert len(batch.records) == 1
    assert batch.records[0].core.length_mm.status.value == "Unknown"
    assert batch.records[0].core.price.amount == 646000000


def test_normalization_handles_vietnamese_diacritics_and_spacing() -> None:
    assert normalize_text("  Mẫu xe điện Đô-thị  ") == "mau xe dien do thi"


def test_high_risk_price_change_requires_review() -> None:
    risk = classify_change("price", "650000000", "660000000")

    assert risk is ChangeRisk.HIGH
    assert requires_human_review(risk)


def test_energy_source_content_change_requires_review() -> None:
    assert is_energy_category("fuel-price")
    assert is_energy_category("charging-promotion")
    risk = classify_change("energy_source_content_hash", "old", "new")

    assert risk is ChangeRisk.HIGH
    assert requires_human_review(risk)


def test_known_url_fetcher_hashes_and_deduplicates_immutable_snapshot() -> None:
    registry = SourceRegistry.load(REGISTRY_PATH)
    source = registry.by_id("toyota-yaris-cross")
    storage = MemoryStorage()

    def handler(request: httpx.Request) -> httpx.Response:
        return httpx.Response(
            200,
            content=b"official-source",
            headers={"content-type": "text/html; charset=utf-8", "etag": "v1"},
            request=request,
        )

    fetcher = KnownUrlFetcher("test-agent", transport=httpx.MockTransport(handler))
    first = asyncio.run(fetcher.fetch(source, storage))
    second = asyncio.run(fetcher.fetch(source, storage))

    assert first.content_hash == second.content_hash
    assert first.object_key == second.object_key
    assert storage.get_bytes(first.object_key) == b"official-source"
    assert len(storage.objects) == 1


def test_fetcher_rejects_redirect_to_non_allowlisted_domain() -> None:
    registry = SourceRegistry.load(REGISTRY_PATH)
    source = registry.by_id("toyota-yaris-cross")

    def handler(request: httpx.Request) -> httpx.Response:
        if request.url.host == "www.toyota.com.vn":
            return httpx.Response(302, headers={"location": "https://example.com/capture"}, request=request)
        return httpx.Response(200, content=b"not official", request=request)

    fetcher = KnownUrlFetcher("test-agent", transport=httpx.MockTransport(handler))

    with pytest.raises(ValueError, match="escaped source allowlist"):
        asyncio.run(fetcher.fetch(source, MemoryStorage()))
