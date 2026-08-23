from __future__ import annotations

import asyncio
from pathlib import Path

import redis.asyncio as redis
import structlog

from ingestion.jobs import IngestionJob
from ingestion.logging import configure_logging
from ingestion.registry import SourceRegistry
from ingestion.settings import Settings


async def run_scheduler(settings: Settings) -> None:
    logger = structlog.get_logger("ingestion.scheduler")
    client = redis.from_url(settings.redis_url, decode_responses=True)
    registry = SourceRegistry.load(Path(settings.source_registry_path))
    await client.ping()
    logger.info("scheduler_ready", cadence_seconds=settings.ingestion_schedule_seconds)
    try:
        while True:
            enqueued = 0
            stale_lease = await client.set(
                "ingestion:next-staleness-check",
                "scheduled",
                ex=24 * 3600,
                nx=True,
            )
            if stale_lease:
                await client.rpush(settings.ingestion_queue, IngestionJob.staleness_check().model_dump_json())
                enqueued += 1
            for source in registry.sources:
                if not source.automated_fetch:
                    continue
                lease_key = f"ingestion:next-fetch:{source.id}"
                acquired = await client.set(
                    lease_key,
                    "scheduled",
                    ex=source.refresh_hours * 3600,
                    nx=True,
                )
                if not acquired:
                    continue
                job = IngestionJob.known_url(source.id)
                await client.rpush(settings.ingestion_queue, job.model_dump_json())
                enqueued += 1
            logger.info("source_schedule_checked", enqueued=enqueued, queue=settings.ingestion_queue)
            await asyncio.sleep(settings.ingestion_schedule_seconds)
    finally:
        await client.aclose()


def main() -> None:
    configure_logging()
    asyncio.run(run_scheduler(Settings()))


if __name__ == "__main__":
    main()
