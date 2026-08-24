from __future__ import annotations

import asyncio
import json
from datetime import UTC, datetime
from pathlib import Path
from types import SimpleNamespace

import pytest
from pydantic import ValidationError

from ingestion import cli
from ingestion.fetcher import Snapshot
from ingestion.market_scope import load_market_scope, validate_market_scope
from ingestion.registry import RegistrySource, SourceRegistry


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


def test_market_scope_fetch_retries_only_failed_sources_after_batch(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    registry = SourceRegistry.load(ROOT / "data" / "source-registry.v1.json")

    class MemoryStorage:
        def __init__(self, _settings: object) -> None:
            pass

    class FlakyFetcher:
        calls: dict[str, int] = {}

        def __init__(self, _user_agent: str) -> None:
            pass

        async def fetch(self, source: RegistrySource, _storage: object) -> Snapshot:
            source_id = source.id
            self.calls[source_id] = self.calls.get(source_id, 0) + 1
            if source_id == "mercedes-vietnam-market" and self.calls[source_id] == 1:
                raise RuntimeError("transient manufacturer CDN throttle")
            return Snapshot(
                source_id=source_id,
                source_url=source.url,
                final_url=source.url,
                fetched_at=datetime(2026, 8, 24, tzinfo=UTC),
                content_hash=f"hash-{source_id}",
                object_key=f"sources/{source_id}/snapshot.xml",
                http_status=200,
                content_type="application/xml",
                etag=None,
                last_modified=None,
                size_bytes=100,
                fetch_method="http",
            )

    async def no_sleep(_seconds: float) -> None:
        return None

    asyncio_proxy = SimpleNamespace(
        Semaphore=asyncio.Semaphore,
        gather=asyncio.gather,
        wait_for=asyncio.wait_for,
        sleep=no_sleep,
    )
    monkeypatch.setattr(cli, "S3CompatibleObjectStorage", MemoryStorage)
    monkeypatch.setattr(cli, "KnownUrlFetcher", FlakyFetcher)
    monkeypatch.setattr(cli, "asyncio", asyncio_proxy)

    snapshots = asyncio.run(
        cli._fetch_market_scope_sources(
            registry,
            {"mercedes-vietnam-market", "suzuki-vietnam-market"},
            SimpleNamespace(ingestion_user_agent="test", ingestion_max_concurrency=2),  # type: ignore[arg-type]
        )
    )

    assert {snapshot.source_id for snapshot in snapshots} == {
        "mercedes-vietnam-market",
        "suzuki-vietnam-market",
    }
    assert FlakyFetcher.calls == {
        "mercedes-vietnam-market": 2,
        "suzuki-vietnam-market": 1,
    }
