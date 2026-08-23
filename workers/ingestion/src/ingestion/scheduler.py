from __future__ import annotations

import asyncio
from pathlib import Path

import redis.asyncio as redis
import structlog

from ingestion.jobs import IngestionJob
from ingestion.logging import configure_logging
from ingestion.monitoring import job_for_schedule, schedule_definitions
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
                if not source.automated_fetch and source.category != "brand-registry":
                    continue
                for schedule in schedule_definitions(source):
                    if (
                        schedule.monitor_kind == "charging_poi_locations"
                        and not settings.open_charge_map_api_key.get_secret_value().strip()
                    ):
                        continue
                    lease_key = f"ingestion:next-fetch:{schedule.monitor_kind}:{source.id}"
                    acquired = await client.set(
                        lease_key,
                        "scheduled",
                        ex=schedule.cadence_hours * 3600,
                        nx=True,
                    )
                    if not acquired:
                        continue
                    job = job_for_schedule(source, schedule)
                    await client.rpush(settings.ingestion_queue, job.model_dump_json())
                    enqueued += 1
            queue_depth = await client.llen(settings.ingestion_queue)
            logger.info(
                "source_schedule_checked",
                enqueued=enqueued,
                queue=settings.ingestion_queue,
                queue_depth=queue_depth,
            )
            await asyncio.sleep(settings.ingestion_schedule_seconds)
    finally:
        await client.aclose()


def main() -> None:
    configure_logging()
    asyncio.run(run_scheduler(Settings()))


if __name__ == "__main__":
    main()
