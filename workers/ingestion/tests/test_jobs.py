from datetime import UTC

import pytest
from pydantic import ValidationError

from ingestion.jobs import IngestionJob


def test_staleness_job_is_timezone_aware() -> None:
    job = IngestionJob.staleness_check()

    assert job.job_type == "source_staleness_check"
    assert job.requested_at.tzinfo == UTC


def test_unknown_job_type_is_rejected() -> None:
    with pytest.raises(ValidationError):
        IngestionJob.model_validate(
            {"job_type": "publish_without_review", "requested_at": "2026-08-22T00:00:00Z"}
        )


def test_discovery_job_requires_a_complete_official_scope() -> None:
    with pytest.raises(ValidationError):
        IngestionJob.model_validate(
            {
                "job_type": "source_discovery",
                "requested_at": "2026-08-22T00:00:00Z",
                "brand": "Toyota",
                "data_type": "price",
            }
        )

    job = IngestionJob.discovery(
        "Toyota", "price", ["toyota.com.vn"], force_discovery=True
    )
    assert job.job_type == "source_discovery"
    assert job.force_discovery is True
