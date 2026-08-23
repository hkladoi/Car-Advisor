from __future__ import annotations

from datetime import UTC, datetime
from typing import Literal

from pydantic import BaseModel, ConfigDict, model_validator


class IngestionJob(BaseModel):
    model_config = ConfigDict(extra="forbid")

    job_type: Literal["source_staleness_check", "known_url_fetch"]
    requested_at: datetime
    source_id: str | None = None

    @classmethod
    def staleness_check(cls) -> "IngestionJob":
        return cls(job_type="source_staleness_check", requested_at=datetime.now(UTC))

    @classmethod
    def known_url(cls, source_id: str) -> "IngestionJob":
        return cls(job_type="known_url_fetch", source_id=source_id, requested_at=datetime.now(UTC))

    @model_validator(mode="after")
    def require_source_for_fetch(self) -> "IngestionJob":
        if self.job_type == "known_url_fetch" and not self.source_id:
            raise ValueError("known_url_fetch requires source_id")
        return self
