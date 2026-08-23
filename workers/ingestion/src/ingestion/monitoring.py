from __future__ import annotations

import hashlib
import uuid
from dataclasses import dataclass
from datetime import UTC, datetime

import psycopg

from ingestion.jobs import IngestionJob
from ingestion.registry import RegistrySource


@dataclass(frozen=True, slots=True)
class ScheduleDefinition:
    monitor_kind: str
    cadence_hours: int


_DAILY = 24
_WEEKLY = 168


def schedule_definitions(source: RegistrySource) -> tuple[ScheduleDefinition, ...]:
    category = source.category
    if category == "vehicle":
        definitions = (
            ScheduleDefinition("vehicle_price_promotion", _DAILY),
            ScheduleDefinition("vehicle_specs_features", _WEEKLY),
            ScheduleDefinition("vehicle_images_colors", _WEEKLY),
        )
    elif category == "brand-registry":
        definitions = (ScheduleDefinition("new_model_discovery", _DAILY),)
    elif category == "dealer-offer":
        definitions = (ScheduleDefinition("dealer_offers", _DAILY),)
    elif category == "finance-campaign":
        definitions = (ScheduleDefinition("finance_campaign_reference", _DAILY),)
    elif category == "fuel-price":
        definitions = (ScheduleDefinition("fuel_price", _DAILY),)
    elif category == "electricity-price":
        definitions = (ScheduleDefinition("electricity_tariff", _DAILY),)
    elif category in {"charging-price", "charging-promotion"}:
        definitions = (ScheduleDefinition("charging_tariff_promotion", _DAILY),)
    elif category == "charging-poi":
        definitions = (ScheduleDefinition("charging_poi_locations", _WEEKLY),)
    elif category == "registration-rule":
        definitions = (ScheduleDefinition("registration_legal_rules", _DAILY),)
    elif category in {"vehicle-energy", "administrative-region"}:
        definitions = (ScheduleDefinition("vehicle_specs_features", _WEEKLY),)
    else:
        definitions = (ScheduleDefinition("source_refresh", source.refresh_hours),)
    return tuple(
        ScheduleDefinition(value.monitor_kind, min(value.cadence_hours, source.refresh_hours))
        for value in definitions
    )


def job_for_schedule(source: RegistrySource, schedule: ScheduleDefinition) -> IngestionJob:
    if schedule.monitor_kind == "charging_poi_locations":
        return IngestionJob.charging_poi(source.id)
    if schedule.monitor_kind == "new_model_discovery":
        return IngestionJob.discovery(
            brand=source.owner,
            data_type="vehicle",
            allowed_domains=source.allowed_domains,
            known_urls=[source.url],
            source_id=source.id,
        )
    return IngestionJob.known_url(source.id, schedule.monitor_kind)


class MonitoringRepository:
    _ALERT_NAMESPACE = uuid.UUID("df3fb0ef-f447-4294-ab10-122089d72d69")

    def __init__(self, dsn: str, parser_failure_threshold: int = 3) -> None:
        self._dsn = dsn
        self._parser_failure_threshold = parser_failure_threshold

    def begin(self, job: IngestionJob, source: RegistrySource | None = None) -> None:
        now = datetime.now(UTC)
        with psycopg.connect(self._dsn) as connection, connection.cursor() as cursor:
            source_id = self._source_id(cursor, source.url) if source else None
            cursor.execute(
                """
                INSERT INTO ingestion_job_runs
                    (id, job_type, monitor_kind, source_key, source_id, requested_at,
                     started_at, status, created_at, updated_at)
                VALUES (%s, %s, %s, %s, %s, %s, %s, 'Running', %s, %s)
                ON CONFLICT (id) DO NOTHING
                """,
                (
                    job.run_id, job.job_type, job.monitor_kind, job.source_id, source_id,
                    job.requested_at, now, now, now,
                ),
            )
            connection.commit()

    def succeed(
        self,
        job: IngestionJob,
        *,
        http_status: int | None = None,
        parse_status: str | None = None,
        content_changed: bool | None = None,
    ) -> None:
        self._finish(
            job,
            "Succeeded",
            http_status=http_status,
            parse_status=parse_status,
            content_changed=content_changed,
        )
        if job.source_id and parse_status:
            self._resolve_alert("PARSER_CONSECUTIVE_FAILURE", job.source_id)

    def partial(
        self,
        job: IngestionJob,
        stage: str,
        error: Exception,
        *,
        http_status: int | None = None,
        parse_status: str | None = None,
        content_changed: bool | None = None,
    ) -> None:
        self._finish(
            job,
            "Partial",
            http_status=http_status,
            parse_status=parse_status,
            content_changed=content_changed,
            stage=stage,
            error=error,
        )
        if job.source_id and parse_status:
            self._resolve_alert("PARSER_CONSECUTIVE_FAILURE", job.source_id)

    def fail(
        self,
        job: IngestionJob,
        stage: str,
        error: Exception,
        *,
        http_status: int | None = None,
    ) -> None:
        self._finish(job, "Failed", http_status=http_status, stage=stage, error=error)
        if stage == "parser" and job.source_id and self._has_consecutive_parser_failures(job):
            self._open_alert(
                "PARSER_CONSECUTIVE_FAILURE",
                "High",
                job.source_id,
                job.run_id,
                f"Parser failed {self._parser_failure_threshold} consecutive runs for {job.source_id}; published data was retained.",
            )

    def reconcile_stale_sources(
        self,
        all_sources: list[RegistrySource],
        stale_source_keys: list[str],
    ) -> None:
        stale = set(stale_source_keys)
        for source in all_sources:
            if source.id in stale:
                severity = "High" if source.authority.value in {
                    "CompetentAuthority", "BrandOfficial", "DistributorOfficial"
                } else "Medium"
                self._open_alert(
                    "SOURCE_STALE",
                    severity,
                    source.id,
                    None,
                    f"Source {source.id} exceeded its {source.refresh_hours}-hour freshness policy.",
                )
            else:
                self._resolve_alert("SOURCE_STALE", source.id)

    def _finish(
        self,
        job: IngestionJob,
        status: str,
        *,
        http_status: int | None = None,
        parse_status: str | None = None,
        content_changed: bool | None = None,
        stage: str | None = None,
        error: Exception | None = None,
    ) -> None:
        now = datetime.now(UTC)
        error_code = type(error).__name__ if error else None
        error_message = _safe_error(error) if error else None
        with psycopg.connect(self._dsn) as connection, connection.cursor() as cursor:
            cursor.execute(
                """
                UPDATE ingestion_job_runs
                SET status = %s, completed_at = %s,
                    duration_milliseconds = GREATEST(0, (EXTRACT(EPOCH FROM (%s - started_at)) * 1000)::integer),
                    http_status = %s, parse_status = %s, content_changed = %s,
                    error_stage = %s, error_code = %s, error_message = %s, updated_at = %s
                WHERE id = %s AND status = 'Running'
                """,
                (
                    status, now, now, http_status, parse_status, content_changed,
                    stage, error_code, error_message, now, job.run_id,
                ),
            )
            connection.commit()

    def _has_consecutive_parser_failures(self, job: IngestionJob) -> bool:
        with psycopg.connect(self._dsn) as connection, connection.cursor() as cursor:
            cursor.execute(
                """
                SELECT COUNT(*) = %s AND BOOL_AND(status = 'Failed' AND error_stage = 'parser')
                FROM (
                    SELECT status, error_stage
                    FROM ingestion_job_runs
                    WHERE source_key = %s AND monitor_kind = %s
                    ORDER BY started_at DESC
                    LIMIT %s
                ) recent
                """,
                (
                    self._parser_failure_threshold,
                    job.source_id,
                    job.monitor_kind,
                    self._parser_failure_threshold,
                ),
            )
            return bool(cursor.fetchone()[0])

    def _open_alert(
        self,
        alert_type: str,
        severity: str,
        source_key: str,
        job_run_id: uuid.UUID | None,
        message: str,
    ) -> None:
        fingerprint = f"{alert_type}:{source_key}"
        alert_id = uuid.uuid5(self._ALERT_NAMESPACE, fingerprint)
        now = datetime.now(UTC)
        with psycopg.connect(self._dsn) as connection, connection.cursor() as cursor:
            source_id = self._source_id_by_key(cursor, source_key)
            cursor.execute(
                """
                INSERT INTO monitoring_alerts
                    (id, fingerprint, alert_type, severity, status, source_key, source_id,
                     job_run_id, message, occurrence_count, first_triggered_at,
                     last_triggered_at, created_at, updated_at)
                VALUES (%s, %s, %s, %s, 'Open', %s, %s, %s, %s, 1, %s, %s, %s, %s)
                ON CONFLICT (fingerprint) DO UPDATE SET
                    severity = EXCLUDED.severity,
                    status = 'Open',
                    source_id = COALESCE(EXCLUDED.source_id, monitoring_alerts.source_id),
                    job_run_id = EXCLUDED.job_run_id,
                    message = EXCLUDED.message,
                    occurrence_count = monitoring_alerts.occurrence_count + 1,
                    last_triggered_at = EXCLUDED.last_triggered_at,
                    acknowledged_at = NULL,
                    acknowledged_by = NULL,
                    resolved_at = NULL,
                    updated_at = EXCLUDED.updated_at
                """,
                (
                    alert_id, fingerprint, alert_type, severity, source_key, source_id,
                    job_run_id, message, now, now, now, now,
                ),
            )
            connection.commit()

    def _resolve_alert(self, alert_type: str, source_key: str) -> None:
        now = datetime.now(UTC)
        with psycopg.connect(self._dsn) as connection, connection.cursor() as cursor:
            cursor.execute(
                """
                UPDATE monitoring_alerts
                SET status = 'Resolved', resolved_at = %s, updated_at = %s
                WHERE fingerprint = %s AND status <> 'Resolved'
                """,
                (now, now, f"{alert_type}:{source_key}"),
            )
            connection.commit()

    @staticmethod
    def _source_id(cursor: psycopg.Cursor, url: str) -> uuid.UUID | None:
        cursor.execute("SELECT id FROM sources WHERE url = %s", (url,))
        row = cursor.fetchone()
        return row[0] if row else None

    @staticmethod
    def _source_id_by_key(cursor: psycopg.Cursor, source_key: str) -> uuid.UUID | None:
        cursor.execute(
            """
            SELECT source_id
            FROM ingestion_job_runs
            WHERE source_key = %s AND source_id IS NOT NULL
            ORDER BY started_at DESC
            LIMIT 1
            """,
            (source_key,),
        )
        row = cursor.fetchone()
        if row:
            return row[0]
        cursor.execute(
            "SELECT id FROM sources WHERE url LIKE %s OR name ILIKE %s ORDER BY priority LIMIT 1",
            (f"%{source_key}%", f"%{source_key.replace('-', ' ')}%"),
        )
        fallback = cursor.fetchone()
        return fallback[0] if fallback else None


def _safe_error(error: Exception) -> str:
    value = " ".join(str(error).split())
    digest = hashlib.sha256(value.encode("utf-8")).hexdigest()[:12]
    return f"{type(error).__name__} [{digest}]: {value[:1800]}"
