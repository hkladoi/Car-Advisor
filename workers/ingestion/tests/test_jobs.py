from datetime import UTC

import pytest
from pydantic import ValidationError

from ingestion.jobs import IngestionJob
from ingestion.monitoring import job_for_schedule, schedule_definitions
from ingestion.registry import Authority, ContentType, RegistrySource


def test_staleness_job_is_timezone_aware() -> None:
    job = IngestionJob.staleness_check()

    assert job.job_type == "source_staleness_check"
    assert job.requested_at.tzinfo == UTC
    assert job.monitor_kind == "source_staleness_check"
    assert job.run_id is not None


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
    assert job.monitor_kind == "new_model_discovery"


def source(category: str, refresh_hours: int = 720) -> RegistrySource:
    return RegistrySource(
        id=f"test-{category}",
        name="Official test source",
        owner="Official owner",
        category=category,
        url="https://official.example/data",
        allowed_domains=["official.example"],
        authority=Authority.BRAND_OFFICIAL,
        content_type=ContentType.HTML,
        refresh_hours=refresh_hours,
        priority=10,
        robots_note="Public known URL with bounded conditional fetch.",
        terms_note="Facts and provenance only.",
    )


def test_monitoring_schedule_separates_daily_and_weekly_vehicle_jobs() -> None:
    schedules = schedule_definitions(source("vehicle"))

    assert [(value.monitor_kind, value.cadence_hours) for value in schedules] == [
        ("vehicle_price_promotion", 24),
        ("vehicle_specs_features", 168),
        ("vehicle_images_colors", 168),
    ]


@pytest.mark.parametrize(
    ("category", "monitor_kind"),
    [
        ("dealer-offer", "dealer_offers"),
        ("finance-campaign", "finance_campaign_reference"),
        ("brand-registry", "new_model_discovery"),
        ("fuel-price", "fuel_price"),
        ("electricity-price", "electricity_tariff"),
        ("charging-price", "charging_tariff_promotion"),
        ("registration-rule", "registration_legal_rules"),
    ],
)
def test_daily_watch_categories_have_explicit_monitor_kinds(category: str, monitor_kind: str) -> None:
    schedule = schedule_definitions(source(category))[0]

    assert schedule.monitor_kind == monitor_kind
    assert schedule.cadence_hours == 24


def test_brand_registry_schedule_builds_bounded_discovery_instead_of_fetch() -> None:
    registry_source = source("brand-registry", refresh_hours=168)
    schedule = schedule_definitions(registry_source)[0]

    job = job_for_schedule(registry_source, schedule)

    assert job.job_type == "source_discovery"
    assert job.monitor_kind == "new_model_discovery"
    assert job.data_type == "vehicle"
    assert job.allowed_domains == registry_source.allowed_domains
    assert job.known_urls == [registry_source.url]
    assert job.source_id == registry_source.id


def test_charging_poi_schedule_uses_provider_adapter_job() -> None:
    registry_source = source("charging-poi", refresh_hours=168)
    schedule = schedule_definitions(registry_source)[0]

    job = job_for_schedule(registry_source, schedule)

    assert job.job_type == "charging_poi_sync"
    assert job.monitor_kind == "charging_poi_locations"
    assert job.source_id == registry_source.id
