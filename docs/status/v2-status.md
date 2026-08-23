# V2 milestone status

Last updated: 2026-08-23

Overall: **IN PROGRESS — V2.7**

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
- [x] V2.5 Automated monitoring — PASS 2026-08-23
- [x] V2.6 Charging and map enrichment — PASS 2026-08-23
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

## V2.5 gate — PASS

Implemented evidence:

- The scheduler emits category-specific monitoring jobs: vehicle price and
  promotion daily, vehicle specs/features/images weekly, official dealer offers
  daily, finance campaign references daily, fuel/legal/electricity/charging
  sources daily, and model discovery daily. Each monitor kind has its own Redis
  lease, so a weekly job cannot suppress a daily job for the same source.
- Brand registry entries intentionally marked `automated_fetch=false` run a
  bounded `source_discovery` job instead of crawling/publishing the homepage.
  Known official URLs return first, preserve source identity and consume zero
  Brave requests; forced search remains under the V2.1 budget policy.
- Every run has a UUID and durable lifecycle ledger with requested/start/end
  timestamps, status, duration, source, monitor kind, HTTP/parser outcome,
  content-change flag, and a bounded hashed error. The admin metrics aggregate
  the complete 24-hour window rather than truncating to the recent-run display.
- Consecutive parser failures open a deduplicated high-severity alert only at the
  configured threshold. Source staleness opens an authority-aware alert.
  Acknowledgement requires reviewer RBAC plus a reason and audit event; a
  successful parser run or recovered freshness resolves the alert.
- Failure paths retain published data. They record a failed/partial run and keep
  the immutable snapshot/candidate workflow separate from canonical catalog
  mutation.
- The registry now contains a real official Toyota dealer offer source and an
  official Toyota finance-information source. Finance terms are explicitly
  reference/as-of only and can never be presented as approval or a guaranteed
  offer. The dealer domain has its own versioned parser and rights-safe fixture.
- Admin `/admin/monitoring` displays full-day success metrics, monitor kinds,
  recent runs, open/resolved alerts and audited acknowledgement. OpenAPI and the
  generated TypeScript client include both monitoring endpoints.

Official sources checked on 2026-08-23:

- <https://www.toyota.com.vn/lien-he-dai-ly> identifies Toyota An Thanh
  Fukushima as an official dealer.
- <https://taf.toyota.com.vn/khuyen-mai-toyota-xe-moi-t8-2026/> is the dated
  official dealer offer monitored by the gate.
- <https://www.toyota.com.vn/tin-tuc/thong-tin-bo-tro/mua-xe-tra-gop-vios-43083>
  is the manufacturer finance-reference page; its changing terms remain
  provenance-backed reference information only.

Gate commands/evidence:

- Worker suite — 57 passed; API suite — 45 passed with zero build warnings;
  web lint, 6 tests and production build pass, including `/admin/monitoring`.
- EF reports no pending model changes. Migration
  `20260823040639_AddV25AutomatedMonitoring` passed up → down → up against local
  PostgreSQL and is the latest applied migration.
- The first live schedule completed all 53 automated fetch jobs successfully.
  Four brand-registry discovery jobs then completed with
  `strategy=known_url_first`, one official candidate each and zero charged Brave
  requests. The gate caught and fixed both the original scheduler exclusion and
  an invalid discovery data-type slug before this milestone was accepted.
- `scripts/verify_v2_5_monitoring.py` passes repeatedly: both official dealer
  and finance URLs return HTTP 200 and parse successfully; three deterministic
  parser failures open an alert; API acknowledgement adds exactly one audit per
  run; a recovery resolves parser and stale-source alerts; the canonical
  price/spec/powertrain/energy digest is unchanged.
- OpenAPI export and TypeScript generation pass. Gitleaks v8.29.0 reports no
  leaks. API, web, PostgreSQL, Redis, MinIO, worker and scheduler containers are
  healthy; deterministic gate failure rows remain as local operational evidence
  while both alerts finish in `Resolved` state.

## V2.6 gate — PASS

Implemented evidence:

- A bounded, retrying, server-only Open Charge Map v3 adapter synchronizes only
  Vietnam POIs, stores the canonical response as an immutable source snapshot,
  then transactionally normalizes stations and connectors. A failed, incomplete
  or non-Vietnam response preserves the last published station set.
- Station quality is explicitly `ReferenceOnly` with Unknown/Low/Medium/High
  confidence derived from OCM data quality. The public API and `/charging` page
  disclose coverage, staleness, snapshot time, confidence, and visible Open
  Charge Map/CC BY attribution.
- OCM `UsageCost` is deliberately discarded. A charging tariff is returned only
  after a reviewed station-to-provider mapping and only from an effective
  `ChargingTariff` with `SourceFact`/`SourceSnapshot` provenance. The schema and
  gate assert that station tables cannot contain price, tariff or cost fields.
- Optional Goong geocoding executes server-side only on explicit user search,
  with bounded response, timeout, hashed Redis cache, rate limit and sanitized
  degraded response. No key or request URL is logged, and neither REST nor map
  tile credentials are placed in browser code or API capability responses.
- The charging page reads the cached station API, can filter by bounding box,
  connector and power, and renders a responsive coordinate plot without
  pre-geocoding the dataset. Missing optional credentials or an empty station
  cache produces an honest unavailable/empty state; no synthetic fallback is
  inserted.
- The scheduler queues one weekly POI refresh only when the OCM key is configured.
  The manual `enqueue-charging-poi` command also fails before queue mutation when
  the key is absent. Core catalog endpoints never depend on OCM or Goong.

Official contracts checked on 2026-08-23:

- <https://openchargemap.org/develop>
- <https://openchargemap.org/develop/api>
- <https://github.com/openchargemap/ocm-system>
- <https://docs.goong.io/rest/geocode/>
- <https://docs.goong.io/rest/api-key/>

Gate commands/evidence:

- Worker suite — 69 passed, covering OCM v3 query/pagination, response bounds,
  Vietnamese coordinate/country validation, confidence, idempotent repository
  behavior, failure retention, credential absence and credential redaction.
- API Release build succeeds with zero warnings; 53 tests pass. Web lint, 8
  tests and production build pass, including `/charging`. The prior Vitest
  `window is not defined` unhandled scheduler error no longer reproduces.
- Migration `20260823050109_AddV26ChargingMapData` passed up → down → up against
  local PostgreSQL. EF reports no pending model changes.
- `scripts/verify_v2_6_charging.py` passes against all seven healthy Compose
  services. It verifies DB constraints, cached-reference semantics, invalid bbox
  handling, provider-only tariff policy, optional-provider degraded behavior,
  non-exposed keys, scheduler policy, OpenAPI paths and catalog independence.
- Desktop and 390×844 mobile `/charging` flows were exercised in a real Chromium
  browser: address search returned the expected `GOONG_NOT_CONFIGURED` state,
  cached content remained usable, all requests were HTTP 200 and the console had
  zero warnings/errors.

Credential note: both optional keys are absent locally, so no paid/provider live
call was fabricated. Production OCM and Goong paths call their real documented
endpoints; transport doubles are confined to contract tests. Supplying keys in
`docs/CODEX-SECRETS.local.md` enables the next real sync without a code change.

Recorded design decision: PostGIS was not introduced for this bounded,
Vietnam-only milestone because the design makes it optional when needed; indexed
latitude/longitude bounding-box filters meet the current acceptance path. Goong
map tiles are also not exposed to the browser because the product's stricter
server-secret rule takes priority; a coordinate plot plus on-demand server
geocoding preserves map usefulness without leaking credentials.
