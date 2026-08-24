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
Compare, Sources, Admin, Coverage and Partner API are separate module boundaries
inside the API. Cross-module writes use explicit application services/transactions.
Worker/admin publications write a durable `CatalogSearchSync.*` event in the
same transaction as canonical facts. Horizontally safe API projectors use a
PostgreSQL advisory lock to coalesce events, refresh the materialized search
view asynchronously, schedule failed retries and invalidate catalog cache only
after a successful refresh.

The existing anonymous `/api/v1` product surface and the read-only
`/api/v1/partner` integration surface share reviewed catalog services rather
than duplicate data. Partner credentials are returned once, stored as
prefix-plus-SHA-256 only, policy-version bound and administratively audited.
PostgreSQL owns key/plan lifecycle; an atomic Redis minute/month counter enforces
usage across API replicas and fails closed. See ADR-012.

## Runtime versions

- .NET 8 LTS (`global.json` pins SDK 8.0.422, still supported on the implementation date).
- Node.js 24 and Next.js App Router.
- Python 3.12.
- PostgreSQL 16, Redis 7 and S3-compatible object storage.

The design calls for an LTS/stable runtime rather than a particular major. .NET 8 is selected for reproducible local builds on the supplied environment; upgrading runtime major requires regression/golden tests and an ADR.

## Initial V3 capacity target

ADR-013 defines the measurable single-replica target as 20 RPS for 60 seconds
with zero errors and the design p95 budgets: catalog below 300 ms, detail below
400 ms and recommendation/heavy calculation below 700 ms. This API load gate is
paired with the isolated 100,000-row PostgreSQL search benchmark. Raise and
rerun both gates before claiming a higher traffic or materially larger-dataset
capacity.
