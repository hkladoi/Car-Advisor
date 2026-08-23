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
from ingestion.fetcher import Snapshot
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
from ingestion.monitoring import MonitoringRepository
from ingestion.open_charge_map import ChargingPoiRepository, OpenChargeMapClient
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
    monitoring_repository = MonitoringRepository(
        settings.postgres_dsn,
        settings.parser_failure_alert_threshold,
    )
    open_charge_map = OpenChargeMapClient(
        settings.open_charge_map_api_key.get_secret_value(),
        settings.ingestion_user_agent,
        timeout_seconds=settings.open_charge_map_timeout_seconds,
        retries=settings.open_charge_map_retries,
        page_size=settings.open_charge_map_page_size,
        max_stations=settings.open_charge_map_max_stations,
        max_response_bytes=settings.open_charge_map_max_response_bytes,
    )
    charging_poi_repository = ChargingPoiRepository(settings.postgres_dsn)
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
            source = registry.by_id(job.source_id) if job.source_id else None
            await asyncio.to_thread(monitoring_repository.begin, job, source)
            logger.info(
                "job_received",
                run_id=str(job.run_id),
                job_type=job.job_type,
                monitor_kind=job.monitor_kind,
                source_id=job.source_id,
            )
            if job.job_type == "charging_poi_sync":
                try:
                    result = await open_charge_map.fetch_vietnam()
                    object_key = (
                        f"sources/open-charge-map/sha256/{result.content_hash}.json"
                    )
                    existed = await asyncio.to_thread(storage.exists, object_key)
                    if not existed:
                        await asyncio.to_thread(storage.ensure_bucket)
                        await asyncio.to_thread(
                            storage.put_bytes,
                            object_key,
                            result.snapshot_bytes,
                            "application/json",
                        )
                    snapshot = Snapshot(
                        source_id=source.id,
                        source_url=source.url,
                        final_url=source.url,
                        fetched_at=result.fetched_at,
                        content_hash=result.content_hash,
                        object_key=object_key,
                        http_status=result.http_status,
                        content_type="application/json",
                        etag=None,
                        last_modified=None,
                        size_bytes=len(result.snapshot_bytes),
                        fetch_method="open-charge-map-api-v3",
                    )
                    snapshot_id = await asyncio.to_thread(
                        metadata_repository.record, source, snapshot
                    )
                    await asyncio.to_thread(
                        metadata_repository.mark_parsed,
                        snapshot_id,
                        "open-charge-map/v3",
                    )
                    outcome = await asyncio.to_thread(
                        charging_poi_repository.synchronize,
                        result.stations,
                        snapshot_id,
                        result.fetched_at,
                        complete=result.complete,
                    )
                    await asyncio.to_thread(
                        monitoring_repository.succeed,
                        job,
                        http_status=result.http_status,
                        parse_status="normalized",
                        content_changed=not existed,
                    )
                    await client.rpush(
                        settings.snapshot_event_queue,
                        json.dumps(asdict(snapshot), default=str, ensure_ascii=False),
                    )
                    await client.ltrim(settings.snapshot_event_queue, -10_000, -1)
                    logger.info(
                        "charging_poi_sync_complete",
                        source_id=source.id,
                        stations=outcome.imported_stations,
                        connectors=outcome.imported_connectors,
                        rejected_records=result.rejected_records,
                        deactivated=outcome.deactivated_stations,
                        complete=result.complete,
                        page_count=result.page_count,
                        content_hash=result.content_hash,
                    )
                except Exception as error:
                    await asyncio.to_thread(
                        monitoring_repository.fail, job, "charging_poi", error
                    )
                    logger.exception(
                        "charging_poi_sync_failed",
                        source_id=job.source_id,
                        error_type=type(error).__name__,
                    )
                continue
            if job.job_type == "source_staleness_check":
                monitored_sources = [
                    source
                    for source in registry.sources
                    if source.automated_fetch
                    and (
                        source.category != "charging-poi"
                        or settings.open_charge_map_api_key.get_secret_value().strip()
                    )
                ]
                try:
                    stale = await asyncio.to_thread(
                        metadata_repository.find_stale_sources, monitored_sources
                    )
                    await asyncio.to_thread(
                        monitoring_repository.reconcile_stale_sources,
                        monitored_sources,
                        stale,
                    )
                    await asyncio.to_thread(monitoring_repository.succeed, job)
                    logger.info(
                        "source_staleness_check_complete",
                        monitored_sources=len(monitored_sources),
                        stale_source_ids=stale,
                        stale_count=len(stale),
                    )
                except Exception as error:
                    await asyncio.to_thread(
                        monitoring_repository.fail, job, "staleness", error
                    )
                    logger.exception(
                        "source_staleness_check_failed", error_type=type(error).__name__
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
                    await asyncio.to_thread(monitoring_repository.succeed, job)
                except Exception as error:
                    await asyncio.to_thread(
                        monitoring_repository.fail, job, "discovery", error
                    )
                    logger.exception(
                        "source_discovery_failed",
                        brand=job.brand,
                        data_type=job.data_type,
                        error_type=type(error).__name__,
                    )
                continue
            try:
                if source is None:
                    raise KeyError(f"Unknown source for monitoring job: {job.source_id}")
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
                        await asyncio.to_thread(
                            monitoring_repository.succeed,
                            job,
                            http_status=snapshot.http_status,
                            parse_status=parse_outcome.status,
                            content_changed=parse_outcome.status == "parsed",
                        )
                    except Exception as error:
                        await asyncio.to_thread(
                            monitoring_repository.partial,
                            job,
                            "extraction",
                            error,
                            http_status=snapshot.http_status,
                            parse_status=parse_outcome.status,
                            content_changed=parse_outcome.status == "parsed",
                        )
                        logger.exception(
                            "source_extraction_failed",
                            source_id=source.id,
                            content_hash=snapshot.content_hash,
                            error_type=type(error).__name__,
                    )
                except Exception as error:
                    await asyncio.to_thread(
                        monitoring_repository.fail,
                        job,
                        "parser",
                        error,
                        http_status=snapshot.http_status,
                    )
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
                await asyncio.to_thread(
                    monitoring_repository.fail, job, "fetch", error
                )
                logger.exception(
                    "source_fetch_failed",
                    source_id=job.source_id,
                    error_type=type(error).__name__,
                )
    finally:
        await open_charge_map.close()
        await client.aclose()
        logger.info("worker_stopped")


def main() -> None:
    configure_logging()
    asyncio.run(run_worker(Settings()))


if __name__ == "__main__":
    main()
