from __future__ import annotations

import uuid
from datetime import UTC, datetime, timedelta
from typing import Any

import psycopg

from ingestion.fetcher import Snapshot
from ingestion.registry import RegistrySource


_NAMESPACE = uuid.UUID("f5a25127-52e6-4f59-a72c-d568ff5dca6e")


class SnapshotMetadataRepository:
    def __init__(self, dsn: str) -> None:
        self._dsn = dsn

    def record(self, source: RegistrySource, snapshot: Snapshot) -> uuid.UUID:
        source_id = _stable_id("source", source.url)
        proposed_snapshot_id = _stable_id("snapshot", str(source_id), snapshot.content_hash)
        with psycopg.connect(self._dsn) as connection, connection.transaction(), connection.cursor() as cursor:
            cursor.execute(
                """
                SELECT ss.content_hash
                FROM sources s
                JOIN source_snapshots ss ON ss.source_id = s.id
                WHERE s.url = %s
                ORDER BY ss.fetched_at DESC
                LIMIT 1
                """,
                (source.url,),
            )
            previous = cursor.fetchone()
            previous_hash: str | None = previous[0] if previous else None
            cursor.execute(
                """
                INSERT INTO sources
                    (id, name, url, domain, authority_level, content_type, robots_note,
                     terms_note, active, priority, refresh_interval, last_fetched_at,
                     created_at, updated_at)
                VALUES (%s, %s, %s, %s, %s, %s, %s, %s, TRUE, %s, %s, %s, %s, %s)
                ON CONFLICT (url) DO UPDATE SET
                    name = EXCLUDED.name,
                    domain = EXCLUDED.domain,
                    authority_level = EXCLUDED.authority_level,
                    content_type = EXCLUDED.content_type,
                    robots_note = EXCLUDED.robots_note,
                    terms_note = EXCLUDED.terms_note,
                    priority = EXCLUDED.priority,
                    refresh_interval = EXCLUDED.refresh_interval,
                    last_fetched_at = EXCLUDED.last_fetched_at,
                    updated_at = EXCLUDED.updated_at
                RETURNING id
                """,
                (
                    source_id,
                    source.name,
                    source.url,
                    source.allowed_domains[0],
                    source.authority.value,
                    source.content_type.value,
                    source.robots_note,
                    source.terms_note,
                    source.priority,
                    timedelta(hours=source.refresh_hours),
                    snapshot.fetched_at,
                    snapshot.fetched_at,
                    snapshot.fetched_at,
                ),
            )
            actual_source_id: uuid.UUID = cursor.fetchone()[0]
            cursor.execute(
                """
                INSERT INTO source_snapshots
                    (id, source_id, fetched_at, content_hash, object_key, http_status,
                     parser_version, etag, last_modified_at, fetch_error, created_at, updated_at)
                VALUES (%s, %s, %s, %s, %s, %s, %s, %s, NULL, NULL, %s, %s)
                ON CONFLICT (source_id, content_hash) DO NOTHING
                """,
                (
                    proposed_snapshot_id,
                    actual_source_id,
                    snapshot.fetched_at,
                    snapshot.content_hash,
                    snapshot.object_key,
                    snapshot.http_status,
                    f"known-url/v1.2:{snapshot.fetch_method}",
                    snapshot.etag,
                    snapshot.fetched_at,
                    snapshot.fetched_at,
                ),
            )
            cursor.execute(
                "SELECT id FROM source_snapshots WHERE source_id = %s AND content_hash = %s",
                (actual_source_id, snapshot.content_hash),
            )
            snapshot_id: uuid.UUID = cursor.fetchone()[0]
            if previous_hash is not None and previous_hash != snapshot.content_hash:
                change_id = _stable_id(
                    "source-content-change",
                    str(actual_source_id),
                    previous_hash,
                    snapshot.content_hash,
                )
                energy = is_energy_category(source.category)
                cursor.execute(
                    """
                    INSERT INTO data_changes
                        (id, entity_type, entity_id, field_path, old_value, new_value,
                         risk_level, status, detected_at, source_fact_id,
                         reviewed_audit_event_id, created_at, updated_at)
                    VALUES (%s, 'Source', %s, %s, %s, %s, %s, %s, %s, NULL, NULL, %s, %s)
                    ON CONFLICT (id) DO NOTHING
                    """,
                    (
                        change_id,
                        actual_source_id,
                        "energy_source_content_hash" if energy else "source_content_hash",
                        previous_hash,
                        snapshot.content_hash,
                        "High" if energy else "Low",
                        "PendingReview" if energy else "Detected",
                        snapshot.fetched_at,
                        snapshot.fetched_at,
                        snapshot.fetched_at,
                    ),
                )
            return snapshot_id

    def find_stale_sources(self, sources: list[RegistrySource]) -> list[str]:
        if not sources:
            return []
        by_url = {source.url: source.id for source in sources}
        with psycopg.connect(self._dsn) as connection, connection.cursor() as cursor:
            cursor.execute(
                """
                SELECT url
                FROM sources
                WHERE url = ANY(%s)
                  AND (last_fetched_at IS NULL OR last_fetched_at + refresh_interval < %s)
                ORDER BY url
                """,
                (list(by_url), datetime.now(UTC)),
            )
            stale = {row[0] for row in cursor.fetchall()}
        missing = set(by_url) - self._registered_urls(by_url)
        return sorted(by_url[url] for url in stale | missing)

    def mark_parsed(self, snapshot_id: uuid.UUID, parser_version: str) -> None:
        with psycopg.connect(self._dsn) as connection, connection.cursor() as cursor:
            cursor.execute(
                "UPDATE source_snapshots SET parser_version = %s, updated_at = %s WHERE id = %s",
                (parser_version, datetime.now(UTC), snapshot_id),
            )
            if cursor.rowcount != 1:
                raise KeyError(f"Unknown source snapshot: {snapshot_id}")
            connection.commit()

    def _registered_urls(self, by_url: dict[str, str]) -> set[str]:
        with psycopg.connect(self._dsn) as connection, connection.cursor() as cursor:
            cursor.execute("SELECT url FROM sources WHERE url = ANY(%s)", (list(by_url),))
            return {row[0] for row in cursor.fetchall()}


def _stable_id(*parts: str) -> uuid.UUID:
    return uuid.uuid5(_NAMESPACE, "|".join(parts))


def is_energy_category(category: str) -> bool:
    return category in {"fuel-price", "electricity-price", "charging-price", "charging-promotion"}
