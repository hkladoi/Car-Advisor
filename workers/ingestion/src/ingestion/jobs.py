from __future__ import annotations

from datetime import UTC, datetime
from typing import Literal

from pydantic import BaseModel, ConfigDict, Field, model_validator


class IngestionJob(BaseModel):
    model_config = ConfigDict(extra="forbid")

    job_type: Literal["source_staleness_check", "known_url_fetch", "source_discovery"]
    requested_at: datetime
    source_id: str | None = None
    brand: str | None = None
    data_type: str | None = None
    allowed_domains: list[str] = Field(default_factory=list)
    known_urls: list[str] = Field(default_factory=list)
    force_discovery: bool = False

    @classmethod
    def staleness_check(cls) -> "IngestionJob":
        return cls(job_type="source_staleness_check", requested_at=datetime.now(UTC))

    @classmethod
    def known_url(cls, source_id: str) -> "IngestionJob":
        return cls(job_type="known_url_fetch", source_id=source_id, requested_at=datetime.now(UTC))

    @classmethod
    def discovery(
        cls,
        brand: str,
        data_type: str,
        allowed_domains: list[str],
        known_urls: list[str] | None = None,
        force_discovery: bool = False,
    ) -> "IngestionJob":
        return cls(
            job_type="source_discovery",
            brand=brand,
            data_type=data_type,
            allowed_domains=allowed_domains,
            known_urls=known_urls or [],
            force_discovery=force_discovery,
            requested_at=datetime.now(UTC),
        )

    @model_validator(mode="after")
    def require_source_for_fetch(self) -> "IngestionJob":
        if self.job_type == "known_url_fetch" and not self.source_id:
            raise ValueError("known_url_fetch requires source_id")
        if self.job_type == "source_discovery" and (
            not self.brand or not self.data_type or not self.allowed_domains
        ):
            raise ValueError("source_discovery requires brand, data_type and allowed_domains")
        return self
