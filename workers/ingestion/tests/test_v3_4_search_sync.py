import uuid
from datetime import UTC, datetime

from ingestion.search_sync import enqueue_catalog_search_sync


def test_publisher_enqueues_pending_search_event_in_caller_connection() -> None:
    calls: list[tuple[str, tuple[object, ...]]] = []

    class Cursor:
        def __enter__(self) -> "Cursor":
            return self

        def __exit__(self, *_: object) -> None:
            return None

        def execute(self, query: str, params: tuple[object, ...]) -> None:
            calls.append((query, params))

    class Connection:
        def cursor(self) -> Cursor:
            return Cursor()

    occurred_at = datetime(2026, 8, 24, 3, 0, tzinfo=UTC)
    event_id = enqueue_catalog_search_sync(
        Connection(),  # type: ignore[arg-type]
        "CatalogSeedPublished",
        "ManualImportBatch",
        correlation_id="seed:test",
        payload={"records": 3},
        occurred_at=occurred_at,
    )

    assert isinstance(event_id, uuid.UUID)
    assert len(calls) == 1
    query, params = calls[0]
    assert "INSERT INTO published_data_events" in query
    assert "'Pending'" in query
    assert params[1] == "CatalogSearchSync.CatalogSeedPublished"
    assert params[2] == "ManualImportBatch"
    assert params[5] == occurred_at
    assert params[7] == "seed:test"
