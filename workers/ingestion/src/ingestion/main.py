from __future__ import annotations

import asyncio
import json
import signal
from dataclasses import asdict
from pathlib import Path

import redis.asyncio as redis
import structlog

from ingestion.jobs import IngestionJob
from ingestion.discovery import (
    BraveSearchClient,
    BraveSearchOptions,
    DiscoveryRequest,
    DiscoveryService,
    QueryTemplateCatalog,
)
from ingestion.fetcher import KnownUrlFetcher
from ingestion.logging import configure_logging
from ingestion.metadata import SnapshotMetadataRepository
from ingestion.registry import SourceRegistry
from ingestion.settings import Settings
from ingestion.storage import S3CompatibleObjectStorage


async def run_worker(settings: Settings) -> None:
    logger = structlog.get_logger("ingestion.worker")
    client = redis.from_url(settings.redis_url, decode_responses=True)
    registry = SourceRegistry.load(Path(settings.source_registry_path))
    storage = S3CompatibleObjectStorage(settings)
    fetcher = KnownUrlFetcher(settings.ingestion_user_agent)
    metadata_repository = SnapshotMetadataRepository(settings.postgres_dsn)
    discovery = DiscoveryService(
        BraveSearchClient(
            client,
            BraveSearchOptions(
                api_key=settings.brave_search_api_key.get_secret_value(),
                monthly_request_budget=settings.brave_monthly_request_budget,
                endpoint=settings.brave_search_endpoint,
                timeout_seconds=settings.brave_search_timeout_seconds,
                cache_seconds=settings.brave_discovery_cache_seconds,
            ),
        ),
        QueryTemplateCatalog.load(Path(settings.discovery_query_templates_path)),
        settings.brave_discovery_max_queries,
    )
    stop = asyncio.Event()
    loop = asyncio.get_running_loop()
    for event in (signal.SIGINT, signal.SIGTERM):
        try:
            loop.add_signal_handler(event, stop.set)
        except NotImplementedError:
            signal.signal(event, lambda *_: loop.call_soon_threadsafe(stop.set))

    await client.ping()
    logger.info("worker_ready", queue=settings.ingestion_queue)

    try:
        while not stop.is_set():
            item = await client.blpop(settings.ingestion_queue, timeout=5)
            if item is None:
                continue
            _, raw_job = item
            job = IngestionJob.model_validate_json(raw_job)
            logger.info("job_received", job_type=job.job_type, source_id=job.source_id)
            if job.job_type == "source_staleness_check":
                energy_sources = [source for source in registry.sources if source.category in {
                    "fuel-price", "electricity-price", "charging-price", "charging-promotion"
                }]
                stale = await asyncio.to_thread(metadata_repository.find_stale_sources, energy_sources)
                logger.info(
                    "source_staleness_check_complete",
                    energy_sources=len(energy_sources),
                    stale_source_ids=stale,
                    stale_count=len(stale),
                )
                continue
            if job.job_type == "source_discovery":
                try:
                    batch = await discovery.discover(
                        DiscoveryRequest(
                            brand=job.brand or "",
                            data_type=job.data_type or "",
                            allowed_domains=job.allowed_domains,
                            known_urls=job.known_urls,
                            force_discovery=job.force_discovery,
                        )
                    )
                    await client.rpush(
                        settings.discovery_candidate_queue,
                        batch.model_dump_json(),
                    )
                    await client.ltrim(settings.discovery_candidate_queue, -10_000, -1)
                    logger.info(
                        "source_discovery_complete",
                        brand=batch.brand,
                        data_type=batch.data_type,
                        strategy=batch.strategy,
                        candidates=len(batch.candidates),
                        cache_hits=batch.cache_hits,
                        charged_requests=batch.charged_requests,
                    )
                except Exception as error:
                    logger.exception(
                        "source_discovery_failed",
                        brand=job.brand,
                        data_type=job.data_type,
                        error_type=type(error).__name__,
                    )
                continue
            try:
                source = registry.by_id(job.source_id or "")
                snapshot = await fetcher.fetch(source, storage)
                snapshot_id = await asyncio.to_thread(metadata_repository.record, source, snapshot)
                await client.rpush(
                    settings.snapshot_event_queue,
                    json.dumps(asdict(snapshot), default=str, ensure_ascii=False),
                )
                await client.ltrim(settings.snapshot_event_queue, -10_000, -1)
                logger.info(
                    "source_snapshot_saved",
                    source_id=source.id,
                    content_hash=snapshot.content_hash,
                    object_key=snapshot.object_key,
                    size_bytes=snapshot.size_bytes,
                    snapshot_id=str(snapshot_id),
                )
            except Exception as error:
                logger.exception(
                    "source_fetch_failed",
                    source_id=job.source_id,
                    error_type=type(error).__name__,
                )
    finally:
        await client.aclose()
        logger.info("worker_stopped")


def main() -> None:
    configure_logging()
    asyncio.run(run_worker(Settings()))


if __name__ == "__main__":
    main()
