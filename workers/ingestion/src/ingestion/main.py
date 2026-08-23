from __future__ import annotations

import asyncio
import json
import signal
from dataclasses import asdict
from pathlib import Path

import redis.asyncio as redis
import structlog

from ingestion.jobs import IngestionJob
from ingestion.change_detection import CandidateChangeRepository
from ingestion.discovery import (
    BraveSearchClient,
    BraveSearchOptions,
    DiscoveryRequest,
    DiscoveryService,
    QueryTemplateCatalog,
)
from ingestion.fetcher import KnownUrlFetcher
from ingestion.extraction import (
    CandidateFactRepository,
    CatalogEntityRepository,
    LocalLlmJsonSchemaExtractor,
    LocalLlmOptions,
    StructuredExtractionEngine,
    StructuredExtractionPipeline,
)
from ingestion.logging import configure_logging
from ingestion.metadata import SnapshotMetadataRepository
from ingestion.parsers import DomainParserRegistry, ParserCoordinator, ParserProfileRegistry
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
    parser_coordinator = ParserCoordinator(
        DomainParserRegistry(
            ParserProfileRegistry.load(Path(settings.parser_registry_path)),
            settings.parser_max_pdf_pages,
        ),
        settings.parser_max_content_bytes,
    )
    llm = None
    if settings.local_llm_base_url.strip() and settings.local_llm_model.strip():
        llm = LocalLlmJsonSchemaExtractor(
            LocalLlmOptions(
                base_url=settings.local_llm_base_url,
                model=settings.local_llm_model,
                api_key=settings.local_llm_api_key.get_secret_value(),
                timeout_seconds=settings.local_llm_timeout_seconds,
                max_input_chars=settings.local_llm_max_input_chars,
            )
        )
    extraction_pipeline = StructuredExtractionPipeline(
        storage=storage,
        catalog_repository=CatalogEntityRepository(settings.postgres_dsn),
        fact_repository=CandidateFactRepository(settings.postgres_dsn),
        engine=StructuredExtractionEngine(llm),
    )
    change_repository = CandidateChangeRepository(settings.postgres_dsn)
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
                try:
                    parse_outcome = await asyncio.to_thread(
                        parser_coordinator.parse, source, snapshot, storage
                    )
                    await asyncio.to_thread(
                        metadata_repository.mark_parsed,
                        snapshot_id,
                        parse_outcome.parser_version,
                    )
                    if parse_outcome.status == "parsed":
                        await client.rpush(
                            settings.parsed_document_queue,
                            json.dumps(
                                {
                                    "source_id": source.id,
                                    "snapshot_id": str(snapshot_id),
                                    "content_hash": snapshot.content_hash,
                                    "parser_version": parse_outcome.parser_version,
                                    "parsed_object_key": parse_outcome.parsed_object_key,
                                },
                                ensure_ascii=False,
                            ),
                        )
                        await client.ltrim(settings.parsed_document_queue, -10_000, -1)
                    logger.info(
                        "source_parse_complete",
                        source_id=source.id,
                        parser_version=parse_outcome.parser_version,
                        parse_status=parse_outcome.status,
                        parsed_object_key=parse_outcome.parsed_object_key,
                    )
                    try:
                        extraction_outcome = await extraction_pipeline.process(
                            source,
                            snapshot_id,
                            parse_outcome.parsed_object_key,
                            snapshot.content_hash,
                        )
                        batch = extraction_outcome.batch
                        changes = (
                            await asyncio.to_thread(
                                change_repository.detect_and_apply, batch
                            )
                            if batch else None
                        )
                        if changes and changes.auto_published:
                            from ingestion.cache import invalidate_catalog_cache

                            await asyncio.to_thread(
                                invalidate_catalog_cache, settings.redis_url
                            )
                        if extraction_outcome.status == "extracted" or (changes and changes.detected):
                            await client.rpush(
                                settings.extracted_candidate_queue,
                                json.dumps(
                                    {
                                        "source_id": source.id,
                                        "snapshot_id": str(snapshot_id),
                                        "content_hash": snapshot.content_hash,
                                        "artifact_key": extraction_outcome.artifact_key,
                                        "fact_count": len(batch.facts) if batch else 0,
                                        "inserted_facts": extraction_outcome.inserted_facts,
                                        "entity_resolution": (
                                            batch.entity_resolution.model_dump(mode="json")
                                            if batch else None
                                        ),
                                        "changes": changes.model_dump() if changes else None,
                                    },
                                    ensure_ascii=False,
                                ),
                            )
                            await client.ltrim(settings.extracted_candidate_queue, -10_000, -1)
                        if changes:
                            logger.info(
                                "candidate_change_detection_complete",
                                source_id=source.id,
                                detected=changes.detected,
                                auto_published=changes.auto_published,
                                queued_for_review=changes.queued_for_review,
                                unchanged=changes.unchanged,
                            )
                        logger.info(
                            "source_extraction_complete",
                            source_id=source.id,
                            extraction_status=extraction_outcome.status,
                            artifact_key=extraction_outcome.artifact_key,
                            inserted_facts=extraction_outcome.inserted_facts,
                            fact_count=(
                                len(extraction_outcome.batch.facts)
                                if extraction_outcome.batch else 0
                            ),
                            resolution_status=(
                                extraction_outcome.batch.entity_resolution.status
                                if extraction_outcome.batch else None
                            ),
                        )
                    except Exception as error:
                        logger.exception(
                            "source_extraction_failed",
                            source_id=source.id,
                            content_hash=snapshot.content_hash,
                            error_type=type(error).__name__,
                        )
                except Exception as error:
                    logger.exception(
                        "source_parse_failed",
                        source_id=source.id,
                        content_hash=snapshot.content_hash,
                        error_type=type(error).__name__,
                    )
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
