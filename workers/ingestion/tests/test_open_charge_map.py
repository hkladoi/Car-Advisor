from __future__ import annotations

import asyncio

import httpx
import pytest

from ingestion.open_charge_map import (
    OpenChargeMapClient,
    OpenChargeMapConfigurationError,
    OpenChargeMapPayloadError,
    OpenChargeMapProviderError,
    confidence_for_quality,
)


def poi(station_id: int = 101, *, latitude: float = 21.028, longitude: float = 105.834) -> dict[str, object]:
    return {
        "ID": station_id,
        "UUID": f"ocm-{station_id}",
        "UsageCost": "community supplied price text must not be imported",
        "AddressInfo": {
            "Title": "Trạm sạc tham khảo",
            "AddressLine1": "Hoàn Kiếm",
            "Town": "Hà Nội",
            "StateOrProvince": "Hà Nội",
            "Country": {"ISOCode": "VN", "Title": "Viet Nam"},
            "Latitude": latitude,
            "Longitude": longitude,
            "RelatedURL": "https://example.vn/station",
        },
        "OperatorInfo": {"Title": "Example operator"},
        "UsageType": {"Title": "Public"},
        "StatusType": {"Title": "Operational", "IsOperational": True},
        "DataQualityLevel": 4,
        "NumberOfPoints": 2,
        "DateLastStatusUpdate": "2026-08-22T01:02:03Z",
        "Connections": [
            {
                "ID": 901,
                "ConnectionType": {"Title": "CCS (Type 2)"},
                "Level": {"Title": "Level 3: High (Over 40kW)"},
                "CurrentType": {"Title": "DC"},
                "StatusType": {"Title": "Operational", "IsOperational": True},
                "PowerKW": 60,
                "Quantity": 2,
            }
        ],
    }


def test_ocm_adapter_pages_vietnam_and_drops_tariff_like_usage_cost() -> None:
    captured: list[httpx.Request] = []

    def handler(request: httpx.Request) -> httpx.Response:
        captured.append(request)
        return httpx.Response(200, json=[poi()], request=request)

    transport = httpx.MockTransport(handler)

    async def run():
        async with httpx.AsyncClient(transport=transport) as http:
            client = OpenChargeMapClient("server-secret", "VCP test", page_size=1000, client=http)
            return await client.fetch_vietnam()

    result = asyncio.run(run())

    assert result.complete is True
    assert result.page_count == 1
    assert result.rejected_records == 0
    assert result.stations[0].address.title == "Trạm sạc tham khảo"
    assert result.stations[0].connections[0].power_kw == 60
    assert "usage_cost" not in type(result.stations[0]).model_fields
    assert captured[0].url.params["countrycode"] == "VN"
    assert captured[0].url.params["sortby"] == "id_asc"
    assert captured[0].url.params["greaterthanid"] == "0"


def test_ocm_adapter_rejects_out_of_country_coordinates_without_losing_valid_data() -> None:
    def handler(request: httpx.Request) -> httpx.Response:
        return httpx.Response(
            200,
            json=[poi(101), poi(102, latitude=51.5, longitude=-0.1)],
            request=request,
        )

    async def run():
        async with httpx.AsyncClient(transport=httpx.MockTransport(handler)) as http:
            return await OpenChargeMapClient("key", "VCP test", client=http).fetch_vietnam()

    result = asyncio.run(run())

    assert [station.external_id for station in result.stations] == [101]
    assert result.rejected_records == 1


def test_ocm_paging_advances_past_a_malformed_provider_row() -> None:
    requested_after: list[str] = []

    def handler(request: httpx.Request) -> httpx.Response:
        requested_after.append(request.url.params["greaterthanid"])
        if request.url.params["greaterthanid"] == "0":
            return httpx.Response(
                200,
                json=[poi(101), {"ID": 102, "AddressInfo": None}],
                request=request,
            )
        return httpx.Response(200, json=[], request=request)

    async def run():
        async with httpx.AsyncClient(transport=httpx.MockTransport(handler)) as http:
            return await OpenChargeMapClient(
                "key", "VCP test", page_size=2, client=http
            ).fetch_vietnam()

    result = asyncio.run(run())

    assert requested_after == ["0", "102"]
    assert result.complete is True
    assert result.rejected_records == 1
    assert [station.external_id for station in result.stations] == [101]


def test_ocm_stream_is_bounded_before_json_parsing() -> None:
    def handler(request: httpx.Request) -> httpx.Response:
        return httpx.Response(200, content=b"[" + (b" " * 64) + b"]", request=request)

    async def run():
        async with httpx.AsyncClient(transport=httpx.MockTransport(handler)) as http:
            await OpenChargeMapClient(
                "key", "VCP test", max_response_bytes=32, client=http
            ).fetch_vietnam()

    with pytest.raises(OpenChargeMapPayloadError, match="size limit"):
        asyncio.run(run())


def test_ocm_adapter_fails_closed_and_never_leaks_key() -> None:
    async def missing_key():
        async with httpx.AsyncClient(transport=httpx.MockTransport(lambda _: httpx.Response(500))) as http:
            await OpenChargeMapClient("", "VCP test", client=http).fetch_vietnam()

    with pytest.raises(OpenChargeMapConfigurationError):
        asyncio.run(missing_key())

    def handler(request: httpx.Request) -> httpx.Response:
        return httpx.Response(401, request=request)

    async def run():
        async with httpx.AsyncClient(transport=httpx.MockTransport(handler)) as http:
            await OpenChargeMapClient("do-not-leak", "VCP test", client=http).fetch_vietnam()

    with pytest.raises(OpenChargeMapProviderError) as captured:
        asyncio.run(run())
    assert "do-not-leak" not in str(captured.value)
    assert "HTTP 401" in str(captured.value)


@pytest.mark.parametrize(
    ("level", "expected"),
    [(None, "Unknown"), (1, "Low"), (2, "Low"), (3, "Medium"), (4, "High"), (5, "High")],
)
def test_ocm_quality_maps_to_explicit_reference_confidence(level: int | None, expected: str) -> None:
    assert confidence_for_quality(level) == expected
