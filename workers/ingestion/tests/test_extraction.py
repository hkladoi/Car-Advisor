from __future__ import annotations

import asyncio
import hashlib
import uuid
from datetime import UTC, datetime

import httpx

from ingestion.extraction import (
    CatalogVehicle,
    DeterministicExtractor,
    EntityResolver,
    LocalLlmJsonSchemaExtractor,
    LocalLlmOptions,
    RawExtractedFact,
    StructuredExtractionEngine,
    StructuredExtractionPipeline,
    SupportedField,
    UnitNormalizer,
)
from ingestion.parsers import ParsedDocument
from ingestion.registry import SourceRegistry
from ingestion.storage import StoredObject
from pathlib import Path


ROOT = Path(__file__).parents[3]
REGISTRY = SourceRegistry.load(ROOT / "data/source-registry.v1.json")


def document(text: str, *, structured_data: list[dict] | None = None) -> ParsedDocument:
    return ParsedDocument(
        source_id="toyota-yaris-cross",
        source_url="https://www.toyota.com.vn/yaris-cross",
        final_url="https://www.toyota.com.vn/yaris-cross",
        content_hash=hashlib.sha256(text.encode()).hexdigest(),
        content_type="Html",
        parser_id="toyota-html",
        parser_version="toyota-html/2.2.0",
        title="Toyota Yaris Cross",
        structured_data=structured_data or [],
        text_blocks=[text],
    )


def catalog() -> list[CatalogVehicle]:
    return [
        CatalogVehicle(
            brand_id=uuid.uuid4(),
            brand_name="Toyota",
            brand_slug="toyota",
            model_id=uuid.UUID("11111111-1111-1111-1111-111111111111"),
            model_name="Yaris Cross",
            model_slug="yaris-cross",
            model_aliases=["yaris cross"],
            model_year=2026,
            trim_id=uuid.UUID("22222222-2222-2222-2222-222222222222"),
            trim_name="Yaris Cross",
            trim_slug="yaris-cross",
            trim_aliases=[],
        ),
        CatalogVehicle(
            brand_id=uuid.uuid4(),
            brand_name="Honda",
            brand_slug="honda",
            model_id=uuid.UUID("33333333-3333-3333-3333-333333333333"),
            model_name="CR-V",
            model_slug="cr-v",
            model_aliases=["crv"],
            model_year=2026,
            trim_id=uuid.UUID("44444444-4444-4444-4444-444444444444"),
            trim_name="CR-V G",
            trim_slug="cr-v-g",
            trim_aliases=[],
        ),
    ]


def test_unit_normalization_handles_vietnamese_price_dimensions_and_power() -> None:
    normalizer = UnitNormalizer()
    price = RawExtractedFact(
        field_path=SupportedField.MSRP,
        raw_value="Giá bán 1,2 tỷ",
        numeric_value="1,2",
        unit="tỷ",
        method="deterministic_anchor",
        extraction_context="Giá bán 1,2 tỷ",
    )
    length = price.model_copy(
        update={"field_path": SupportedField.LENGTH, "numeric_value": "4.310", "unit": "mm"}
    )
    power = price.model_copy(
        update={"field_path": SupportedField.POWER, "numeric_value": "200", "unit": "PS"}
    )

    assert normalizer.normalize(price) == ("1200000000", "VND")
    assert normalizer.normalize(length) == ("4310", "mm")
    assert normalizer.normalize(power) == ("147.09975", "kW")


def test_deterministic_extraction_prefers_structured_data_and_normalizes_candidates() -> None:
    source = REGISTRY.by_id("toyota-yaris-cross")
    parsed = document(
        "Toyota Yaris Cross. Chiều dài 4.310 mm. Số chỗ ngồi 5 chỗ.",
        structured_data=[
            {
                "@type": "Product",
                "offers": {"price": "650000000", "priceCurrency": "VND"},
            }
        ],
    )
    raw, warnings = DeterministicExtractor().extract(parsed)

    assert warnings == []
    assert raw[0].field_path is SupportedField.MSRP
    batch = asyncio.run(
        StructuredExtractionEngine().extract(parsed, source, uuid.uuid4(), catalog())
    )
    values = {fact.field_path: fact.normalized_value for fact in batch.facts}
    assert values[SupportedField.MSRP] == "650000000"
    assert values[SupportedField.LENGTH] == "4310"
    assert values[SupportedField.SEATS] == "5"
    assert batch.entity_resolution.status == "resolved_trim"
    assert all(fact.confidence_score >= 0.95 for fact in batch.facts)


def test_entity_resolution_refuses_close_model_matches() -> None:
    duplicated = catalog()
    duplicated.append(
        catalog()[0].model_copy(
            update={
                "model_id": uuid.UUID("55555555-5555-5555-5555-555555555555"),
                "trim_id": uuid.UUID("66666666-6666-6666-6666-666666666666"),
            }
        )
    )
    resolution = EntityResolver().resolve(
        document("Toyota Yaris Cross"), REGISTRY.by_id("toyota-yaris-cross"), duplicated
    )

    assert resolution.status == "ambiguous"
    assert resolution.entity_id is None


def test_local_llm_is_schema_bound_and_drops_ungrounded_values() -> None:
    def handler(request: httpx.Request) -> httpx.Response:
        body = __import__("json").loads(request.content)
        assert body["response_format"]["type"] == "json_schema"
        assert body["response_format"]["json_schema"]["strict"] is True
        return httpx.Response(
            200,
            json={
                "choices": [
                    {
                        "message": {
                            "content": __import__("json").dumps(
                                {
                                    "facts": [
                                        {
                                            "field_path": "energy.official_range_km",
                                            "raw_value": "Tầm hoạt động 500 km",
                                            "numeric_value": "500",
                                            "unit": "km",
                                            "extraction_context": "explicit range",
                                        },
                                        {
                                            "field_path": "spec.seats",
                                            "raw_value": "7 seats not in source",
                                            "numeric_value": "7",
                                            "unit": "seat",
                                            "extraction_context": "hallucinated",
                                        },
                                    ]
                                }
                            )
                        }
                    }
                ]
            },
        )

    extractor = LocalLlmJsonSchemaExtractor(
        LocalLlmOptions(base_url="http://localhost:11434/v1", model="local-test"),
        transport=httpx.MockTransport(handler),
    )
    facts = asyncio.run(extractor.extract(document("Tầm hoạt động 500 km")))

    assert len(facts) == 1
    assert facts[0].field_path is SupportedField.RANGE
    assert facts[0].method == "local_llm"


def test_extraction_pipeline_is_immutable_and_idempotent() -> None:
    parsed = document("Toyota Yaris Cross. Số chỗ ngồi 5 chỗ.")
    storage = _MemoryStorage()
    parsed_key = "parsed/toyota.json"
    storage.put_bytes(parsed_key, parsed.model_dump_json().encode(), "application/json")
    facts = _FactRepository()
    pipeline = StructuredExtractionPipeline(
        storage=storage,
        catalog_repository=_CatalogRepository(),  # type: ignore[arg-type]
        fact_repository=facts,  # type: ignore[arg-type]
        engine=StructuredExtractionEngine(),
    )
    snapshot_id = uuid.uuid4()

    first = asyncio.run(
        pipeline.process(
            REGISTRY.by_id("toyota-yaris-cross"),
            snapshot_id,
            parsed_key,
            parsed.content_hash,
        )
    )
    second = asyncio.run(
        pipeline.process(
            REGISTRY.by_id("toyota-yaris-cross"),
            snapshot_id,
            parsed_key,
            parsed.content_hash,
        )
    )

    assert first.status == "extracted" and first.inserted_facts == 1
    assert second.status == "unchanged" and facts.calls == 1


def test_cached_unresolved_extraction_promotes_after_catalog_publication() -> None:
    parsed = document("Toyota Yaris Cross. Số chỗ ngồi 5 chỗ.")
    storage = _MemoryStorage()
    parsed_key = "parsed/toyota-late-catalog.json"
    storage.put_bytes(parsed_key, parsed.model_dump_json().encode(), "application/json")
    facts = _FactRepository()
    catalog_repository = _MutableCatalogRepository()
    pipeline = StructuredExtractionPipeline(
        storage=storage,
        catalog_repository=catalog_repository,  # type: ignore[arg-type]
        fact_repository=facts,  # type: ignore[arg-type]
        engine=StructuredExtractionEngine(),
    )
    snapshot_id = uuid.uuid4()

    unresolved = asyncio.run(
        pipeline.process(
            REGISTRY.by_id("toyota-yaris-cross"),
            snapshot_id,
            parsed_key,
            parsed.content_hash,
        )
    )
    catalog_repository.rows = catalog()
    promoted = asyncio.run(
        pipeline.process(
            REGISTRY.by_id("toyota-yaris-cross"),
            snapshot_id,
            parsed_key,
            parsed.content_hash,
        )
    )
    replay = asyncio.run(
        pipeline.process(
            REGISTRY.by_id("toyota-yaris-cross"),
            snapshot_id,
            parsed_key,
            parsed.content_hash,
        )
    )

    assert unresolved.batch is not None
    assert unresolved.batch.entity_resolution.status == "unresolved"
    assert promoted.status == "extracted" and promoted.inserted_facts == 1
    assert promoted.batch is not None
    assert promoted.batch.entity_resolution.status == "resolved_trim"
    assert replay.status == "unchanged" and facts.calls == 2


class _MemoryStorage:
    def __init__(self) -> None:
        self.objects: dict[str, bytes] = {}

    def exists(self, key: str) -> bool:
        return key in self.objects

    def get_bytes(self, key: str) -> bytes:
        return self.objects[key]

    def put_bytes(self, key: str, content: bytes, content_type: str) -> StoredObject:
        self.objects[key] = content
        return StoredObject("memory", key, None, None)

    def ensure_bucket(self) -> None:
        return None


class _CatalogRepository:
    def load(self) -> list[CatalogVehicle]:
        return catalog()


class _MutableCatalogRepository:
    def __init__(self) -> None:
        self.rows: list[CatalogVehicle] = []

    def load(self) -> list[CatalogVehicle]:
        return self.rows


class _FactRepository:
    def __init__(self) -> None:
        self.calls = 0

    def persist(self, batch: object) -> int:
        self.calls += 1
        return 1
