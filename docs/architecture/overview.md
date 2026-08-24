# Architecture overview

Vietnam Car Platform starts as a modular ASP.NET Core monolith plus a separate Python ingestion worker. Next.js renders public/admin experiences. PostgreSQL owns canonical published, candidate, history and audit data. Redis is limited to cache, queue and rate counters. MinIO supplies the local S3-compatible snapshot/media boundary; production can use Cloudflare R2 or S3 without changing domain ownership.

## Synchronous path

`Browser → Next.js → ASP.NET Core API → PostgreSQL/Redis`

The normal catalog path never calls Brave Search, Playwright, manufacturer sites or map providers. Calculator formulas live in backend domain/application modules and always return assumptions, applied rules, warnings and timestamps.

## Asynchronous path

`Scheduler/API → Redis queue → Python worker → immutable snapshot → parse/normalize/validate → candidate change → risk policy → publish/review → PostgreSQL outbox → search projector`

Worker failure may mark a source stale or a job failed, but may not delete or mutate the currently published version.

## Module boundaries

Catalog, Pricing, Registration, Energy, Ownership, Affordability, Financing,
Compare, Sources, Admin and Coverage are separate module boundaries inside the
API. Cross-module writes use explicit application services/transactions.
Worker/admin publications write a durable `CatalogSearchSync.*` event in the
same transaction as canonical facts. Horizontally safe API projectors use a
PostgreSQL advisory lock to coalesce events, refresh the materialized search
view asynchronously, schedule failed retries and invalidate catalog cache only
after a successful refresh.

## Runtime versions

- .NET 8 LTS (`global.json` pins SDK 8.0.422, still supported on the implementation date).
- Node.js 24 and Next.js App Router.
- Python 3.12.
- PostgreSQL 16, Redis 7 and S3-compatible object storage.

The design calls for an LTS/stable runtime rather than a particular major. .NET 8 is selected for reproducible local builds on the supplied environment; upgrading runtime major requires regression/golden tests and an ADR.
