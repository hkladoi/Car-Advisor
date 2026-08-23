# Vietnam Car Platform — engineering handoff

Last updated: 2026-08-23

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
- V2.1–V2.4 are complete; V2.5 automated monitoring is next. See
  `docs/status/v2-status.md`.
- V3 must not start until the V2 FINAL GATE passes; see
  `docs/status/v3-status.md`.

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
V1 seeds, runs their golden checks and checks web/API health. Use
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

Required for V2.1 real discovery: `BRAVE_SEARCH_API_KEY`. Optional/non-critical
enrichment and observability: `GOONG_API_KEY`, `GOONG_MAPTILES_KEY`,
`OPEN_CHARGE_MAP_API_KEY`, `SENTRY_DSN`, `OTEL_EXPORTER_OTLP_ENDPOINT`.
Discovery must fail explicitly when its key is absent; the public product must
remain available.

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
- API liveness/readiness: <http://localhost:8080/health/live> and
  <http://localhost:8080/health/ready>
- MinIO console: <http://localhost:9001>
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

## Continuing implementation

Implement one milestone at a time in plan order. For every milestone: finish
code and migration, add deterministic tests, run the relevant build/tests and
integration gate, fix failures, then update the corresponding status document
with commands and evidence. Do not advance while its gate is failing. Any
design/plan conflict must be recorded with the chosen product-safe decision.
