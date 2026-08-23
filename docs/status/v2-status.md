# V2 milestone status

Last updated: 2026-08-23

Overall: **IN PROGRESS — V2.5**

## Governing decisions

- The design document governs business rules, schema and architecture; the plan
  governs execution order.
- V1 primitives are reused but are not counted as completed V2 capabilities.
- Provider calls run asynchronously in ingestion/enrichment. Public API and web
  request paths read published data only.
- Facts require stored source snapshots and provenance. Search snippets are URL
  discovery hints only and are never stored as facts.
- Deterministic extraction precedes any optional schema-bound LLM fallback.
- Unknown is a valid published data state; unsupported inference is not.

No material conflict between the two source documents has been found so far.

## Source audit and implementation matrix

| Capability | Existing V1 primitive | Required V2 gap | Owner milestone |
|---|---|---|---|
| Source discovery | Known URL registry and fetcher | Brave adapter, budget, query cache, templates, normalized URL candidates | V2.1 |
| Domain parsing | Generic seed parsers | Versioned parser registry and fixtures per priority domain | V2.2 |
| Structured extraction | Candidate/change records | Schema validation, confidence and entity resolution | V2.3 |
| Review and rollback | Review queue, locks, audit | Risk routing and reversible publication rollback | V2.4 |
| Monitoring | Scheduler, heartbeat, basic change detection | Provider health, freshness/drift alerts and run observability | V2.5 |
| Charging/map | Energy data only | OCM/Goong server adapters, normalized stations and degraded mode | V2.6 |
| Price/offer history | Dealer offers and provenance | History API/UX, trend/explainability and stale offer states | V2.7 |
| Full market | Explicitly partial V1 dataset | Reviewed BrandScope, complete active model/trim matrix and coverage gate | V2.8 |

## Milestones and gates

- [x] V2.1 Brave discovery — PASS 2026-08-23
- [x] V2.2 Domain parsers — PASS 2026-08-23
- [x] V2.3 Structured extraction — PASS 2026-08-23
- [x] V2.4 Change detection, review and rollback — PASS 2026-08-23
- [ ] V2.5 Automated monitoring
- [ ] V2.6 Charging and map enrichment
- [ ] V2.7 Price and offer history UX
- [ ] V2.8 Full-market coverage
- [ ] V2 FINAL GATE

## V2.1 gate — PASS

Implemented evidence:

- `ingestion.discovery` is a real server-side Brave Web Search client using the
  documented HTTPS endpoint and `X-Subscription-Token`; an absent key produces
  `MissingBraveApiKey` on a cache miss.
- Known URLs return before query generation or budget reservation.
- Versioned templates cover vehicle, price, promotion, specs, brochure, dealer
  offer and financing campaign discovery per official domain.
- Redis caches normalized URL candidates and uses an atomic Lua counter for the
  UTC monthly cap. Every retry reserves another request conservatively.
- Only URL/domain/query/rank/timestamp metadata is retained. Result descriptions,
  snippets and page copy are discarded.
- HTTPS, credential, port, official-domain and IP-literal checks prevent unsafe
  discovery candidates; URL/domain/result deduplication is deterministic.
- CLI, worker job and bounded candidate queue are wired; user-facing web/API
  paths have no Brave dependency.

Official contract checked on 2026-08-23:

- <https://api-dashboard.search.brave.com/documentation/guides/authentication>
- <https://api-dashboard.search.brave.com/api-reference/web/search/get>
- <https://api-dashboard.search.brave.com/documentation/guides/rate-limiting>

Gate commands/evidence:

- `python -m pytest workers/ingestion/tests` — 32 passed.
- `npm run lint:web && npm run test:web && npm run build:web` — pass (6 web tests).
- `dotnet build ... --configuration Release` and `dotnet test ... --no-build`
  — pass (43 API tests, zero warnings).
- Worker/scheduler Docker images build; both services are healthy.
- Container CLI known-URL-first smoke and `scripts/verify_v2_1_discovery.py` pass
  with zero Brave requests.

Credential note: `BRAVE_SEARCH_API_KEY` is absent locally, so a paid-provider
smoke was not fabricated or run. The deterministic HTTP contract test uses
`httpx.MockTransport` only in tests; production code always calls the real Brave
endpoint. Add the key to `docs/CODEX-SECRETS.local.md` to run
`discover-source --force-discovery` without changing code.

## V2.2 gate — PASS

Implemented evidence:

- Versioned per-domain HTML parser profiles cover every current automated HTML
  source. PDF, JSON and XML use explicit content-type parsers; unsupported
  content fails closed.
- HTML parses JSON-LD before configured content selectors, removes navigation,
  script/style and footer copy, and records canonical/meta data for provenance.
- PDF workflow uses pinned `pypdf`, records metadata/page count and extracts text
  per page with bounded page/content limits and warnings.
- Worker writes a JSON parsed artifact below the immutable source content hash,
  updates `source_snapshots.parser_version`, and queues only new parsed artifacts.
- If the same content hash/parser version already exists, object download and
  parsing are skipped. Snapshot bytes are SHA-256 verified before parsing.
- Synthetic, rights-safe HTML fixtures are checked into `data/fixtures/parsers`;
  PDF fixtures are generated in-memory by tests and are never production data.
- HTTP-first/Playwright-only-for-HTML behavior remains enforced by
  `KnownUrlFetcher`; parsers never trigger browser/network access.

Gate commands/evidence:

- `validate-parser-registry` — 24 HTML profiles and all 29 automated sources
  resolve to a versioned parser.
- `python -m pytest workers/ingestion/tests` — 37 passed.
- `npm run lint:web && npm run test:web && npm run build:web` — pass (6 tests).
- .NET Release build and 43 tests pass with zero warnings; EF reports no pending
  model changes.
- Worker/scheduler images build and both containers are healthy.
- Real allowlisted Toyota fetch returned HTTP 200, stored a 194,178-byte
  immutable snapshot, wrote a `toyota-html/2.2.0` parsed artifact and updated the
  DB snapshot row. Repeating the fetch produced `parse_status=unchanged` and did
  not add a second parsed event.

## V2.3 gate — PASS

Implemented evidence:

- `DeterministicExtractor` reads JSON-LD first, then bounded anchored patterns
  for MSRP, dimensions, seats, power, torque, battery, range and official
  consumption. It never infers a missing value.
- `UnitNormalizer` handles Vietnamese price notation and canonical VND, mm, kW,
  Nm, kWh, km, L/100km and kWh/100km values with plausible-range rejection while
  retaining original raw value/unit.
- Trim-first entity resolution uses normalized brand/model/trim names and aliases.
  A unique trim/model must pass thresholds; close matches produce `ambiguous`
  with alternatives and no guessed entity ID.
- Confidence combines source authority, deterministic/JSON-LD/local-LLM method,
  entity resolution and conflicts. Conflicting normalized values are retained as
  reviewable candidates with capped confidence.
- Optional local OpenAI-compatible extraction is disabled by default, runs only
  after deterministic extraction yields no facts, requests strict JSON Schema,
  validates through Pydantic and discards any raw value not grounded verbatim in
  the parsed snapshot.
- Candidate `SourceFact` IDs and extraction artifacts are deterministic and
  idempotent. Candidates do not overwrite published catalog data.

Gate commands/evidence:

- `python -m pytest workers/ingestion/tests` — 42 passed, including unit
  conversion, structured-first extraction, ambiguity refusal, LLM grounding and
  immutable pipeline replay.
- Worker/scheduler images build and services are healthy.
- Real Toyota replay resolved the exact Vietnam trim, persisted one
  `spec.seats=5` candidate with `VerifiedOfficial` confidence and
  `structured-extraction/2.3.0` provenance. A second replay returned
  `extraction_status=unchanged`, inserted zero facts and emitted no duplicate
  candidate event.
- Web lint/build and 6 tests pass; .NET Release build and 43 tests pass with zero
  warnings; EF reports no pending model changes.

Credential note: no local LLM endpoint/model is configured, so the optional
fallback was not called. Its HTTP interaction is exercised only through a strict
schema/grounding contract test; production remains deterministic-only until an
operator supplies `LOCAL_LLM_BASE_URL` and `LOCAL_LLM_MODEL`.

## V2.4 gate — PASS

Implemented evidence:

- Every structured candidate is diffed against its typed canonical trim value.
  Deterministic IDs make replay idempotent; unchanged facts do not create review
  noise.
- The anomaly policy routes unresolved entities, conflicts, active field locks,
  price changes, seat changes, and large technical/dimension deltas to review.
  Only a `VerifiedOfficial` dimension correction at or below 3%, with no lock or
  conflict and an existing canonical mapping, may auto-publish.
- Candidate facts remain separate from published values. Publication is mapped
  explicitly to `Price`, `TrimSpec`, `PowertrainProfile` or `EnergyProfile`; an
  unsupported or stale mapping fails closed.
- Admin review presents published before value, normalized candidate and the
  immutable source snapshot side by side, including raw extraction evidence,
  parser version, object key, source authority and confidence.
- Approve, reject and edit-and-publish record actor/reason/timestamp. Publication
  versions retain before/after values plus both previous and candidate
  `SourceFact` lineage. Price mutation archives the prior version in
  `price_history`.
- Only the latest published version for an entity field can be rolled back.
  Rollback restores the exact typed value and previous provenance, refreshes the
  catalog read model/cache, records an audit event and rejects repeat rollback.

Gate commands/evidence:

- Worker suite — 47 passed, including safe auto-publish, initial/high-delta price anomaly,
  field-lock, conflict and unresolved-entity policies.
- API suite — 44 passed; web lint, 6 tests and production build pass.
- EF reports no pending model changes. Migration
  `20260823034531_AddV24PublicationRollback` passed up → down → up against local
  PostgreSQL and leaves `publication_versions` plus anomaly context applied.
- `scripts/verify_v2_4_change_review.py` passes against Docker: it verifies real
  V2.3 official snapshot evidence is shown, publishes a reviewed typed value,
  observes canonical/read lineage, restores exact prior value and SourceFact,
  verifies audit, and confirms repeat rollback returns conflict.
- A live HTTP 200 Toyota replay reused the immutable 194,178-byte snapshot and
  extraction artifact, reported one unchanged fact and produced zero duplicate
  changes/publications. API, web, worker and scheduler containers are healthy.
