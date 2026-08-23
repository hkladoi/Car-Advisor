# Vietnam Car Platform

Trim-first automotive data platform for new cars officially sold in Vietnam. The product separates published data from ingestion candidates, preserves source provenance, versions time-sensitive rules and keeps ownership affordability separate from purchase/financing affordability.

## Repository map

- `apps/web` — Next.js App Router web experience.
- `apps/api` — ASP.NET Core modular monolith and authoritative calculators.
- `workers/ingestion` — Python fetch/parse/normalize/diff worker and scheduler.
- `packages/contracts` — generated OpenAPI document and TypeScript client.
- `packages/taxonomy` — canonical codes shared by data tooling and web.
- `data` — curated seeds, validated imports and legally permitted fixtures.
- `infra` — Docker, Compose and monitoring assets.
- `docs` — architecture, ADRs, runbooks, source ownership and milestone status.

## Local start

Prerequisites: Docker Desktop with Linux containers and Compose, Node.js 24+, .NET SDK 8.0.422 and Python 3.12.

```powershell
./scripts/bootstrap-local.ps1
```

The bootstrap copies `.env.example` to `.env` only when `.env` does not exist, builds the stack and starts web/API/PostgreSQL/Redis/MinIO/worker/scheduler.

- Web: `http://localhost:3000`
- API Swagger: `http://localhost:8080/swagger`
- API liveness: `http://localhost:8080/health/live`
- API readiness: `http://localhost:8080/health/ready`
- MinIO console: `http://localhost:9001`

## Local checks

```powershell
npm ci
npm run check
dotnet restore VietnamCarPlatform.sln
dotnet build VietnamCarPlatform.sln --configuration Release --no-restore
dotnet test VietnamCarPlatform.sln --configuration Release --no-build

& 'C:\path\to\python.exe' -m pip install -r workers/ingestion/requirements.txt
& 'C:\path\to\python.exe' -m pytest workers/ingestion/tests
```

Generate the OpenAPI contract from the running ASP.NET application, then regenerate the web client:

```powershell
dotnet build apps/api/src/Api/VietnamCarPlatform.Api.csproj
./scripts/export-openapi.ps1
npm run generate:api-client
```

## Troubleshooting

- `health/ready` is unhealthy: verify PostgreSQL, Redis and MinIO health in `docker compose ps`; readiness intentionally fails when any source-of-truth dependency is missing.
- Docker cannot access the engine: start Docker Desktop and ensure Linux containers are selected.
- Port conflict: stop the local process on 3000/5432/6379/8080/9000/9001 or change the host-side mapping.
- OpenAPI generation times out: run the API build first and inspect `.tmp/openapi/api.err.log`.
- Worker restarts: inspect `docker compose logs ingestion-worker`; a failed parser must never mutate published data.

## Architecture guardrails

- Frontend never owns legal, on-road, energy, affordability or financing formulas.
- User request paths never call Brave Search or Playwright.
- Redis is a cache/queue, never the source of truth.
- External APIs are bounded, cached and non-critical to normal catalog reads.
- UNKNOWN is distinct from NOT_AVAILABLE and NOT_APPLICABLE.
- Calculator responses include assumptions, applied rule/source IDs, warnings and calculation time.

Current progress is tracked in `docs/status/v1-status.md`.

Operational onboarding and final-release evidence:

- `docs/runbooks/onboard-brand-trim-dealer-source.md`
- `docs/runbooks/restore.md`
- `docs/requirements-traceability-v1.md`
