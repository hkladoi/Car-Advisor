from __future__ import annotations

import json
import uuid
from datetime import UTC, datetime
from typing import Any

import psycopg


def enqueue_catalog_search_sync(
    connection: psycopg.Connection[Any],
    publication_type: str,
    aggregate_type: str,
    *,
    aggregate_id: uuid.UUID | None = None,
    correlation_id: str | None = None,
    payload: dict[str, Any] | None = None,
    occurred_at: datetime | None = None,
) -> uuid.UUID:
    """Write a search-projection event inside the caller's publication transaction."""
    event_id = uuid.uuid4()
    now = occurred_at or datetime.now(UTC)
    with connection.cursor() as cursor:
        cursor.execute(
            """
            INSERT INTO published_data_events
                (id,event_type,aggregate_type,aggregate_id,payload_json,status,attempts,
                 occurred_at,available_at,processing_started_at,processed_at,last_error,
                 correlation_id,created_at,updated_at)
            VALUES (%s,%s,%s,%s,%s::jsonb,'Pending',0,%s,%s,NULL,NULL,NULL,%s,%s,%s)
            """,
            (
                event_id,
                f"CatalogSearchSync.{publication_type}",
                aggregate_type,
                aggregate_id,
                json.dumps(payload or {}, ensure_ascii=False, sort_keys=True),
                now,
                now,
                correlation_id,
                now,
                now,
            ),
        )
    return event_id
