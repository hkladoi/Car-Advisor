from __future__ import annotations

import asyncio
import json

import httpx
import pytest

from ingestion.discovery import (
    BraveBudgetExceeded,
    BraveSearchClient,
    BraveSearchOptions,
    DiscoveryRequest,
    DiscoveryService,
    MissingBraveApiKey,
    QueryTemplateCatalog,
    UnsafeDiscoveryUrl,
    normalize_discovery_url,
)


class FakeRedis:
    def __init__(self, *, usage: int = 0) -> None:
        self.values: dict[str, str] = {}
        self.usage = usage
        self.eval_calls = 0

    async def get(self, name: str) -> str | None:
        return self.values.get(name)

    async def set(self, name: str, value: str, *, ex: int) -> bool:
        assert ex > 0
        self.values[name] = value
        return True

    async def eval(self, script: str, numkeys: int, *keys_and_args: object) -> int:
        assert "INCR" in script
        assert numkeys == 1
        self.eval_calls += 1
        budget = int(keys_and_args[1])
        if self.usage >= budget:
            return -1
        self.usage += 1
        return self.usage


def templates() -> QueryTemplateCatalog:
    return QueryTemplateCatalog(
        schema_version="v2.1",
        templates={"price": 'site:{domain} "{brand}" "bảng giá"'},
    )


def test_known_urls_win_without_brave_request() -> None:
    redis = FakeRedis()
    client = BraveSearchClient(redis, BraveSearchOptions(api_key="", monthly_request_budget=1))
    service = DiscoveryService(client, templates())

    batch = asyncio.run(
        service.discover(
            DiscoveryRequest(
                brand="VinFast",
                data_type="price",
                allowed_domains=["vinfastauto.com", "vinfastauto.com"],
                known_urls=["https://vinfastauto.com/vn_vi/bang-gia?utm_source=test"],
            )
        )
    )

    assert batch.strategy == "known_url_first"
    assert batch.charged_requests == 0
    assert batch.candidates[0].url == "https://vinfastauto.com/vn_vi/bang-gia"
    assert redis.eval_calls == 0


def test_brave_results_are_allowlisted_normalized_deduplicated_and_cached() -> None:
    calls = 0

    def handler(request: httpx.Request) -> httpx.Response:
        nonlocal calls
        calls += 1
        assert request.headers["X-Subscription-Token"] == "secret"
        assert request.url.params["country"] == "VN"
        return httpx.Response(
            200,
            json={
                "web": {
                    "results": [
                        {
                            "url": "https://www.toyota.com.vn/yaris-cross/?utm_source=brave#prices",
                            "title": "discarded",
                            "description": "must never be persisted",
                        },
                        {"url": "https://www.toyota.com.vn/yaris-cross"},
                        {"url": "https://evil.example/toyota"},
                        {"url": "http://www.toyota.com.vn/insecure"},
                    ]
                }
            },
        )

    redis = FakeRedis()
    client = BraveSearchClient(
        redis,
        BraveSearchOptions(api_key="secret", monthly_request_budget=1000),
        transport=httpx.MockTransport(handler),
    )
    service = DiscoveryService(client, templates())
    request = DiscoveryRequest(
        brand="Toyota",
        data_type="price",
        allowed_domains=["toyota.com.vn"],
        force_discovery=True,
    )

    first = asyncio.run(service.discover(request))
    second = asyncio.run(service.discover(request))

    assert calls == 1
    assert first.charged_requests == 1
    assert second.cache_hits == 1
    assert first.domains == ["www.toyota.com.vn"]
    assert [item.url for item in first.candidates] == [
        "https://www.toyota.com.vn/yaris-cross"
    ]
    cache_payload = " ".join(redis.values.values())
    assert "description" not in cache_payload
    assert "discarded" not in cache_payload


def test_missing_key_and_monthly_budget_fail_before_network() -> None:
    missing = BraveSearchClient(FakeRedis(), BraveSearchOptions(api_key="", monthly_request_budget=1))
    with pytest.raises(MissingBraveApiKey):
        asyncio.run(missing.search("site:toyota.com.vn Toyota", ["toyota.com.vn"]))

    exhausted = BraveSearchClient(
        FakeRedis(usage=1), BraveSearchOptions(api_key="secret", monthly_request_budget=1)
    )
    with pytest.raises(BraveBudgetExceeded):
        asyncio.run(exhausted.search("site:toyota.com.vn Toyota", ["toyota.com.vn"]))


def test_discovery_rejects_ssrf_and_nonofficial_urls() -> None:
    with pytest.raises(UnsafeDiscoveryUrl):
        normalize_discovery_url("https://127.0.0.1/admin", ["127.0.0.1"])
    with pytest.raises(UnsafeDiscoveryUrl):
        normalize_discovery_url("https://evil.example/path", ["toyota.com.vn"])
    with pytest.raises(UnsafeDiscoveryUrl):
        normalize_discovery_url("https://user:pass@toyota.com.vn/path", ["toyota.com.vn"])


def test_template_contract_is_strict() -> None:
    catalog = QueryTemplateCatalog.model_validate_json(
        json.dumps(
            {
                "schema_version": "v2.1",
                "templates": {"specs": 'site:{domain} "{brand}" specs'},
            }
        )
    )
    assert catalog.queries("  Kia  ", "specs", ["kia.com", "kia.com"]) == [
        'site:kia.com "Kia" specs'
    ]
