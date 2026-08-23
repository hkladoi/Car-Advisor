from __future__ import annotations

import argparse
import asyncio
import json
from dataclasses import asdict
from pathlib import Path

import redis.asyncio as redis

from ingestion.contracts import load_manual_import
from ingestion.discovery import (
    BraveSearchClient,
    BraveSearchOptions,
    DiscoveryRequest,
    DiscoveryService,
    QueryTemplateCatalog,
)
from ingestion.fetcher import KnownUrlFetcher, Snapshot
from ingestion.gate import evaluate_seed_gate
from ingestion.manifest import read_snapshot_manifest, write_snapshot_manifest
from ingestion.registry import SourceRegistry
from ingestion.parsers import DomainParserRegistry, ParserProfileRegistry
from ingestion.settings import Settings
from ingestion.storage import S3CompatibleObjectStorage
from ingestion.registration_seed import (
    RegistrationSeedPublisher,
    load_registration_seed,
    validate_registration_seed,
)
from ingestion.energy_seed import (
    EnergySeedPublisher,
    load_energy_seed,
    validate_energy_seed,
)


def _parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Vietnam Car Platform source-first ingestion")
    subparsers = parser.add_subparsers(dest="command", required=True)

    validate = subparsers.add_parser("validate-seed", help="Validate JSON/CSV and evaluate V1.2 gate")
    _common_seed_arguments(validate)

    fetch = subparsers.add_parser("fetch-seed", help="Fetch allowlisted official sources to immutable storage")
    _common_seed_arguments(fetch)
    fetch.add_argument("--manifest", type=Path, required=True)

    fetch_source = subparsers.add_parser("fetch-source", help="Fetch one allowlisted source and record metadata")
    fetch_source.add_argument("--registry", type=Path, required=True)
    fetch_source.add_argument("--source-id", required=True)
    fetch_source.add_argument("--dsn")

    publish = subparsers.add_parser("publish-seed", help="Publish a reviewed seed batch transactionally")
    _common_seed_arguments(publish)
    publish.add_argument("--manifest", type=Path, required=True)
    publish.add_argument("--dsn", required=True)

    discover = subparsers.add_parser(
        "discover-source",
        help="Use known official URLs first, then budgeted Brave discovery when requested",
    )
    discover.add_argument("--registry", type=Path, required=True)
    discover.add_argument("--brand", required=True)
    discover.add_argument("--data-type", required=True)
    discover.add_argument("--official-domain", action="append", default=[])
    discover.add_argument("--known-url", action="append", default=[])
    discover.add_argument("--force-discovery", action="store_true")
    discover.add_argument("--templates", type=Path)

    validate_parsers = subparsers.add_parser(
        "validate-parser-registry",
        help="Verify every automated registered source resolves to a V2.2 parser",
    )
    validate_parsers.add_argument("--registry", type=Path, required=True)
    validate_parsers.add_argument("--parsers", type=Path, required=True)

    for command, help_text in (
        ("validate-registration-seed", "Validate reviewed V1.5 registration rules"),
        ("fetch-registration-seed", "Fetch V1.5 legal and province sources to immutable storage"),
        ("publish-registration-seed", "Publish V1.5 regions and registration rules transactionally"),
    ):
        registration = subparsers.add_parser(command, help=help_text)
        registration.add_argument("--registry", type=Path, required=True)
        registration.add_argument("--seed", type=Path, required=True)
        if command != "validate-registration-seed":
            registration.add_argument("--manifest", type=Path, required=True)
        if command == "publish-registration-seed":
            registration.add_argument("--dsn", required=True)
    for command, help_text in (
        ("validate-energy-seed", "Validate reviewed V1.6 energy rates and profiles"),
        ("fetch-energy-seed", "Fetch V1.6 official energy sources to immutable storage"),
        ("publish-energy-seed", "Publish V1.6 energy data transactionally"),
    ):
        energy = subparsers.add_parser(command, help=help_text)
        energy.add_argument("--registry", type=Path, required=True)
        energy.add_argument("--seed", type=Path, required=True)
        if command != "validate-energy-seed":
            energy.add_argument("--manifest", type=Path, required=True)
        if command == "publish-energy-seed":
            energy.add_argument("--dsn", required=True)
    return parser


def _common_seed_arguments(parser: argparse.ArgumentParser) -> None:
    parser.add_argument("--registry", type=Path, required=True)
    parser.add_argument("--seed", type=Path, required=True)


async def _fetch_seed(registry: SourceRegistry, source_ids: set[str], settings: Settings) -> list[Snapshot]:
    storage = S3CompatibleObjectStorage(settings)
    fetcher = KnownUrlFetcher(settings.ingestion_user_agent)
    semaphore = asyncio.Semaphore(settings.ingestion_max_concurrency)

    async def fetch_one(source_id: str) -> Snapshot:
        async with semaphore:
            return await fetcher.fetch(registry.by_id(source_id), storage)

    return await asyncio.gather(*(fetch_one(source_id) for source_id in sorted(source_ids)))


async def _discover_source(args: argparse.Namespace, registry: SourceRegistry, settings: Settings) -> dict:
    brand_key = " ".join(args.brand.lower().split())
    matching_sources = [
        source
        for source in registry.sources
        if brand_key in " ".join(
            (source.id, source.name, source.owner, source.url)
        ).lower()
    ]
    domains = args.official_domain or [
        domain for source in matching_sources for domain in source.allowed_domains
    ]
    known_urls = args.known_url or [source.url for source in matching_sources]
    if not domains:
        raise ValueError(
            "No official domain found for brand; pass --official-domain explicitly"
        )

    client = redis.from_url(settings.redis_url, decode_responses=True)
    try:
        service = DiscoveryService(
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
            QueryTemplateCatalog.load(
                args.templates or Path(settings.discovery_query_templates_path)
            ),
            settings.brave_discovery_max_queries,
        )
        batch = await service.discover(
            DiscoveryRequest(
                brand=args.brand,
                data_type=args.data_type,
                allowed_domains=domains,
                known_urls=known_urls,
                force_discovery=args.force_discovery,
            )
        )
        return batch.model_dump(mode="json")
    finally:
        await client.aclose()


def main() -> None:
    args = _parser().parse_args()
    registry = SourceRegistry.load(args.registry)
    if args.command == "validate-parser-registry":
        profiles = ParserProfileRegistry.load(args.parsers)
        parsers = DomainParserRegistry(profiles)
        covered = [
            source.id
            for source in registry.sources
            if source.automated_fetch and source.category != "discovery"
            if parsers.resolve(source, source.url)
        ]
        print(
            json.dumps(
                {
                    "schema_version": profiles.schema_version,
                    "profiles": len(profiles.profiles),
                    "covered_sources": len(covered),
                    "source_ids": covered,
                },
                ensure_ascii=False,
                indent=2,
            )
        )
        return
    if args.command == "discover-source":
        result = asyncio.run(_discover_source(args, registry, Settings()))
        print(json.dumps(result, ensure_ascii=False, indent=2))
        return
    if args.command.endswith("energy-seed"):
        batch = load_energy_seed(args.seed)
        report = validate_energy_seed(batch, registry)
        if args.command == "validate-energy-seed":
            print(json.dumps(report, ensure_ascii=False, indent=2))
            return
        if args.command == "fetch-energy-seed":
            snapshots = asyncio.run(_fetch_seed(registry, batch.source_ids, Settings()))
            write_snapshot_manifest(args.manifest, snapshots)
            print(json.dumps({"snapshots": len(snapshots), "manifest": str(args.manifest)}, indent=2))
            return
        settings = Settings()
        result = EnergySeedPublisher(
            args.dsn,
            S3CompatibleObjectStorage(settings),
        ).publish(batch, registry, read_snapshot_manifest(args.manifest))
        print(json.dumps(result, ensure_ascii=False, indent=2))
        return
    if args.command.endswith("registration-seed"):
        batch = load_registration_seed(args.seed)
        report = validate_registration_seed(batch, registry)
        if args.command == "validate-registration-seed":
            print(json.dumps(report, ensure_ascii=False, indent=2))
            return
        if args.command == "fetch-registration-seed":
            snapshots = asyncio.run(_fetch_seed(registry, batch.source_ids, Settings()))
            write_snapshot_manifest(args.manifest, snapshots)
            print(json.dumps({"snapshots": len(snapshots), "manifest": str(args.manifest)}, indent=2))
            return
        settings = Settings()
        result = RegistrationSeedPublisher(
            args.dsn,
            S3CompatibleObjectStorage(settings),
        ).publish(batch, registry, read_snapshot_manifest(args.manifest))
        print(json.dumps(result, ensure_ascii=False, indent=2))
        return
    if args.command == "fetch-source":
        from ingestion.metadata import SnapshotMetadataRepository

        settings = Settings()
        source = registry.by_id(args.source_id)
        snapshot = asyncio.run(
            KnownUrlFetcher(settings.ingestion_user_agent).fetch(
                source,
                S3CompatibleObjectStorage(settings),
            )
        )
        snapshot_id = SnapshotMetadataRepository(args.dsn or settings.postgres_dsn).record(source, snapshot)
        print(json.dumps({**asdict(snapshot), "snapshot_id": str(snapshot_id)}, default=str, ensure_ascii=False, indent=2))
        return

    batch = load_manual_import(args.seed)
    report = evaluate_seed_gate(batch, registry)
    if not report.passed:
        print(json.dumps(asdict(report), ensure_ascii=False, indent=2))
        raise SystemExit(2)

    if args.command == "validate-seed":
        print(json.dumps(asdict(report), ensure_ascii=False, indent=2))
        return

    if args.command == "fetch-seed":
        snapshots = asyncio.run(
            _fetch_seed(registry, {record.source_id for record in batch.records}, Settings())
        )
        write_snapshot_manifest(args.manifest, snapshots)
        print(json.dumps({"snapshots": len(snapshots), "manifest": str(args.manifest)}, indent=2))
        return

    from ingestion.publisher import PostgresPublisher

    snapshots = read_snapshot_manifest(args.manifest)
    result = PostgresPublisher(args.dsn).publish(batch, registry, snapshots)
    settings = Settings()
    try:
        from ingestion.cache import invalidate_catalog_cache

        result["catalog_cache_keys_invalidated"] = invalidate_catalog_cache(settings.redis_url)
    except Exception as error:  # cache TTL remains the safe fallback after a committed DB publish
        result["catalog_cache_invalidation_warning"] = type(error).__name__
    print(json.dumps(result, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
