# Vietnam Car Platform — engineering handoff

Last updated: 2026-08-24

## Product and source of truth

This repository implements a Vietnam-first car research, cost and advisory
platform. The design document is authoritative for product behavior, schema
and architecture; the plan controls implementation order:

- `docs/spec/thiet-ke-project-vietnam-car-platform-full-v3.docx`
- `docs/spec/plan-v1-v2-v3-vietnam-car-platform-full-v2.md`

When the documents leave room for interpretation, preserve the product goals:
traceable real data, candidate/published separation, human review for risky
changes, explicit unknowns and no provider calls on the end-user request path.
Decisions and gate evidence belong in `docs/status/`.

## Current state

- V1.0–V1.10 and the V1 FINAL GATE are complete.
- V2.1–V2.8 and the V2 FINAL GATE are complete. See
  `docs/status/v2-status.md`. The live reviewed scope is 51 brands (38 included,
  13 excluded), 255 model candidates, 49 explicit trim candidates and 236
  documented trim-inventory gaps; do not replace those gaps with invented data.
- V3.1 explainable recommendation is complete and its gate passed. The current
  reviewed catalog is intentionally too sparse to publish a ranked result, so
  `/recommend` exposes a data-withheld ledger instead of fabricated scores.
- V3.2 opt-in accounts/privacy is complete. Anonymous behavior remains the
  default; account owners can save profile/comparisons/watchlist, inspect
  current alert signals, export all private data and permanently delete it.
- V3.3 trusted EEA real-world consumption is complete. The live data contains
  322 manufacturer/fuel/year cohorts representing 6,515,134 reported vehicles;
  only reviewed exact manufacturer mappings are linked, and every reference is
  explicitly non-trim.
- V3.4 PostgreSQL-first search scale is complete. The isolated 100,000-row
  benchmark passed the 150 ms/query p95 gate, so Typesense/Meilisearch was not
  added. Search-affecting publications now synchronize through a transactional
  outbox and retryable async projector; final gate event-to-index latency was
  256 ms.
- V3.5 public/partner API is complete. The separate read-only
  `/api/v1/partner` surface uses one-time 256-bit credentials (hash/prefix only
  at rest), PostgreSQL plans, atomic Redis minute/month quotas, exact policy
  acceptance and source-specific attribution. See ADR-012 and
  `docs/api/public-partner-v1.md`.
- V3 FINAL GATE is complete. The consolidated gate passed deterministic
  recommendation, full privacy lifecycle, isolated 100,000-row search,
  backup/restore and 1,200-response target load at 20 RPS with zero errors. See
  ADR-013 and `docs/status/v3-status.md`. V1, V2 and V3 are complete; there is no
  unlocked milestone in the supplied plan.

## Architecture

- Web: Next.js 16, React 19 and TypeScript in `apps/web`.
- API: ASP.NET Core 8 modular monolith in `apps/api`.
- Ingestion: Python 3.12 worker and scheduler in `workers/ingestion`.
- State: PostgreSQL 16, Redis 7 and versioned MinIO object storage.
- Contracts/taxonomy: `packages/contracts` and `packages/taxonomy`.
- Local orchestration: Docker Compose.

The API serves published records only. Ingestion stores an immutable source
snapshot before parsing. Extracted candidates and changes are reviewed before
publication where policy requires it. Brave, Playwright, Goong and Open Charge
Map are ingestion/enrichment dependencies; an interactive user request never
waits on them.

## Clean-clone setup

Prerequisites:

- Docker Desktop with Linux containers and Docker Compose v2
- Node.js 24+ and npm 11+
- .NET SDK 8.0.422
- Python 3.12

From the repository root, run one of:

```powershell
.\scripts\bootstrap-local.ps1
```

```sh
./scripts/bootstrap-local.sh
```

The bootstrap is repeatable. It creates `.env`, securely merges local provider
keys, installs dependencies, builds and starts the Compose stack, waits for
readiness, checks schema constraints, validates/fetches/publishes the official
V1 seeds plus the official EEA V3.3 cohort snapshot, runs their golden checks,
the V3.4 isolated PostgreSQL benchmark/async projection gate, both V3.5 partner
API/migration gates and the consolidated V3 FINAL gate, then checks web/API
health. Use
`-SkipInstall`/`SKIP_INSTALL=1` or `-SkipSeed`/`SKIP_SEED=1` only for an already
prepared local environment.

## Local secrets workflow

1. Keep `.env.example` committed with names and non-secret defaults only.
2. Put real local credentials in `docs/CODEX-SECRETS.local.md` as non-empty
   `KEY=VALUE` lines. The file is Git-ignored.
3. Bootstrap copies `.env.example` to `.env` when necessary and merges those
   values without logging them.
4. Never commit `.env`, the local secrets file, tokens, session cookies or
   provider responses containing credentials.
5. Never expose server keys to the frontend. Only intentional public settings
   may use the `NEXT_PUBLIC_` prefix.

Required for the core local stack: `DATABASE_URL`, `REDIS_URL`,
`OBJECT_STORAGE_ENDPOINT`, `OBJECT_STORAGE_BUCKET`,
`OBJECT_STORAGE_ACCESS_KEY`, `OBJECT_STORAGE_SECRET_KEY`. Compose supplies safe
local defaults.

`SEARCH_SYNC_INTERVAL_MILLISECONDS` (default 500) and
`SEARCH_SYNC_BATCH_SIZE` (default 250) tune the PostgreSQL outbox projector.
They are non-secret operational values; keep the interval within 100–10,000 ms
and the batch within 1–1,000. Do not add Typesense/Meilisearch unless ADR-002's
measured gate is exceeded.

Required for V2.1 real discovery: `BRAVE_SEARCH_API_KEY`. Optional/non-critical
enrichment and observability: `GOONG_API_KEY`, `GOONG_MAPTILES_KEY`,
`OPEN_CHARGE_MAP_API_KEY`, `SENTRY_DSN`, `OTEL_EXPORTER_OTLP_ENDPOINT`.
Discovery must fail explicitly when its key is absent; the public product must
remain available.

V2.6 provider behavior is optional and server-only. With
`OPEN_CHARGE_MAP_API_KEY` configured, the scheduler refreshes Vietnam reference
POIs weekly; an operator can request an immediate run with:

```powershell
docker compose exec -T ingestion-worker python -m ingestion.cli enqueue-charging-poi --registry /app/data/source-registry.v1.json
```

`GOONG_API_KEY` enables on-demand server geocoding. `GOONG_MAPTILES_KEY` remains
reserved and is never exposed by the current web/API path. OCM costs are never
tariff facts; only reviewed provider mappings backed by first-party tariff
provenance may show a tariff.

Optional difficult-page extraction may use a local OpenAI-compatible endpoint
through `LOCAL_LLM_BASE_URL`, `LOCAL_LLM_MODEL` and `LOCAL_LLM_API_KEY`. It is
disabled unless both URL and model are set; deterministic extraction always runs
first and all local-LLM output remains schema-validated candidate data.

## Common commands

```powershell
npm ci
npm run lint:web
npm run test:web
npm run build:web
dotnet restore VietnamCarPlatform.sln
dotnet build VietnamCarPlatform.sln --configuration Release
dotnet test VietnamCarPlatform.sln --configuration Release
python -m pip install -r workers/ingestion/requirements.txt
python -m pytest workers/ingestion/tests
python scripts/verify_v2_6_charging.py
python scripts/verify_v2_7_history.py
python scripts/verify_v2_8_coverage.py
python scripts/verify_v2_final.py
python scripts/verify_v3_1_recommendation.py
python scripts/verify_v3_2_accounts.py
python scripts/verify_v3_3_real_world.py
python scripts/verify_v3_4_search.py
python scripts/verify_v3_5_partner_api.py
python scripts/verify_v3_5_migration.py
python scripts/load_v3_final.py
python scripts/verify_v3_final.py
docker compose up --build -d --wait
docker compose ps
docker compose logs -f api ingestion-worker ingestion-scheduler
```

Migrations:

```powershell
dotnet tool restore
dotnet ef database update --project apps/api/src/Infrastructure/VietnamCarPlatform.Infrastructure.csproj --startup-project apps/api/src/Api/VietnamCarPlatform.Api.csproj
dotnet ef migrations has-pending-model-changes --project apps/api/src/Infrastructure/VietnamCarPlatform.Infrastructure.csproj --startup-project apps/api/src/Api/VietnamCarPlatform.Api.csproj
```

## Local endpoints

- Web: <http://localhost:3000>
- API Swagger: <http://localhost:8080/swagger>
- Partner policy: <http://localhost:8080/api/v1/partner/policy>
- API liveness/readiness: <http://localhost:8080/health/live> and
  <http://localhost:8080/health/ready>
- MinIO console: <http://localhost:9001>
- Energy price history: <http://localhost:3000/energy/history>
- Public full-market coverage: <http://localhost:3000/coverage>
- Explainable recommendation: <http://localhost:3000/recommend>
- Account/privacy workspace: <http://localhost:3000/account>
- PostgreSQL: `localhost:5432`; Redis: `localhost:6379`

## Debugging and recovery

- Start with `docker compose ps` and `docker compose logs --tail 200 SERVICE`.
- A readiness failure normally identifies PostgreSQL, Redis or object storage
  in the API health payload.
- Ingestion health also checks its Redis heartbeat. Inspect both worker and
  scheduler logs.
- Do not delete volumes to solve migration or data issues. Use the documented
  backup/restore drill (`python scripts/backup_restore_test.py`) and preserve
  evidence.
- The CI workflow in `.github/workflows/ci.yml` is the executable reference for
  all V1 seed and gate commands.
- If a host-side migration reports PostgreSQL password authentication failure
  while Compose is healthy, do not print or guess the password. Rebuild/start
  the API with `APPLY_DATABASE_MIGRATIONS=true`; it uses the shared PostgreSQL
  Unix socket. Confirm the applied migration in `__EFMigrationsHistory`.

V3.3 live refresh is source-first and needs no API key:

```powershell
docker compose run --rm --no-deps --volume "${PWD}/.tmp:/app/.tmp" ingestion-worker python -m ingestion.cli fetch-real-world-consumption --registry /app/data/source-registry.v1.json --manifest /app/.tmp/v3.3-real-world.json
docker compose run --rm --no-deps --volume "${PWD}/.tmp:/app/.tmp:ro" ingestion-worker python -m ingestion.cli publish-real-world-consumption --registry /app/data/source-registry.v1.json --manifest /app/.tmp/v3.3-real-world.json --dsn "host=/var/run/postgresql dbname=vietnam_car_platform user=vcp"
python scripts/verify_v3_3_real_world.py
```

V3.5 partner keys are server-side credentials. Use authenticated admin routes
to issue a key after accepting the exact current policy; copy the plaintext once
into a secret manager and never place it in `CODEX-SECRETS.local.md` if that file
may be shared. Calls use `X-VCP-API-Key`; list/audit responses intentionally
cannot recover the key. Rotate by issuing a replacement and revoking the old
credential. See `docs/api/public-partner-v1.md` and
`docs/api/data-attribution-policy.md`.

## Continuing implementation

The supplied V1→V3 plan is complete. Before adding future scope, read both source
documents and all status/ADR evidence, define a new milestone with measurable
acceptance criteria, and preserve current contracts and gates. For every future
milestone: finish code/migration, add deterministic tests, run relevant
build/tests/integration/load gates, fix failures, then update status and handoff.
Any design conflict must be recorded with the chosen product-safe decision.
