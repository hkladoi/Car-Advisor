from __future__ import annotations

import hashlib
import json
import re
import xml.etree.ElementTree as element_tree
from dataclasses import dataclass
from io import BytesIO
from pathlib import Path, PurePosixPath
from typing import Any, Literal, Protocol
from urllib.parse import urljoin, urlsplit

from pydantic import BaseModel, ConfigDict, Field, model_validator
from pypdf import PdfReader
from selectolax.parser import HTMLParser

from ingestion.fetcher import Snapshot
from ingestion.registry import ContentType, RegistrySource
from ingestion.storage import ObjectStorage


class ParserError(RuntimeError):
    pass


class UnsupportedParser(ParserError):
    pass


class ParserProfile(BaseModel):
    model_config = ConfigDict(extra="forbid")

    domain: str = Field(min_length=3, max_length=253)
    parser_id: str = Field(pattern=r"^[a-z0-9]+(?:-[a-z0-9]+)*$")
    version: str = Field(pattern=r"^\d+\.\d+\.\d+$")
    content_selectors: list[str] = Field(min_length=1)
    title_selectors: list[str] = Field(min_length=1)

    @model_validator(mode="after")
    def normalize_domain(self) -> "ParserProfile":
        self.domain = self.domain.lower().strip(".")
        if "/" in self.domain or ":" in self.domain:
            raise ValueError("Parser profile domain must be a hostname")
        return self


class ParserProfileRegistry(BaseModel):
    model_config = ConfigDict(extra="forbid")

    schema_version: str
    profiles: list[ParserProfile] = Field(min_length=1)

    @model_validator(mode="after")
    def unique_domains(self) -> "ParserProfileRegistry":
        domains = [profile.domain for profile in self.profiles]
        if len(domains) != len(set(domains)):
            raise ValueError("Parser profile domains must be unique")
        return self

    @classmethod
    def load(cls, path: Path) -> "ParserProfileRegistry":
        return cls.model_validate_json(path.read_text(encoding="utf-8"))

    def by_domain(self, domain: str) -> ParserProfile:
        normalized = domain.lower().strip(".")
        for profile in self.profiles:
            if normalized == profile.domain:
                return profile
        raise UnsupportedParser(f"No HTML parser profile registered for {domain}")


class ParsedDocument(BaseModel):
    model_config = ConfigDict(extra="forbid")

    source_id: str
    source_url: str
    final_url: str
    content_hash: str
    content_type: str
    parser_id: str
    parser_version: str
    title: str | None = None
    canonical_url: str | None = None
    metadata: dict[str, str] = Field(default_factory=dict)
    structured_data: list[dict[str, Any]] = Field(default_factory=list)
    text_blocks: list[str] = Field(default_factory=list)
    page_count: int | None = Field(default=None, ge=0)
    warnings: list[str] = Field(default_factory=list)


class SourceParser(Protocol):
    @property
    def parser_id(self) -> str: ...

    @property
    def parser_version(self) -> str: ...

    def parse(self, source: RegistrySource, snapshot: Snapshot, content: bytes) -> ParsedDocument: ...


class ConfiguredHtmlParser:
    def __init__(self, profile: ParserProfile) -> None:
        self._profile = profile

    @property
    def parser_id(self) -> str:
        return self._profile.parser_id

    @property
    def parser_version(self) -> str:
        return f"{self.parser_id}/{self._profile.version}"

    def parse(self, source: RegistrySource, snapshot: Snapshot, content: bytes) -> ParsedDocument:
        tree = HTMLParser(content)
        warnings: list[str] = []
        title = _first_text(tree, self._profile.title_selectors)
        canonical = _canonical_url(tree, snapshot.final_url, source)
        structured_data = _json_ld(tree, warnings)
        _remove_noncontent(tree)
        text_blocks = _selected_text(tree, self._profile.content_selectors)
        if not text_blocks:
            warnings.append("no_profile_content_match")
        metadata = _html_metadata(tree)
        return ParsedDocument(
            source_id=source.id,
            source_url=source.url,
            final_url=snapshot.final_url,
            content_hash=snapshot.content_hash,
            content_type="Html",
            parser_id=self.parser_id,
            parser_version=self.parser_version,
            title=title,
            canonical_url=canonical,
            metadata=metadata,
            structured_data=structured_data,
            text_blocks=text_blocks,
            warnings=warnings,
        )


class PdfDocumentParser:
    parser_id = "pdf-pypdf"

    def __init__(self, max_pages: int) -> None:
        self._max_pages = max_pages

    @property
    def parser_version(self) -> str:
        return f"{self.parser_id}/2.2.0"

    def parse(self, source: RegistrySource, snapshot: Snapshot, content: bytes) -> ParsedDocument:
        reader = PdfReader(BytesIO(content), strict=False)
        page_count = len(reader.pages)
        if page_count > self._max_pages:
            raise ParserError(f"PDF has {page_count} pages; limit is {self._max_pages}")
        warnings: list[str] = []
        if reader.is_encrypted:
            try:
                reader.decrypt("")
            except Exception as error:
                raise ParserError("Encrypted PDF cannot be parsed") from error
        text_blocks: list[str] = []
        for page_number, page in enumerate(reader.pages, start=1):
            try:
                text = _normalize_text(page.extract_text() or "")
            except Exception:
                warnings.append(f"page_{page_number}_text_extraction_failed")
                continue
            if text:
                text_blocks.append(text)
        metadata = {
            str(key).lstrip("/"): str(value)
            for key, value in (reader.metadata or {}).items()
            if value is not None
        }
        return ParsedDocument(
            source_id=source.id,
            source_url=source.url,
            final_url=snapshot.final_url,
            content_hash=snapshot.content_hash,
            content_type="Pdf",
            parser_id=self.parser_id,
            parser_version=self.parser_version,
            title=metadata.get("Title"),
            metadata=metadata,
            text_blocks=text_blocks,
            page_count=page_count,
            warnings=warnings,
        )


class JsonDocumentParser:
    parser_id = "json-structured"
    parser_version = "json-structured/2.2.0"

    def parse(self, source: RegistrySource, snapshot: Snapshot, content: bytes) -> ParsedDocument:
        payload = json.loads(content)
        structured = payload if isinstance(payload, list) else [payload]
        if not all(isinstance(item, dict) for item in structured):
            raise ParserError("JSON source root must be an object or list of objects")
        return ParsedDocument(
            source_id=source.id,
            source_url=source.url,
            final_url=snapshot.final_url,
            content_hash=snapshot.content_hash,
            content_type="Json",
            parser_id=self.parser_id,
            parser_version=self.parser_version,
            structured_data=structured,
        )


class XmlDocumentParser:
    parser_id = "xml-structured"
    parser_version = "xml-structured/2.2.0"

    def parse(self, source: RegistrySource, snapshot: Snapshot, content: bytes) -> ParsedDocument:
        if b"<!DOCTYPE" in content.upper() or b"<!ENTITY" in content.upper():
            raise ParserError("XML DTD and entity declarations are not allowed")
        root = element_tree.fromstring(content)
        text = _normalize_text(" ".join(root.itertext()))
        return ParsedDocument(
            source_id=source.id,
            source_url=source.url,
            final_url=snapshot.final_url,
            content_hash=snapshot.content_hash,
            content_type="Xml",
            parser_id=self.parser_id,
            parser_version=self.parser_version,
            text_blocks=[text] if text else [],
            metadata={"root_tag": root.tag},
        )


class DomainParserRegistry:
    def __init__(self, profiles: ParserProfileRegistry, max_pdf_pages: int = 500) -> None:
        self._profiles = profiles
        self._pdf = PdfDocumentParser(max_pdf_pages)
        self._json = JsonDocumentParser()
        self._xml = XmlDocumentParser()

    def resolve(self, source: RegistrySource, final_url: str) -> SourceParser:
        if source.content_type is ContentType.PDF:
            return self._pdf
        if source.content_type is ContentType.JSON:
            return self._json
        if source.content_type is ContentType.XML:
            return self._xml
        if source.content_type is not ContentType.HTML:
            raise UnsupportedParser(f"Unsupported content type: {source.content_type}")
        hostname = urlsplit(final_url).hostname
        if hostname is None or not source.allows_url(final_url):
            raise UnsupportedParser("Final parser URL is outside the source allowlist")
        return ConfiguredHtmlParser(self._profiles.by_domain(hostname))


class ParseOutcome(BaseModel):
    model_config = ConfigDict(extra="forbid")

    status: Literal["parsed", "unchanged"]
    parser_version: str
    parsed_object_key: str
    document: ParsedDocument | None = None


@dataclass(frozen=True, slots=True)
class ParserCoordinator:
    registry: DomainParserRegistry
    max_content_bytes: int = 20_000_000

    def parse(
        self,
        source: RegistrySource,
        snapshot: Snapshot,
        storage: ObjectStorage,
    ) -> ParseOutcome:
        parser = self.registry.resolve(source, snapshot.final_url)
        version_key = re.sub(r"[^a-zA-Z0-9._-]+", "-", parser.parser_version)
        parsed_object_key = str(
            PurePosixPath(
                "parsed", source.id, "sha256", snapshot.content_hash, f"{version_key}.json"
            )
        )
        if storage.exists(parsed_object_key):
            return ParseOutcome(
                status="unchanged",
                parser_version=parser.parser_version,
                parsed_object_key=parsed_object_key,
            )

        content = storage.get_bytes(snapshot.object_key)
        if len(content) > self.max_content_bytes:
            raise ParserError(
                f"Snapshot is {len(content)} bytes; parser limit is {self.max_content_bytes}"
            )
        digest = hashlib.sha256(content).hexdigest()
        if digest != snapshot.content_hash:
            raise ParserError("Snapshot bytes do not match immutable content hash")
        document = parser.parse(source, snapshot, content)
        storage.put_bytes(
            parsed_object_key,
            document.model_dump_json(indent=2).encode("utf-8"),
            "application/json",
        )
        return ParseOutcome(
            status="parsed",
            parser_version=parser.parser_version,
            parsed_object_key=parsed_object_key,
            document=document,
        )


def _first_text(tree: HTMLParser, selectors: list[str]) -> str | None:
    for selector in selectors:
        node = tree.css_first(selector)
        if node is not None:
            text = _normalize_text(node.text(separator=" ", strip=True))
            if text:
                return text
    return None


def _selected_text(tree: HTMLParser, selectors: list[str]) -> list[str]:
    blocks: list[str] = []
    seen: set[str] = set()
    for selector in selectors:
        for node in tree.css(selector):
            text = _normalize_text(node.text(separator=" ", strip=True))
            if text and text not in seen:
                seen.add(text)
                blocks.append(text)
        if blocks:
            break
    return blocks


def _json_ld(tree: HTMLParser, warnings: list[str]) -> list[dict[str, Any]]:
    structured: list[dict[str, Any]] = []
    for script in tree.css('script[type="application/ld+json"]'):
        raw = script.text(strip=True)
        if not raw:
            continue
        try:
            payload = json.loads(raw)
        except json.JSONDecodeError:
            warnings.append("invalid_json_ld")
            continue
        items = payload if isinstance(payload, list) else [payload]
        structured.extend(item for item in items if isinstance(item, dict))
    return structured[:100]


def _remove_noncontent(tree: HTMLParser) -> None:
    for node in tree.css("script, style, noscript, nav, footer"):
        node.decompose()


def _canonical_url(tree: HTMLParser, base_url: str, source: RegistrySource) -> str | None:
    node = tree.css_first('link[rel="canonical"]')
    if node is None:
        return None
    href = node.attributes.get("href")
    if not href:
        return None
    candidate = urljoin(base_url, href)
    parsed = urlsplit(candidate)
    if parsed.scheme != "https" or not source.allows_url(candidate):
        return None
    return candidate


def _html_metadata(tree: HTMLParser) -> dict[str, str]:
    metadata: dict[str, str] = {}
    for node in tree.css("meta[property], meta[name]"):
        key = node.attributes.get("property") or node.attributes.get("name")
        value = node.attributes.get("content")
        if key and value and len(metadata) < 100:
            metadata[key] = value
    return metadata


def _normalize_text(value: str) -> str:
    return re.sub(r"\s+", " ", value).strip()
