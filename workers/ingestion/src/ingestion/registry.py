from __future__ import annotations

import json
from enum import StrEnum
from pathlib import Path
from urllib.parse import urlsplit

from pydantic import BaseModel, ConfigDict, Field, model_validator


class Authority(StrEnum):
    COMPETENT_AUTHORITY = "CompetentAuthority"
    BRAND_OFFICIAL = "BrandOfficial"
    DISTRIBUTOR_OFFICIAL = "DistributorOfficial"
    DEALER_OFFICIAL = "DealerOfficial"
    TRUSTED_SECONDARY = "TrustedSecondary"
    DISCOVERY_ONLY = "DiscoveryOnly"


class ContentType(StrEnum):
    HTML = "Html"
    PDF = "Pdf"
    JSON = "Json"
    XML = "Xml"
    MANUAL_DOCUMENT = "ManualDocument"


class RegistrySource(BaseModel):
    model_config = ConfigDict(extra="forbid")

    id: str = Field(pattern=r"^[a-z0-9]+(?:-[a-z0-9]+)*$")
    name: str = Field(min_length=1, max_length=240)
    owner: str = Field(min_length=1, max_length=240)
    category: str = Field(min_length=1, max_length=80)
    url: str
    allowed_domains: list[str] = Field(min_length=1)
    authority: Authority
    content_type: ContentType
    refresh_hours: int = Field(ge=1, le=8760)
    priority: int = Field(ge=0, le=1000)
    robots_note: str = Field(min_length=1, max_length=2000)
    terms_note: str = Field(min_length=1, max_length=2000)
    automated_fetch: bool = True

    @model_validator(mode="after")
    def validate_origin(self) -> "RegistrySource":
        parsed = urlsplit(self.url)
        if parsed.scheme != "https" or not parsed.hostname:
            raise ValueError("Registry URLs must be absolute HTTPS URLs")
        normalized_domains = {domain.lower().strip(".") for domain in self.allowed_domains}
        if parsed.hostname.lower() not in normalized_domains:
            raise ValueError("Registry URL hostname must be explicitly allowlisted")
        if self.authority is Authority.DISCOVERY_ONLY and self.automated_fetch:
            raise ValueError("Discovery-only sources cannot be fetched/published as facts")
        return self

    def allows_url(self, url: str) -> bool:
        parsed = urlsplit(url)
        return parsed.scheme == "https" and parsed.hostname is not None and parsed.hostname.lower() in {
            domain.lower() for domain in self.allowed_domains
        }


class SourceRegistry(BaseModel):
    model_config = ConfigDict(extra="forbid")

    schema_version: str
    sources: list[RegistrySource] = Field(min_length=1)

    @model_validator(mode="after")
    def unique_ids_and_urls(self) -> "SourceRegistry":
        ids = [source.id for source in self.sources]
        urls = [source.url for source in self.sources]
        if len(ids) != len(set(ids)):
            raise ValueError("Source registry IDs must be unique")
        if len(urls) != len(set(urls)):
            raise ValueError("Source registry URLs must be unique")
        return self

    def by_id(self, source_id: str) -> RegistrySource:
        for source in self.sources:
            if source.id == source_id:
                return source
        raise KeyError(f"Unknown source registry ID: {source_id}")

    @classmethod
    def load(cls, path: Path) -> "SourceRegistry":
        return cls.model_validate(json.loads(path.read_text(encoding="utf-8")))
