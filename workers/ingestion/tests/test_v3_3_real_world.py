import uuid
from decimal import Decimal
from pathlib import Path

import pytest

from ingestion.real_world import (
    DATASET_REPORTING_YEAR,
    DATASET_VERSION,
    REVIEWED_MANUFACTURER_BRAND_SLUGS,
    RealWorldConsumptionPublisher,
    parse_eea_aggregate,
)
from ingestion.registry import SourceRegistry


ROOT = next(
    candidate
    for candidate in (Path("/app"), Path.cwd(), *Path(__file__).resolve().parents)
    if (candidate / "data" / "source-registry.v1.json").exists()
)
FIXTURE = ROOT / "data" / "fixtures" / "real-world" / "eea-2023-cars-aggregate-excerpt.csv"


def test_official_eea_excerpt_preserves_obfcm_wltp_method_and_sample_size() -> None:
    rows = parse_eea_aggregate(FIXTURE.read_bytes())

    assert len(rows) == 6
    assert {row.vehicle_registration_year for row in rows} == {2021, 2022, 2023}
    toyota = next(
        row
        for row in rows
        if row.vehicle_registration_year == 2023 and row.manufacturer == "TOYOTA"
    )
    assert toyota.sample_size == 1702
    assert toyota.real_world_fuel_weighted_litres_per100km == Decimal("4.62")
    assert toyota.official_wltp_fuel_weighted_litres_per100km == Decimal("0.97")
    assert toyota.fuel_weighted_percentage_gap == Decimal("377.97")
    assert DATASET_REPORTING_YEAR == 2024


def test_parser_rejects_missing_methodology_columns_and_zero_sample() -> None:
    content = FIXTURE.read_text(encoding="utf-8")
    missing = content.replace(",\"percentage gap CO2 emissions weighted (%)\"", "", 1)
    with pytest.raises(ValueError, match="missing required columns"):
        parse_eea_aggregate(missing.encode())

    zero = content.replace('"1702"', '"0"', 1)
    with pytest.raises(ValueError, match="greater than 0"):
        parse_eea_aggregate(zero.encode())


def test_only_reviewed_exact_manufacturers_map_to_vietnam_brands() -> None:
    assert REVIEWED_MANUFACTURER_BRAND_SLUGS["TOYOTA MOTOR CORPORATION"] == "toyota"
    assert REVIEWED_MANUFACTURER_BRAND_SLUGS["MERCEDES-BENZ AG"] == "mercedes-benz"
    assert "STELLANTIS EUROPE" not in REVIEWED_MANUFACTURER_BRAND_SLUGS
    assert "SAIC MOTOR CORPORATION" not in REVIEWED_MANUFACTURER_BRAND_SLUGS


def test_registry_declares_official_csv_with_attribution_policy() -> None:
    registry = SourceRegistry.load(ROOT / "data" / "source-registry.v1.json")
    source = registry.by_id("eea-real-world-cars-2023-aggregate")

    assert source.authority.value == "CompetentAuthority"
    assert source.content_type.value == "Csv"
    assert source.allowed_domains == ["sdi.eea.europa.eu"]
    assert "attribution" in source.terms_note.lower()
    assert "never infer a Vietnam trim" in source.terms_note


def test_reconciliation_targets_only_stale_rows_in_current_dataset() -> None:
    calls: list[tuple[str, tuple[object, ...]]] = []

    class Cursor:
        def __enter__(self) -> "Cursor":
            return self

        def __exit__(self, *_: object) -> None:
            return None

        def execute(self, query: str, params: tuple[object, ...]) -> None:
            calls.append((query, params))

    class Connection:
        def cursor(self) -> Cursor:
            return Cursor()

    current_ids = [uuid.uuid4(), uuid.uuid4()]
    RealWorldConsumptionPublisher._delete_stale_rows(Connection(), current_ids)  # type: ignore[arg-type]

    assert len(calls) == 1
    assert "DELETE FROM real_world_consumption_aggregates" in calls[0][0]
    assert "NOT (id = ANY" in calls[0][0]
    assert calls[0][1] == (DATASET_VERSION, current_ids)
