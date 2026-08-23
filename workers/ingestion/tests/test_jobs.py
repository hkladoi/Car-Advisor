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

