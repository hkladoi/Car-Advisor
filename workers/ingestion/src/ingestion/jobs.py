from __future__ import annotations

from datetime import UTC, datetime
from typing import Literal
import uuid

from pydantic import BaseModel, ConfigDict, Field, model_validator


class IngestionJob(BaseModel):
    model_config = ConfigDict(extra="forbid")

    job_type: Literal["source_staleness_check", "known_url_fetch", "source_discovery"]
    run_id: uuid.UUID = Field(default_factory=uuid.uuid4)
    requested_at: datetime
    monitor_kind: str = Field(default="source_refresh", min_length=1, max_length=100)
    source_id: str | None = None
    brand: str | None = None
    data_type: str | None = None
    allowed_domains: list[str] = Field(default_factory=list)
    known_urls: list[str] = Field(default_factory=list)
    force_discovery: bool = False

    @classmethod
    def staleness_check(cls) -> "IngestionJob":
        return cls(
            job_type="source_staleness_check",
            monitor_kind="source_staleness_check",
            requested_at=datetime.now(UTC),
        )

    @classmethod
    def known_url(cls, source_id: str, monitor_kind: str = "source_refresh") -> "IngestionJob":
        return cls(
            job_type="known_url_fetch",
            monitor_kind=monitor_kind,
            source_id=source_id,
            requested_at=datetime.now(UTC),
        )

    @classmethod
    def discovery(
        cls,
        brand: str,
        data_type: str,
        allowed_domains: list[str],
        known_urls: list[str] | None = None,
        force_discovery: bool = False,
        source_id: str | None = None,
    ) -> "IngestionJob":
        return cls(
            job_type="source_discovery",
            monitor_kind="new_model_discovery",
            source_id=source_id,
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
