from __future__ import annotations

import asyncio
import hashlib
import json
import uuid
from dataclasses import dataclass
from datetime import UTC, datetime
from typing import Any

import httpx
import psycopg
from pydantic import BaseModel, ConfigDict, Field, ValidationError


_STATION_NAMESPACE = uuid.UUID("810b53ed-7c37-474c-b3e8-9bef8497fc1a")
_CONNECTOR_NAMESPACE = uuid.UUID("cb6336a4-7610-4870-93f7-1d2901757e74")


class OpenChargeMapError(RuntimeError):
    """Safe provider failure whose message never contains the API key."""


class OpenChargeMapConfigurationError(OpenChargeMapError):
    pass


class OpenChargeMapProviderError(OpenChargeMapError):
    pass


class OpenChargeMapPayloadError(OpenChargeMapError):
    pass


class ReferenceValue(BaseModel):
    model_config = ConfigDict(extra="ignore", populate_by_name=True)

    title: str | None = Field(default=None, alias="Title")
    is_operational: bool | None = Field(default=None, alias="IsOperational")
    iso_code: str | None = Field(default=None, alias="ISOCode")


class AddressInfo(BaseModel):
    model_config = ConfigDict(extra="ignore", populate_by_name=True)

    title: str = Field(alias="Title", min_length=1, max_length=1000)
    address_line_1: str | None = Field(default=None, alias="AddressLine1", max_length=4000)
    address_line_2: str | None = Field(default=None, alias="AddressLine2", max_length=4000)
    town: str | None = Field(default=None, alias="Town", max_length=1000)
    state_or_province: str | None = Field(default=None, alias="StateOrProvince", max_length=1000)
    postcode: str | None = Field(default=None, alias="Postcode", max_length=200)
    country: ReferenceValue | None = Field(default=None, alias="Country")
    latitude: float = Field(alias="Latitude", ge=-90, le=90)
    longitude: float = Field(alias="Longitude", ge=-180, le=180)
    related_url: str | None = Field(default=None, alias="RelatedURL", max_length=4000)


class ConnectorInfo(BaseModel):
    model_config = ConfigDict(extra="ignore", populate_by_name=True)

    external_id: int = Field(alias="ID", gt=0)
    connection_type: ReferenceValue | None = Field(default=None, alias="ConnectionType")
    status_type: ReferenceValue | None = Field(default=None, alias="StatusType")
    level: ReferenceValue | None = Field(default=None, alias="Level")
    current_type: ReferenceValue | None = Field(default=None, alias="CurrentType")
    amps: int | None = Field(default=None, alias="Amps", ge=0, le=1000)
    voltage: int | None = Field(default=None, alias="Voltage", ge=0, le=10000)
    power_kw: float | None = Field(default=None, alias="PowerKW", ge=0, le=1000)
    quantity: int | None = Field(default=None, alias="Quantity", ge=0, le=500)


class ChargingPoi(BaseModel):
    model_config = ConfigDict(extra="ignore", populate_by_name=True)

    external_id: int = Field(alias="ID", gt=0)
    external_uuid: str | None = Field(default=None, alias="UUID", max_length=100)
    address: AddressInfo = Field(alias="AddressInfo")
    connections: list[ConnectorInfo] = Field(default_factory=list, alias="Connections")
    operator: ReferenceValue | None = Field(default=None, alias="OperatorInfo")
    usage_type: ReferenceValue | None = Field(default=None, alias="UsageType")
    status_type: ReferenceValue | None = Field(default=None, alias="StatusType")
    number_of_points: int | None = Field(default=None, alias="NumberOfPoints", ge=0, le=500)
    data_quality_level: int | None = Field(default=None, alias="DataQualityLevel", ge=1, le=5)
    external_updated_at: datetime | None = Field(default=None, alias="DateLastStatusUpdate")
    last_confirmed_at: datetime | None = Field(default=None, alias="DateLastConfirmed")

    @property
    def country_code(self) -> str:
        return (self.address.country.iso_code if self.address.country else None) or "VN"


@dataclass(frozen=True, slots=True)
class OpenChargeMapFetchResult:
    stations: tuple[ChargingPoi, ...]
    snapshot_bytes: bytes
    rejected_records: int
    page_count: int
    complete: bool
    fetched_at: datetime
    http_status: int = 200

    @property
    def content_hash(self) -> str:
        return hashlib.sha256(self.snapshot_bytes).hexdigest()


@dataclass(frozen=True, slots=True)
class ChargingPoiSyncOutcome:
    imported_stations: int
    imported_connectors: int
    deactivated_stations: int


class OpenChargeMapClient:
    ENDPOINT = "https://api.openchargemap.io/v3/poi"

    def __init__(
        self,
        api_key: str,
        user_agent: str,
        *,
        timeout_seconds: float = 15,
        retries: int = 3,
        page_size: int = 1000,
        max_stations: int = 20_000,
        max_response_bytes: int = 25_000_000,
        client: httpx.AsyncClient | None = None,
    ) -> None:
        self._api_key = api_key.strip()
        self._timeout = timeout_seconds
        self._retries = retries
        self._page_size = page_size
        self._max_stations = max_stations
        self._max_response_bytes = max_response_bytes
        self._client = client or httpx.AsyncClient(
            headers={"User-Agent": user_agent, "Accept": "application/json"},
            follow_redirects=False,
        )
        self._owns_client = client is None

    async def close(self) -> None:
        if self._owns_client:
            await self._client.aclose()

    async def fetch_vietnam(self) -> OpenChargeMapFetchResult:
        if not self._api_key:
            raise OpenChargeMapConfigurationError(
                "OPEN_CHARGE_MAP_API_KEY is required for the optional charging-location sync."
            )

        raw_records: list[dict[str, Any]] = []
        stations: list[ChargingPoi] = []
        rejected = 0
        greater_than_id = 0
        page_count = 0
        complete = False
        total_response_bytes = 0
        while len(stations) < self._max_stations:
            page, page_bytes = await self._fetch_page(greater_than_id)
            total_response_bytes += page_bytes
            if total_response_bytes > self._max_response_bytes:
                raise OpenChargeMapPayloadError(
                    "Open Charge Map country sync exceeded the configured total size limit."
                )
            page_count += 1
            if not page:
                complete = True
                break
            last_id = greater_than_id
            for raw in page:
                raw_records.append(raw)
                raw_id = raw.get("ID")
                if isinstance(raw_id, int) and not isinstance(raw_id, bool) and raw_id > 0:
                    # Paging must advance even when a provider row fails the
                    # stricter local schema; otherwise one malformed final row
                    # can make every later sync fail on the same page.
                    last_id = max(last_id, raw_id)
                try:
                    station = ChargingPoi.model_validate(raw)
                except ValidationError:
                    rejected += 1
                    continue
                if not _is_vietnam_location(station):
                    rejected += 1
                    continue
                stations.append(station)
                if len(stations) >= self._max_stations:
                    break
            if last_id <= greater_than_id:
                raise OpenChargeMapPayloadError(
                    "Open Charge Map paging did not advance; cached charging locations were retained."
                )
            greater_than_id = last_id
            if len(page) < self._page_size:
                complete = True
                break

        if not stations:
            raise OpenChargeMapPayloadError(
                "Open Charge Map returned no valid Vietnam locations; cached charging locations were retained."
            )
        payload = json.dumps(
            {
                "provider": "OpenChargeMap",
                "countryCode": "VN",
                "complete": complete,
                "pages": page_count,
                "records": raw_records,
            },
            ensure_ascii=False,
            separators=(",", ":"),
        ).encode("utf-8")
        return OpenChargeMapFetchResult(
            tuple(stations), payload, rejected, page_count, complete, datetime.now(UTC)
        )

    async def _fetch_page(self, greater_than_id: int) -> tuple[list[dict[str, Any]], int]:
        params = {
            "key": self._api_key,
            "output": "json",
            "countrycode": "VN",
            "maxresults": str(self._page_size),
            "sortby": "id_asc",
            "greaterthanid": str(greater_than_id),
            "compact": "false",
            "verbose": "true",
            "includecomments": "false",
        }
        for attempt in range(1, self._retries + 1):
            try:
                async with self._client.stream(
                    "GET", self.ENDPOINT, params=params, timeout=self._timeout
                ) as response:
                    status_code = response.status_code
                    retry_after = response.headers.get("Retry-After")
                    content = bytearray()
                    if status_code == 200:
                        async for chunk in response.aiter_bytes():
                            if len(content) + len(chunk) > self._max_response_bytes:
                                raise OpenChargeMapPayloadError(
                                    "Open Charge Map response exceeded the configured size limit."
                                )
                            content.extend(chunk)
            except (httpx.TimeoutException, httpx.NetworkError) as error:
                if attempt == self._retries:
                    raise OpenChargeMapProviderError(
                        "Open Charge Map was unavailable after bounded retries."
                    ) from error
                await asyncio.sleep(0.25 * attempt)
                continue
            if status_code == 200:
                try:
                    value = json.loads(content)
                except (UnicodeDecodeError, ValueError) as error:
                    raise OpenChargeMapPayloadError(
                        "Open Charge Map returned invalid JSON."
                    ) from error
                if not isinstance(value, list) or not all(isinstance(item, dict) for item in value):
                    raise OpenChargeMapPayloadError(
                        "Open Charge Map returned an unexpected payload shape."
                    )
                return value, len(content)
            if status_code not in {429, 500, 502, 503, 504} or attempt == self._retries:
                raise OpenChargeMapProviderError(
                    f"Open Charge Map returned HTTP {status_code}; cached charging locations were retained."
                )
            delay = min(float(retry_after), 2.0) if retry_after and retry_after.isdigit() else 0.25 * attempt
            await asyncio.sleep(delay)
        raise AssertionError("bounded retry loop must return or raise")


class ChargingPoiRepository:
    def __init__(self, dsn: str) -> None:
        self._dsn = dsn

    def synchronize(
        self,
        stations: tuple[ChargingPoi, ...],
        snapshot_id: uuid.UUID,
        imported_at: datetime,
        *,
        complete: bool,
    ) -> ChargingPoiSyncOutcome:
        connector_count = 0
        external_ids: list[int] = []
        with psycopg.connect(self._dsn) as connection, connection.transaction(), connection.cursor() as cursor:
            for station in stations:
                station_id = uuid.uuid5(_STATION_NAMESPACE, f"OpenChargeMap:{station.external_id}")
                external_ids.append(station.external_id)
                cursor.execute(
                    """
                    INSERT INTO charging_stations
                        (id, external_source, external_id, external_uuid, source_snapshot_id,
                         name, address_line1, address_line2, town, state_or_province,
                         postcode, country_code, latitude, longitude, operator_name,
                         usage_type, operational_status, is_operational, number_of_points,
                         external_data_quality_level, coverage, confidence, related_url,
                         external_updated_at, last_confirmed_at, imported_at, last_seen_at,
                         active, created_at, updated_at)
                    VALUES
                        (%s, 'OpenChargeMap', %s, %s, %s, %s, %s, %s, %s, %s,
                         %s, 'VN', %s, %s, %s, %s, %s, %s, %s, %s,
                         'ReferenceOnly', %s, %s, %s, %s, %s, %s, TRUE, %s, %s)
                    ON CONFLICT (external_source, external_id) DO UPDATE SET
                        external_uuid = EXCLUDED.external_uuid,
                        source_snapshot_id = EXCLUDED.source_snapshot_id,
                        name = EXCLUDED.name,
                        address_line1 = EXCLUDED.address_line1,
                        address_line2 = EXCLUDED.address_line2,
                        town = EXCLUDED.town,
                        state_or_province = EXCLUDED.state_or_province,
                        postcode = EXCLUDED.postcode,
                        country_code = EXCLUDED.country_code,
                        latitude = EXCLUDED.latitude,
                        longitude = EXCLUDED.longitude,
                        operator_name = EXCLUDED.operator_name,
                        usage_type = EXCLUDED.usage_type,
                        operational_status = EXCLUDED.operational_status,
                        is_operational = EXCLUDED.is_operational,
                        number_of_points = EXCLUDED.number_of_points,
                        external_data_quality_level = EXCLUDED.external_data_quality_level,
                        coverage = EXCLUDED.coverage,
                        confidence = EXCLUDED.confidence,
                        related_url = EXCLUDED.related_url,
                        external_updated_at = EXCLUDED.external_updated_at,
                        last_confirmed_at = EXCLUDED.last_confirmed_at,
                        imported_at = EXCLUDED.imported_at,
                        last_seen_at = EXCLUDED.last_seen_at,
                        active = TRUE,
                        updated_at = EXCLUDED.updated_at
                    RETURNING id
                    """,
                    (
                        station_id,
                        station.external_id,
                        _trim(station.external_uuid, 100),
                        snapshot_id,
                        _trim(station.address.title, 240) or f"OCM {station.external_id}",
                        _trim(station.address.address_line_1, 1000),
                        _trim(station.address.address_line_2, 1000),
                        _trim(station.address.town, 160),
                        _trim(station.address.state_or_province, 160),
                        _trim(station.address.postcode, 40),
                        station.address.latitude,
                        station.address.longitude,
                        _trim(station.operator.title if station.operator else None, 240),
                        _trim(station.usage_type.title if station.usage_type else None, 160),
                        _trim(station.status_type.title if station.status_type else None, 160),
                        station.status_type.is_operational if station.status_type else None,
                        station.number_of_points,
                        station.data_quality_level,
                        confidence_for_quality(station.data_quality_level),
                        _safe_related_url(station.address.related_url),
                        _utc(station.external_updated_at),
                        _utc(station.last_confirmed_at),
                        imported_at,
                        imported_at,
                        imported_at,
                        imported_at,
                    ),
                )
                actual_station_id: uuid.UUID = cursor.fetchone()[0]
                cursor.execute(
                    "DELETE FROM charging_station_connectors WHERE charging_station_id = %s",
                    (actual_station_id,),
                )
                seen_connector_ids: set[int] = set()
                for connector in station.connections:
                    if connector.external_id in seen_connector_ids:
                        continue
                    seen_connector_ids.add(connector.external_id)
                    connector_id = uuid.uuid5(
                        _CONNECTOR_NAMESPACE,
                        f"{actual_station_id}:{connector.external_id}",
                    )
                    cursor.execute(
                        """
                        INSERT INTO charging_station_connectors
                            (id, charging_station_id, external_id, connector_type,
                             charging_level, current_type, operational_status, power_kw,
                             amps, voltage, quantity, created_at, updated_at)
                        VALUES (%s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s)
                        """,
                        (
                            connector_id,
                            actual_station_id,
                            connector.external_id,
                            _trim(connector.connection_type.title if connector.connection_type else None, 160),
                            _trim(connector.level.title if connector.level else None, 120),
                            _trim(connector.current_type.title if connector.current_type else None, 120),
                            _trim(connector.status_type.title if connector.status_type else None, 160),
                            connector.power_kw,
                            connector.amps,
                            connector.voltage,
                            connector.quantity,
                            imported_at,
                            imported_at,
                        ),
                    )
                    connector_count += 1
            deactivated = 0
            if complete:
                cursor.execute(
                    """
                    UPDATE charging_stations
                    SET active = FALSE, updated_at = %s
                    WHERE external_source = 'OpenChargeMap'
                      AND active = TRUE
                      AND NOT (external_id = ANY(%s))
                    """,
                    (imported_at, external_ids),
                )
                deactivated = cursor.rowcount
        return ChargingPoiSyncOutcome(len(stations), connector_count, deactivated)


def confidence_for_quality(level: int | None) -> str:
    if level is None:
        return "Unknown"
    if level <= 2:
        return "Low"
    if level == 3:
        return "Medium"
    return "High"


def _is_vietnam_location(station: ChargingPoi) -> bool:
    country = station.country_code.upper()
    return (
        country == "VN"
        and 7.5 <= station.address.latitude <= 24.0
        and 101.5 <= station.address.longitude <= 110.5
    )


def _utc(value: datetime | None) -> datetime | None:
    if value is None:
        return None
    return value.replace(tzinfo=UTC) if value.tzinfo is None else value.astimezone(UTC)


def _trim(value: str | None, maximum: int) -> str | None:
    normalized = " ".join(value.split()) if value else ""
    return normalized[:maximum] or None


def _safe_related_url(value: str | None) -> str | None:
    if not value:
        return None
    try:
        parsed = httpx.URL(value)
    except Exception:
        return None
    if parsed.scheme != "https" or not parsed.host or parsed.username or parsed.password:
        return None
    return str(parsed)[:2048]
