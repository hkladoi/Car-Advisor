from __future__ import annotations

import hashlib
import re
from datetime import UTC, datetime
from io import BytesIO
from pathlib import Path

import pytest
from pypdf import PdfWriter

from ingestion.fetcher import Snapshot
from ingestion.parsers import (
    DomainParserRegistry,
    ParseOutcome,
    ParserCoordinator,
    ParserError,
    ParserProfileRegistry,
)
from ingestion.registry import SourceRegistry
from ingestion.storage import StoredObject


ROOT = Path(__file__).parents[3]
SOURCE_REGISTRY_PATH = ROOT / "data/source-registry.v1.json"
PARSER_REGISTRY_PATH = ROOT / "data/parser-registry.v2.json"
FIXTURES = ROOT / "data/fixtures/parsers"


class MemoryStorage:
    def __init__(self) -> None:
        self.objects: dict[str, bytes] = {}

    def ensure_bucket(self) -> None:
        return None

    def put_bytes(self, key: str, content: bytes, content_type: str) -> StoredObject:
        self.objects[key] = content
        return StoredObject("memory", key, None, None)

    def get_bytes(self, key: str) -> bytes:
        return self.objects[key]

    def exists(self, key: str) -> bool:
        return key in self.objects


def snapshot(source_id: str, url: str, content: bytes, extension: str) -> Snapshot:
    digest = hashlib.sha256(content).hexdigest()
    return Snapshot(
        source_id=source_id,
        source_url=url,
        final_url=url,
        fetched_at=datetime(2026, 8, 23, tzinfo=UTC),
        content_hash=digest,
        object_key=f"sources/{source_id}/sha256/{digest}.{extension}",
        http_status=200,
        content_type="application/octet-stream",
        etag=None,
        last_modified=None,
        size_bytes=len(content),
        fetch_method="http",
    )


def parser_registry() -> DomainParserRegistry:
    return DomainParserRegistry(ParserProfileRegistry.load(PARSER_REGISTRY_PATH))


def test_every_automated_source_resolves_to_a_versioned_parser() -> None:
    sources = SourceRegistry.load(SOURCE_REGISTRY_PATH)
    parsers = parser_registry()

    for source in sources.sources:
        if source.automated_fetch and source.category != "discovery":
            parser = parsers.resolve(source, source.url)
            assert re.fullmatch(r"[a-z0-9]+(?:-[a-z0-9]+)*/\d+\.\d+\.\d+", parser.parser_version)

    market_parser = parsers.resolve(sources.by_id("isuzu-vietnam-market"), sources.by_id("isuzu-vietnam-market").url)
    assert market_parser.parser_version == "isuzu-market-html/2.8.0"


def test_toyota_profile_reads_json_ld_and_profile_content_before_body() -> None:
    sources = SourceRegistry.load(SOURCE_REGISTRY_PATH)
    source = sources.by_id("toyota-yaris-cross")
    content = (FIXTURES / "toyota-yaris-cross.html").read_bytes()
    parsed = parser_registry().resolve(source, source.url).parse(
        source, snapshot(source.id, source.url, content, "html"), content
    )

    assert parsed.parser_id == "toyota-html"
    assert parsed.title == "Yaris Cross fixture"
    assert parsed.canonical_url is not None
    assert parsed.structured_data[0]["@type"] == "Product"
    assert "Navigation" not in " ".join(parsed.text_blocks)
    assert "Dữ liệu tổng hợp" in parsed.text_blocks[0]


def test_official_toyota_dealer_profile_extracts_offer_terms() -> None:
    sources = SourceRegistry.load(SOURCE_REGISTRY_PATH)
    source = sources.by_id("toyota-taf-august-2026-offer")
    content = (FIXTURES / "toyota-taf-offer.html").read_bytes()
    parsed = parser_registry().resolve(source, source.url).parse(
        source, snapshot(source.id, source.url, content, "html"), content
    )

    assert parsed.parser_id == "toyota-dealer-html"
    assert parsed.title == "Ưu đãi Toyota tháng 8/2026"
    assert "01/08/2026–31/08/2026" in " ".join(parsed.text_blocks)
    assert "Nội dung điều hướng" not in " ".join(parsed.text_blocks)


def test_coordinator_writes_immutable_artifact_and_skips_same_hash() -> None:
    sources = SourceRegistry.load(SOURCE_REGISTRY_PATH)
    source = sources.by_id("vinfast-vf6")
    content = (FIXTURES / "vinfast-vf6.html").read_bytes()
    item = snapshot(source.id, source.url, content, "html")
    storage = MemoryStorage()
    storage.put_bytes(item.object_key, content, "text/html")
    coordinator = ParserCoordinator(parser_registry())

    first = coordinator.parse(source, item, storage)
    second = coordinator.parse(source, item, storage)

    assert first.status == "parsed"
    assert first.document is not None and first.document.parser_id == "vinfast-html"
    assert second == ParseOutcome(
        status="unchanged",
        parser_version=first.parser_version,
        parsed_object_key=first.parsed_object_key,
    )
    assert storage.get_bytes(first.parsed_object_key).startswith(b"{")


def test_pdf_workflow_extracts_metadata_and_page_count() -> None:
    sources = SourceRegistry.load(SOURCE_REGISTRY_PATH)
    source = sources.by_id("toyota-yaris-cross-energy")
    writer = PdfWriter()
    writer.add_blank_page(width=595, height=842)
    writer.add_metadata({"/Title": "Synthetic parser fixture"})
    output = BytesIO()
    writer.write(output)
    content = output.getvalue()

    parsed = parser_registry().resolve(source, source.url).parse(
        source, snapshot(source.id, source.url, content, "pdf"), content
    )

    assert parsed.parser_id == "pdf-pypdf"
    assert parsed.title == "Synthetic parser fixture"
    assert parsed.page_count == 1


def test_coordinator_rejects_snapshot_hash_mismatch() -> None:
    sources = SourceRegistry.load(SOURCE_REGISTRY_PATH)
    source = sources.by_id("vinfast-vf6")
    expected = b"expected"
    item = snapshot(source.id, source.url, expected, "html")
    storage = MemoryStorage()
    storage.put_bytes(item.object_key, b"tampered", "text/html")

    with pytest.raises(ParserError, match="immutable content hash"):
        ParserCoordinator(parser_registry()).parse(source, item, storage)
