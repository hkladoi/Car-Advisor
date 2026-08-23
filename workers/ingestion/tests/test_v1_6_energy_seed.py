from pathlib import Path

import pytest
from pydantic import ValidationError

from ingestion.energy_seed import EnergySeedBatch, load_energy_seed, validate_energy_seed
from ingestion.registry import SourceRegistry


ROOT = next(
    candidate
    for candidate in (Path("/app"), Path.cwd(), *Path(__file__).resolve().parents)
    if (candidate / "data" / "source-registry.v1.json").exists()
)


def test_reviewed_energy_seed_has_complete_v1_6_current_rates_and_profiles() -> None:
    registry = SourceRegistry.load(ROOT / "data" / "source-registry.v1.json")
    batch = load_energy_seed(ROOT / "data" / "seed" / "v1.6-energy.json")

    report = validate_energy_seed(batch, registry)

    assert report == {
        "passed": True,
        "schema_version": "v1.6",
        "fuel_prices": 3,
        "electricity_tiers": 6,
        "charging_tariffs": 1,
        "charging_promotions": 1,
        "vehicle_profiles": 3,
        "sources": 7,
    }


def test_phev_profile_requires_separate_consumption_condition_labels() -> None:
    batch = load_energy_seed(ROOT / "data" / "seed" / "v1.6-energy.json")
    payload = batch.model_dump(mode="json")
    payload["vehicle_profiles"][2]["fuel_consumption_condition"] = None

    with pytest.raises(ValidationError, match="Fuel consumption must carry its test condition"):
        EnergySeedBatch.model_validate(payload)


def test_household_tiers_must_be_contiguous_and_complete() -> None:
    registry = SourceRegistry.load(ROOT / "data" / "source-registry.v1.json")
    batch = load_energy_seed(ROOT / "data" / "seed" / "v1.6-energy.json")
    payload = batch.model_dump(mode="json")
    payload["energy_prices"][3]["tier_to_inclusive"] = 49
    candidate = EnergySeedBatch.model_validate(payload)

    with pytest.raises(ValueError, match="canonical six EVN marginal tiers"):
        validate_energy_seed(candidate, registry)
