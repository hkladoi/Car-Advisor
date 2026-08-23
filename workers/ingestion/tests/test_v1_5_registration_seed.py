import json
from pathlib import Path

import pytest

from ingestion.registration_seed import load_registration_seed, normalize_provinces, validate_registration_seed
from ingestion.registry import SourceRegistry


ROOT = next(
    candidate
    for candidate in (Path("/app"), Path.cwd(), *Path(__file__).resolve().parents)
    if (candidate / "data" / "source-registry.v1.json").exists()
)


def test_reviewed_registration_seed_and_all_sources_are_registered() -> None:
    registry = SourceRegistry.load(ROOT / "data" / "source-registry.v1.json")
    batch = load_registration_seed(ROOT / "data" / "seed" / "v1.5-registration-rules.json")

    report = validate_registration_seed(batch, registry)

    assert report == {"passed": True, "schema_version": "v1.5", "rules": 8, "sources": 7}


def test_province_mapping_uses_stable_numeric_codes_not_labels() -> None:
    payload = json.dumps(
        [
            {"code": 1, "name": "A label that can change", "wards": [{"code": 11, "name": "Ward A"}]},
            {"code": 79, "name": "Another label", "wards": []},
            {"code": 42, "name": "Not area I by name", "wards": []},
        ]
    ).encode()

    regions = normalize_provinces(payload, expected_count=3)

    assert [(region["code"], region["area_class"]) for region in regions] == [
        ("VN-01", "I"),
        ("VN-79", "I"),
        ("VN-42", "II"),
    ]
    assert regions[0]["wards"][0]["code"] == "VN-W-00011"


def test_province_snapshot_rejects_unexpected_reorganization_count() -> None:
    with pytest.raises(ValueError, match="exactly 34"):
        normalize_provinces(b"[]")
