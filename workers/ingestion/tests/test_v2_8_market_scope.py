from __future__ import annotations

import json
from pathlib import Path

import pytest
from pydantic import ValidationError

from ingestion.market_scope import load_market_scope, validate_market_scope
from ingestion.registry import SourceRegistry


ROOT = Path(__file__).resolve().parents[3]


def test_reviewed_market_scope_is_closed_and_authoritative() -> None:
    registry = SourceRegistry.load(ROOT / "data" / "source-registry.v1.json")
    scope = load_market_scope(ROOT / "data" / "manifests" / "v2.8-vietnam-market-scope.json")

    report = validate_market_scope(scope, registry)

    assert report["included_brands"] == 38
    assert report["excluded_brands"] == 13
    assert report["model_candidates"] == 255
    assert report["trim_candidates"] == 49
    assert report["blocked_models"] == 0
    assert report["blocked_trims"] == 0
    assert report["source_count"] == 43
    by_slug = {brand.slug: brand for brand in scope.brands}
    assert by_slug["porsche"].included
    assert all(not by_slug[slug].included for slug in ("ferrari", "lamborghini", "lotus"))
    assert all(
        model.trim_inventory_reason
        for brand in scope.brands if brand.included
        for model in brand.models
        if model.trim_inventory_status.value == "BlockedWithReason"
    )


def test_scope_rejects_silent_trim_inventory_gap(tmp_path: Path) -> None:
    source = ROOT / "data" / "manifests" / "v2.8-vietnam-market-scope.json"
    payload = json.loads(source.read_text(encoding="utf-8"))
    payload["brands"][0]["models"][0]["trim_inventory_reason"] = None
    candidate = tmp_path / "invalid-scope.json"
    candidate.write_text(json.dumps(payload), encoding="utf-8")

    with pytest.raises(ValidationError, match="Blocked trim inventory requires"):
        load_market_scope(candidate)


def test_scope_rejects_missing_required_supercar_exclusion(tmp_path: Path) -> None:
    source = ROOT / "data" / "manifests" / "v2.8-vietnam-market-scope.json"
    payload = json.loads(source.read_text(encoding="utf-8"))
    payload["brands"] = [brand for brand in payload["brands"] if brand["slug"] != "lotus"]
    candidate = tmp_path / "invalid-scope.json"
    candidate.write_text(json.dumps(payload), encoding="utf-8")

    with pytest.raises(ValidationError, match="Configured supercar exclusions missing: lotus"):
        load_market_scope(candidate)
