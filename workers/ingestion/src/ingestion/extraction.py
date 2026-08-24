from __future__ import annotations

import asyncio
import hashlib
import json
import re
import unicodedata
import uuid
from dataclasses import dataclass
from decimal import Decimal, InvalidOperation
from enum import StrEnum
from pathlib import PurePosixPath
from typing import Any, Literal, Protocol

import httpx
import psycopg
from pydantic import BaseModel, ConfigDict, Field
from tenacity import AsyncRetrying, retry_if_exception_type, stop_after_attempt, wait_exponential_jitter

from ingestion.contracts import Confidence, FactStatus
from ingestion.parsers import ParsedDocument
from ingestion.registry import Authority, RegistrySource
from ingestion.storage import ObjectStorage


class ExtractionError(RuntimeError):
    pass


class SupportedField(StrEnum):
    MSRP = "price.msrp_vnd"
    LENGTH = "spec.length_mm"
    WIDTH = "spec.width_mm"
    HEIGHT = "spec.height_mm"
    WHEELBASE = "spec.wheelbase_mm"
    SEATS = "spec.seats"
    POWER = "powertrain.power_kw"
    TORQUE = "powertrain.torque_nm"
    BATTERY = "energy.usable_battery_kwh"
    RANGE = "energy.official_range_km"
    FUEL_CONSUMPTION = "energy.fuel_litres_per_100km"
    ELECTRIC_CONSUMPTION = "energy.electric_kwh_per_100km"


class RawExtractedFact(BaseModel):
    model_config = ConfigDict(extra="forbid")

    field_path: SupportedField
    raw_value: str = Field(min_length=1, max_length=500)
    numeric_value: str = Field(min_length=1, max_length=80)
    unit: str = Field(min_length=1, max_length=40)
    method: Literal["json_ld", "deterministic_anchor", "local_llm"]
    extraction_context: str = Field(min_length=1, max_length=1000)


class CandidateFact(BaseModel):
    model_config = ConfigDict(extra="forbid")

    field_path: SupportedField
    raw_value: str
    normalized_value: str
    original_unit: str
    canonical_unit: str
    status: FactStatus
    confidence: Confidence
    confidence_score: float = Field(ge=0, le=1)
    extraction_method: str
    extraction_context: str
    conflict: bool = False


class CatalogVehicle(BaseModel):
    model_config = ConfigDict(extra="forbid")

    brand_id: uuid.UUID
    brand_name: str
    brand_slug: str
    model_id: uuid.UUID
    model_name: str
    model_slug: str
    model_aliases: list[str] = Field(default_factory=list)
    model_year: int
    trim_id: uuid.UUID
    trim_name: str
    trim_slug: str
    trim_aliases: list[str] = Field(default_factory=list)


class EntityAlternative(BaseModel):
    model_config = ConfigDict(extra="forbid")

    entity_type: Literal["Model", "Trim"]
    entity_id: uuid.UUID
    label: str
    score: float = Field(ge=0, le=1)


class EntityResolution(BaseModel):
    model_config = ConfigDict(extra="forbid")

    status: Literal["resolved_trim", "resolved_model", "ambiguous", "unresolved"]
    entity_type: Literal["Model", "Trim"] | None = None
    entity_id: uuid.UUID | None = None
    score: float = Field(ge=0, le=1)
    reason: str
    alternatives: list[EntityAlternative] = Field(default_factory=list)


class ExtractionBatch(BaseModel):
    model_config = ConfigDict(extra="forbid")

    schema_version: str = "v2.3"
    extraction_version: str
    source_id: str
    snapshot_id: uuid.UUID
    content_hash: str
    parser_version: str
    entity_resolution: EntityResolution
    facts: list[CandidateFact]
    warnings: list[str] = Field(default_factory=list)


class LlmFact(BaseModel):
    model_config = ConfigDict(extra="forbid")

    field_path: SupportedField
    raw_value: str = Field(min_length=1, max_length=500)
    numeric_value: str = Field(pattern=r"^[0-9]+(?:[.,][0-9]+)*$")
    unit: str = Field(min_length=1, max_length=40)
    extraction_context: str = Field(min_length=1, max_length=1000)


class LlmExtractionPayload(BaseModel):
    model_config = ConfigDict(extra="forbid")

    facts: list[LlmFact] = Field(max_length=100)


@dataclass(frozen=True, slots=True)
class LocalLlmOptions:
    base_url: str
    model: str
    api_key: str = ""
    timeout_seconds: float = 60.0
    max_input_chars: int = 40_000


class LocalLlmJsonSchemaExtractor:
    """Optional OpenAI-compatible local endpoint; output is always Pydantic validated."""

    def __init__(
        self,
        options: LocalLlmOptions,
        transport: httpx.AsyncBaseTransport | None = None,
    ) -> None:
        if not options.base_url.strip() or not options.model.strip():
            raise ValueError("Local LLM base URL and model are both required")
        self._options = options
        self._transport = transport

    async def extract(self, document: ParsedDocument) -> list[RawExtractedFact]:
        source_text = "\n".join(document.text_blocks)[: self._options.max_input_chars]
        if not source_text:
            return []
        endpoint = f"{self._options.base_url.rstrip('/')}/chat/completions"
        headers = {"Accept": "application/json"}
        if self._options.api_key:
            headers["Authorization"] = f"Bearer {self._options.api_key}"
        request_body = {
            "model": self._options.model,
            "temperature": 0,
            "messages": [
                {
                    "role": "system",
                    "content": (
                        "Extract only explicitly stated vehicle facts. Copy raw_value exactly "
                        "from the source. Do not infer a Vietnam trim or invent missing values."
                    ),
                },
                {"role": "user", "content": source_text},
            ],
            "response_format": {
                "type": "json_schema",
                "json_schema": {
                    "name": "vehicle_fact_extraction_v2_3",
                    "strict": True,
                    "schema": LlmExtractionPayload.model_json_schema(),
                },
            },
        }
        response: httpx.Response | None = None
        async for attempt in AsyncRetrying(
            stop=stop_after_attempt(2),
            wait=wait_exponential_jitter(initial=0.5, max=2),
            retry=retry_if_exception_type((httpx.TransportError, httpx.HTTPStatusError)),
            reraise=True,
        ):
            with attempt:
                async with httpx.AsyncClient(
                    transport=self._transport,
                    timeout=self._options.timeout_seconds,
                    headers=headers,
                ) as client:
                    response = await client.post(endpoint, json=request_body)
                    response.raise_for_status()
        if response is None:
            raise ExtractionError("Local LLM completed without a response")
        payload = response.json()
        try:
            content = payload["choices"][0]["message"]["content"]
            validated = LlmExtractionPayload.model_validate_json(content)
        except (KeyError, IndexError, TypeError, ValueError) as error:
            raise ExtractionError("Local LLM response did not match the required JSON schema") from error
        grounded: list[RawExtractedFact] = []
        normalized_source = _normalize_text(source_text)
        for fact in validated.facts:
            if _normalize_text(fact.raw_value) not in normalized_source:
                continue
            grounded.append(
                RawExtractedFact(
                    **fact.model_dump(),
                    method="local_llm",
                )
            )
        return grounded


class DeterministicExtractor:
    version = "deterministic/2.3.0"

    _PATTERNS: tuple[tuple[SupportedField, re.Pattern[str], str], ...] = (
        (SupportedField.MSRP, re.compile(r"(?:giá\s+(?:bán|niêm\s*yết|xe)|msrp)[^0-9]{0,40}([0-9]+(?:[.,][0-9]{1,3}){0,4})\s*(tỷ|triệu|vnđ|vnd|đồng)", re.I), "VND"),
        (SupportedField.WHEELBASE, re.compile(r"(?:chiều\s+dài\s+cơ\s+sở|trục\s+cơ\s+sở|wheelbase)[^0-9]{0,30}([0-9][0-9.,]*)\s*(mm|cm|m)\b", re.I), "mm"),
        (SupportedField.LENGTH, re.compile(r"(?:chiều\s+dài|length)[^0-9]{0,30}([0-9][0-9.,]*)\s*(mm|cm|m)\b", re.I), "mm"),
        (SupportedField.WIDTH, re.compile(r"(?:chiều\s+rộng|width)[^0-9]{0,30}([0-9][0-9.,]*)\s*(mm|cm|m)\b", re.I), "mm"),
        (SupportedField.HEIGHT, re.compile(r"(?:chiều\s+cao|height)[^0-9]{0,30}([0-9][0-9.,]*)\s*(mm|cm|m)\b", re.I), "mm"),
        (SupportedField.SEATS, re.compile(r"(?:số\s+chỗ|chỗ\s+ngồi|seating)[^0-9]{0,20}([0-9]{1,2})\s*(chỗ|ghế|seats?)", re.I), "seat"),
        (SupportedField.POWER, re.compile(r"(?:công\s+suất(?:\s+tối\s+đa)?|power)[^0-9]{0,30}([0-9][0-9.,]*)\s*(kw|ps|hp|mã\s+lực)\b", re.I), "kW"),
        (SupportedField.TORQUE, re.compile(r"(?:mô\s*men(?:\s+xoắn)?|torque)[^0-9]{0,30}([0-9][0-9.,]*)\s*(nm)\b", re.I), "Nm"),
        (SupportedField.BATTERY, re.compile(r"(?:dung\s+lượng\s+pin|battery(?:\s+capacity)?)[^0-9]{0,30}([0-9][0-9.,]*)\s*(kwh)\b", re.I), "kWh"),
        (SupportedField.RANGE, re.compile(r"(?:quãng\s+đường|tầm\s+hoạt\s+động|range)[^0-9]{0,40}([0-9][0-9.,]*)\s*(km)\b", re.I), "km"),
        (SupportedField.FUEL_CONSUMPTION, re.compile(r"(?:mức\s+tiêu\s+thụ\s+nhiên\s+liệu|fuel\s+consumption)[^0-9]{0,40}([0-9][0-9.,]*)\s*(l\s*/\s*100\s*km)\b", re.I), "L/100km"),
        (SupportedField.ELECTRIC_CONSUMPTION, re.compile(r"(?:mức\s+tiêu\s+thụ\s+điện|electric\s+consumption)[^0-9]{0,40}([0-9][0-9.,]*)\s*(kwh\s*/\s*100\s*km)\b", re.I), "kWh/100km"),
    )

    def extract(self, document: ParsedDocument) -> tuple[list[RawExtractedFact], list[str]]:
        facts: list[RawExtractedFact] = []
        warnings: list[str] = []
        for item in document.structured_data:
            facts.extend(self._extract_json_ld(item))
        source_text = "\n".join(document.text_blocks)
        for field_path, pattern, default_unit in self._PATTERNS:
            for match in pattern.finditer(source_text):
                numeric, unit = match.group(1), match.group(2) or default_unit
                facts.append(
                    RawExtractedFact(
                        field_path=field_path,
                        raw_value=match.group(0)[:500],
                        numeric_value=numeric.strip(),
                        unit=unit.strip(),
                        method="deterministic_anchor",
                        extraction_context=match.group(0)[:1000],
                    )
                )
        return facts, warnings

    def _extract_json_ld(self, value: Any) -> list[RawExtractedFact]:
        facts: list[RawExtractedFact] = []
        if isinstance(value, dict):
            offers = value.get("offers")
            offer_items = offers if isinstance(offers, list) else [offers]
            for offer in offer_items:
                if isinstance(offer, dict) and offer.get("price") is not None:
                    currency = str(offer.get("priceCurrency") or "VND")
                    if currency.upper() == "VND":
                        price = str(offer["price"])
                        facts.append(
                            RawExtractedFact(
                                field_path=SupportedField.MSRP,
                                raw_value=price,
                                numeric_value=price,
                                unit="VND",
                                method="json_ld",
                                extraction_context="JSON-LD offers.price",
                            )
                        )
            for child in value.values():
                if child is not offers:
                    facts.extend(self._extract_json_ld(child))
        elif isinstance(value, list):
            for child in value:
                facts.extend(self._extract_json_ld(child))
        return facts


class UnitNormalizer:
    _CANONICAL_UNITS = {
        SupportedField.MSRP: "VND",
        SupportedField.LENGTH: "mm",
        SupportedField.WIDTH: "mm",
        SupportedField.HEIGHT: "mm",
        SupportedField.WHEELBASE: "mm",
        SupportedField.SEATS: "seat",
        SupportedField.POWER: "kW",
        SupportedField.TORQUE: "Nm",
        SupportedField.BATTERY: "kWh",
        SupportedField.RANGE: "km",
        SupportedField.FUEL_CONSUMPTION: "L/100km",
        SupportedField.ELECTRIC_CONSUMPTION: "kWh/100km",
    }

    _RANGES = {
        SupportedField.MSRP: (Decimal("10000000"), Decimal("100000000000")),
        SupportedField.LENGTH: (Decimal("1000"), Decimal("10000")),
        SupportedField.WIDTH: (Decimal("1000"), Decimal("3500")),
        SupportedField.HEIGHT: (Decimal("1000"), Decimal("4500")),
        SupportedField.WHEELBASE: (Decimal("1000"), Decimal("7000")),
        SupportedField.SEATS: (Decimal("1"), Decimal("60")),
        SupportedField.POWER: (Decimal("1"), Decimal("2000")),
        SupportedField.TORQUE: (Decimal("1"), Decimal("10000")),
        SupportedField.BATTERY: (Decimal("1"), Decimal("500")),
        SupportedField.RANGE: (Decimal("1"), Decimal("2500")),
        SupportedField.FUEL_CONSUMPTION: (Decimal("0.1"), Decimal("100")),
        SupportedField.ELECTRIC_CONSUMPTION: (Decimal("0.1"), Decimal("200")),
    }

    def normalize(self, fact: RawExtractedFact) -> tuple[str, str]:
        unit = _normalize_unit(fact.unit)
        value = _parse_number(fact.numeric_value, fact.field_path, unit)
        if fact.field_path is SupportedField.MSRP:
            if unit == "trieu":
                value *= Decimal("1000000")
            elif unit == "ty":
                value *= Decimal("1000000000")
            elif unit != "vnd":
                raise ValueError(f"Unsupported price unit: {fact.unit}")
        elif fact.field_path in {
            SupportedField.LENGTH,
            SupportedField.WIDTH,
            SupportedField.HEIGHT,
            SupportedField.WHEELBASE,
        }:
            value *= {"mm": Decimal(1), "cm": Decimal(10), "m": Decimal(1000)}.get(unit) or _unsupported(unit)
        elif fact.field_path is SupportedField.POWER:
            value *= {"kw": Decimal(1), "ps": Decimal("0.73549875"), "hp": Decimal("0.745699872")}.get(unit) or _unsupported(unit)
        expected = _normalize_unit(self._CANONICAL_UNITS[fact.field_path])
        if fact.field_path not in {SupportedField.MSRP, SupportedField.LENGTH, SupportedField.WIDTH, SupportedField.HEIGHT, SupportedField.WHEELBASE, SupportedField.POWER} and unit != expected:
            raise ValueError(f"Unit {fact.unit} does not match {fact.field_path}")
        minimum, maximum = self._RANGES[fact.field_path]
        if value < minimum or value > maximum:
            raise ValueError(f"Normalized value outside plausible range: {value}")
        return _decimal_string(value), self._CANONICAL_UNITS[fact.field_path]


class EntityResolver:
    def resolve(
        self,
        document: ParsedDocument,
        source: RegistrySource,
        catalog: list[CatalogVehicle],
    ) -> EntityResolution:
        haystack = _normalize_text(
            " ".join(
                [source.id, source.name, source.owner, document.title or ""]
                + document.text_blocks[:3]
            )
        )
        model_candidates: dict[uuid.UUID, EntityAlternative] = {}
        model_rows: dict[uuid.UUID, list[CatalogVehicle]] = {}
        for row in catalog:
            model_rows.setdefault(row.model_id, []).append(row)
            names = [row.model_name, row.model_slug, *row.model_aliases]
            score = max(_name_score(name, haystack) for name in names)
            brand_score = max(_name_score(row.brand_name, haystack), _name_score(row.brand_slug, haystack))
            score = min(1.0, score + (0.1 if brand_score >= 0.7 else 0.0))
            current = model_candidates.get(row.model_id)
            if current is None or score > current.score:
                model_candidates[row.model_id] = EntityAlternative(
                    entity_type="Model",
                    entity_id=row.model_id,
                    label=f"{row.brand_name} {row.model_name}",
                    score=score,
                )
        ranked_models = sorted(model_candidates.values(), key=lambda item: item.score, reverse=True)
        if not ranked_models or ranked_models[0].score < 0.65:
            return EntityResolution(
                status="unresolved",
                score=ranked_models[0].score if ranked_models else 0,
                reason="No model alias reached the deterministic threshold",
                alternatives=ranked_models[:3],
            )
        if len(ranked_models) > 1 and ranked_models[0].score - ranked_models[1].score < 0.08:
            return EntityResolution(
                status="ambiguous",
                score=ranked_models[0].score,
                reason="Top model candidates are too close; human resolution required",
                alternatives=ranked_models[:3],
            )
        model = ranked_models[0]
        trim_candidates: list[EntityAlternative] = []
        for row in model_rows[model.entity_id]:
            names = [row.trim_name, row.trim_slug, *row.trim_aliases]
            score = max(_name_score(name, haystack) for name in names)
            trim_candidates.append(
                EntityAlternative(
                    entity_type="Trim",
                    entity_id=row.trim_id,
                    label=f"{row.brand_name} {row.model_name} {row.trim_name}",
                    score=score,
                )
            )
        trim_candidates.sort(key=lambda item: item.score, reverse=True)
        if trim_candidates and trim_candidates[0].score >= 0.82:
            if len(trim_candidates) == 1 or trim_candidates[0].score - trim_candidates[1].score >= 0.08:
                trim = trim_candidates[0]
                return EntityResolution(
                    status="resolved_trim",
                    entity_type="Trim",
                    entity_id=trim.entity_id,
                    score=trim.score,
                    reason="Unique trim alias matched source identity and content",
                    alternatives=trim_candidates[:3],
                )
            return EntityResolution(
                status="ambiguous",
                score=trim_candidates[0].score,
                reason="Top trim candidates are too close; human resolution required",
                alternatives=trim_candidates[:3],
            )
        return EntityResolution(
            status="resolved_model",
            entity_type="Model",
            entity_id=model.entity_id,
            score=model.score,
            reason="Model resolved but the Vietnam trim is not explicit",
            alternatives=[model, *trim_candidates[:2]],
        )


class CatalogEntityRepository:
    def __init__(self, dsn: str) -> None:
        self._dsn = dsn

    def load(self) -> list[CatalogVehicle]:
        with psycopg.connect(self._dsn) as connection, connection.cursor() as cursor:
            cursor.execute(
                """
                SELECT b.id, b.name, b.slug, m.id, m.name, m.slug,
                       COALESCE(array_agg(DISTINCT ma.normalized_alias) FILTER (WHERE ma.normalized_alias IS NOT NULL), '{}'),
                       my.year, t.id, t.name, t.slug,
                       COALESCE(array_agg(DISTINCT ta.normalized_alias) FILTER (WHERE ta.normalized_alias IS NOT NULL), '{}')
                FROM brands b
                JOIN models m ON m.brand_id = b.id
                JOIN generations g ON g.model_id = m.id
                JOIN model_years my ON my.generation_id = g.id AND my.market = 'VN'
                JOIN trims t ON t.model_year_id = my.id
                LEFT JOIN model_aliases ma ON ma.model_id = m.id
                LEFT JOIN trim_aliases ta ON ta.trim_id = t.id
                GROUP BY b.id, b.name, b.slug, m.id, m.name, m.slug, my.year, t.id, t.name, t.slug
                """
            )
            return [
                CatalogVehicle(
                    brand_id=row[0], brand_name=row[1], brand_slug=row[2],
                    model_id=row[3], model_name=row[4], model_slug=row[5], model_aliases=row[6],
                    model_year=row[7], trim_id=row[8], trim_name=row[9], trim_slug=row[10], trim_aliases=row[11],
                )
                for row in cursor.fetchall()
            ]


class CandidateFactRepository:
    _NAMESPACE = uuid.UUID("73bb9af2-80e1-4ea3-8ea8-b2ecb69eb14e")

    def __init__(self, dsn: str) -> None:
        self._dsn = dsn

    def persist(self, batch: ExtractionBatch) -> int:
        inserted = 0
        entity = batch.entity_resolution
        entity_type = entity.entity_type or "UnresolvedVehicle"
        with psycopg.connect(self._dsn) as connection, connection.transaction(), connection.cursor() as cursor:
            for fact in batch.facts:
                fact_id = self.fact_id(batch, fact)
                context = json.dumps(
                    {
                        "schema_version": batch.schema_version,
                        "extraction_version": batch.extraction_version,
                        "parser_version": batch.parser_version,
                        "method": fact.extraction_method,
                        "context": fact.extraction_context,
                        "original_unit": fact.original_unit,
                        "canonical_unit": fact.canonical_unit,
                        "confidence_score": fact.confidence_score,
                        "conflict": fact.conflict,
                        "entity_resolution": entity.model_dump(mode="json"),
                    },
                    ensure_ascii=False,
                )
                cursor.execute(
                    """
                    INSERT INTO source_facts
                        (id, snapshot_id, entity_type, entity_id, field_path, raw_value,
                         normalized_value, status, confidence, extraction_context, created_at, updated_at)
                    VALUES (%s, %s, %s, %s, %s, %s, %s, %s, %s, %s, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
                    ON CONFLICT (id) DO NOTHING
                    """,
                    (
                        fact_id, batch.snapshot_id, entity_type, entity.entity_id,
                        fact.field_path.value, fact.raw_value, fact.normalized_value,
                        fact.status.value, fact.confidence.value, context,
                    ),
                )
                inserted += cursor.rowcount
        return inserted

    @classmethod
    def fact_id(cls, batch: ExtractionBatch, fact: CandidateFact) -> uuid.UUID:
        return uuid.uuid5(
            cls._NAMESPACE,
            "|".join(
                [
                    str(batch.snapshot_id),
                    str(batch.entity_resolution.entity_id),
                    fact.field_path.value,
                    fact.normalized_value,
                    fact.extraction_method,
                ]
            ),
        )


class StructuredExtractionEngine:
    version = "structured-extraction/2.3.0"

    def __init__(self, llm: LocalLlmJsonSchemaExtractor | None = None) -> None:
        self._deterministic = DeterministicExtractor()
        self._normalizer = UnitNormalizer()
        self._resolver = EntityResolver()
        self._llm = llm

    async def extract(
        self,
        document: ParsedDocument,
        source: RegistrySource,
        snapshot_id: uuid.UUID,
        catalog: list[CatalogVehicle],
    ) -> ExtractionBatch:
        raw_facts, warnings = self._deterministic.extract(document)
        if not raw_facts and self._llm is not None:
            raw_facts = await self._llm.extract(document)
            if raw_facts:
                warnings.append("local_llm_fallback_used")
        resolution = self._resolver.resolve(document, source, catalog)
        normalized: list[tuple[RawExtractedFact, str, str]] = []
        for raw in raw_facts:
            try:
                value, unit = self._normalizer.normalize(raw)
            except (ValueError, InvalidOperation) as error:
                warnings.append(f"normalization_rejected:{raw.field_path.value}:{type(error).__name__}")
                continue
            normalized.append((raw, value, unit))
        unique: dict[tuple[SupportedField, str], tuple[RawExtractedFact, str, str]] = {}
        for item in normalized:
            unique.setdefault((item[0].field_path, item[1]), item)
        values_by_field: dict[SupportedField, set[str]] = {}
        for raw, value, _ in unique.values():
            values_by_field.setdefault(raw.field_path, set()).add(value)
        facts: list[CandidateFact] = []
        for raw, value, unit in unique.values():
            conflict = len(values_by_field[raw.field_path]) > 1
            score, confidence = _confidence(source.authority, raw.method, resolution, conflict)
            facts.append(
                CandidateFact(
                    field_path=raw.field_path,
                    raw_value=raw.raw_value,
                    normalized_value=value,
                    original_unit=raw.unit,
                    canonical_unit=unit,
                    status=FactStatus.OFFICIAL,
                    confidence=confidence,
                    confidence_score=score,
                    extraction_method=raw.method,
                    extraction_context=raw.extraction_context,
                    conflict=conflict,
                )
            )
        return ExtractionBatch(
            extraction_version=self.version,
            source_id=source.id,
            snapshot_id=snapshot_id,
            content_hash=document.content_hash,
            parser_version=document.parser_version,
            entity_resolution=resolution,
            facts=facts,
            warnings=warnings,
        )


class ExtractionOutcome(BaseModel):
    model_config = ConfigDict(extra="forbid")

    status: Literal["extracted", "unchanged"]
    artifact_key: str
    inserted_facts: int = Field(ge=0)
    batch: ExtractionBatch | None = None


@dataclass(slots=True)
class StructuredExtractionPipeline:
    storage: ObjectStorage
    catalog_repository: CatalogEntityRepository
    fact_repository: CandidateFactRepository
    engine: StructuredExtractionEngine

    async def process(
        self,
        source: RegistrySource,
        snapshot_id: uuid.UUID,
        parsed_object_key: str,
        content_hash: str,
    ) -> ExtractionOutcome:
        version_key = re.sub(r"[^a-zA-Z0-9._-]+", "-", self.engine.version)
        artifact_key = str(
            PurePosixPath("extracted", source.id, "sha256", content_hash, f"{version_key}.json")
        )
        if await asyncio.to_thread(self.storage.exists, artifact_key):
            artifact = await asyncio.to_thread(self.storage.get_bytes, artifact_key)
            cached_batch = ExtractionBatch.model_validate_json(artifact)
            # Snapshot bytes are immutable, but catalog identity can legitimately
            # become more specific after a reviewed brand/model/trim publication.
            # Reconsider only non-trim resolutions and promote them only when the
            # deterministic resolver reaches a strictly stronger identity. This
            # keeps stable resolved facts idempotent without freezing an early
            # unresolved artifact forever during clean bootstrap.
            if cached_batch.entity_resolution.status != "resolved_trim":
                parsed_bytes = await asyncio.to_thread(self.storage.get_bytes, parsed_object_key)
                document = ParsedDocument.model_validate_json(parsed_bytes)
                catalog = await asyncio.to_thread(self.catalog_repository.load)
                refreshed_batch = await self.engine.extract(document, source, snapshot_id, catalog)
                resolution_rank = {
                    "unresolved": 0,
                    "ambiguous": 0,
                    "resolved_model": 1,
                    "resolved_trim": 2,
                }
                if (
                    resolution_rank[refreshed_batch.entity_resolution.status]
                    > resolution_rank[cached_batch.entity_resolution.status]
                ):
                    inserted = await asyncio.to_thread(
                        self.fact_repository.persist, refreshed_batch
                    )
                    await asyncio.to_thread(
                        self.storage.put_bytes,
                        artifact_key,
                        refreshed_batch.model_dump_json(indent=2).encode("utf-8"),
                        "application/json",
                    )
                    return ExtractionOutcome(
                        status="extracted",
                        artifact_key=artifact_key,
                        inserted_facts=inserted,
                        batch=refreshed_batch,
                    )
            return ExtractionOutcome(
                status="unchanged",
                artifact_key=artifact_key,
                inserted_facts=0,
                batch=cached_batch,
            )
        parsed_bytes = await asyncio.to_thread(self.storage.get_bytes, parsed_object_key)
        document = ParsedDocument.model_validate_json(parsed_bytes)
        catalog = await asyncio.to_thread(self.catalog_repository.load)
        batch = await self.engine.extract(document, source, snapshot_id, catalog)
        inserted = await asyncio.to_thread(self.fact_repository.persist, batch)
        await asyncio.to_thread(
            self.storage.put_bytes,
            artifact_key,
            batch.model_dump_json(indent=2).encode("utf-8"),
            "application/json",
        )
        return ExtractionOutcome(
            status="extracted",
            artifact_key=artifact_key,
            inserted_facts=inserted,
            batch=batch,
        )


def _confidence(
    authority: Authority,
    method: str,
    resolution: EntityResolution,
    conflict: bool,
) -> tuple[float, Confidence]:
    base = {
        Authority.COMPETENT_AUTHORITY: 0.98,
        Authority.BRAND_OFFICIAL: 0.97,
        Authority.DISTRIBUTOR_OFFICIAL: 0.94,
        Authority.DEALER_OFFICIAL: 0.84,
        Authority.TRUSTED_SECONDARY: 0.74,
        Authority.DISCOVERY_ONLY: 0.2,
    }[authority]
    base += {"json_ld": 0.02, "deterministic_anchor": 0.0, "local_llm": -0.25}[method]
    base *= {"resolved_trim": 1.0, "resolved_model": 0.82, "ambiguous": 0.5, "unresolved": 0.4}[resolution.status]
    if conflict:
        base = min(base, 0.6)
    score = round(max(0.0, min(1.0, base)), 4)
    if score >= 0.95 and authority in {Authority.COMPETENT_AUTHORITY, Authority.BRAND_OFFICIAL, Authority.DISTRIBUTOR_OFFICIAL}:
        confidence = Confidence.VERIFIED_OFFICIAL
    elif score >= 0.8:
        confidence = Confidence.TRUSTED_SINGLE_SOURCE
    elif score >= 0.5:
        confidence = Confidence.ESTIMATED
    else:
        confidence = Confidence.UNKNOWN
    return score, confidence


def _normalize_text(value: str) -> str:
    decomposed = unicodedata.normalize("NFD", value.lower().replace("đ", "d"))
    ascii_text = "".join(character for character in decomposed if unicodedata.category(character) != "Mn")
    return re.sub(r"[^a-z0-9]+", " ", ascii_text).strip()


def _name_score(name: str, haystack: str) -> float:
    needle = _normalize_text(name)
    if not needle:
        return 0
    if re.search(rf"(?:^|\s){re.escape(needle)}(?:$|\s)", haystack):
        return 0.9
    compact_needle = needle.replace(" ", "")
    compact_haystack = haystack.replace(" ", "")
    if len(compact_needle) >= 3 and compact_needle in compact_haystack:
        return 0.84
    tokens = needle.split()
    if len(tokens) > 1 and all(token in haystack.split() for token in tokens):
        return 0.7
    return 0


def _normalize_unit(unit: str) -> str:
    normalized = _normalize_text(unit).replace(" ", "")
    return {
        "trieu": "trieu", "ty": "ty", "vnd": "vnd", "dong": "vnd",
        "mm": "mm", "cm": "cm", "m": "m", "seat": "seat", "cho": "seat", "ghe": "seat", "seats": "seat",
        "kw": "kw", "ps": "ps", "hp": "hp", "maluc": "hp", "nm": "nm",
        "kwh": "kwh", "km": "km", "l/100km": "l/100km", "l100km": "l/100km",
        "kwh/100km": "kwh/100km", "kwh100km": "kwh/100km",
    }.get(normalized, normalized)


def _parse_number(raw: str, field: SupportedField, unit: str) -> Decimal:
    value = re.sub(r"\s+", "", raw)
    separators = [index for index, char in enumerate(value) if char in ".,"]
    if not separators:
        return Decimal(value)
    price_multiplier = field is SupportedField.MSRP and unit in {"trieu", "ty"}
    if price_multiplier and len(separators) == 1:
        return Decimal(value.replace(",", "."))
    groups = re.split(r"[.,]", value)
    if all(len(group) == 3 for group in groups[1:]):
        return Decimal("".join(groups))
    last = separators[-1]
    normalized = value[:last].replace(".", "").replace(",", "") + "." + value[last + 1 :]
    return Decimal(normalized)


def _decimal_string(value: Decimal) -> str:
    normalized = format(value.normalize(), "f")
    return normalized.rstrip("0").rstrip(".") if "." in normalized else normalized


def _unsupported(unit: str) -> Decimal:
    raise ValueError(f"Unsupported unit: {unit}")
