# V3 milestone status

Last updated: 2026-08-24

Overall: **COMPLETE — V3 FINAL GATE PASS**

- [x] V3.1 Explainable recommendation
- [x] V3.2 Accounts, privacy, watchlists and alerts
- [x] V3.3 Trusted real-world data
- [x] V3.4 Search scale (only after benchmark evidence)
- [x] V3.5 Versioned public/partner API
- [x] V3 FINAL GATE

The design document governs product behavior and the plan governs order. Each
milestone will record migrations, tests and gate evidence here before moving to
the next milestone.

V2 FINAL GATE passed on 2026-08-23 before V3 work started.

## V3.1 — Explainable recommendation — PASS

Delivered:

- `POST /api/v1/recommendations` with strict hard filters, seven configurable
  weights, deterministic peer normalization, component raw facts, provenance,
  explicit completeness/source gates and reproducible methodology version
  `v3.1-deterministic-1`.
- Public `/recommend` workbench. It displays hard-filter accounting, the full
  score equation, raw facts/sources per component, ranked candidates and a
  separate data-withheld ledger. UNKNOWN never becomes zero.
- P/P is published only when the candidate passes the public completeness gate
  and source-trust gate. Current reviewed data honestly produces zero ranked
  candidates: 4/49 pass the default hard filters, and all four remain withheld
  with explicit missing-component reasons.
- Versioned OpenAPI and generated TypeScript contract updated. No migration was
  required because V3.1 is a stateless calculation over published records.

Product-safe decisions where the design leaves implementation detail open:

- Completeness threshold is 80% (therefore at least 6 of 7 components), and all
  contributing facts must be current, official and at least trusted-single-
  source. The client cannot lower this public threshold.
- Safety/ADAS, comfort and technology require at least three explicit reviewed
  canonical observations. Missing rows remain UNKNOWN rather than being counted
  as absent equipment.
- P/P is explicitly `0.40 × value + 0.60 × performance`; it is withheld unless
  both components exist and the overall candidate has passed both gates.
- Running cost uses the existing authoritative energy engine for 100 km with
  official consumption and current reviewed tariffs. PHEV/EREV are withheld
  until the user supplies an energy-share scenario; the engine does not invent
  one.

Gate evidence:

- `dotnet build apps/api/src/Api/VietnamCarPlatform.Api.csproj --no-restore` — PASS.
- `dotnet test apps/api/tests/Unit/VietnamCarPlatform.Api.UnitTests.csproj --no-restore`
  — 61/61 PASS, including hard-filter order, reproducibility, completeness,
  weak-source and P/P tests.
- `npm run lint:web` — PASS; `npm run test:web` — 9 files / 15 tests PASS;
  `npm run build:web` — PASS with dynamic `/recommend` and proxy route.
- `.venv/Scripts/python.exe -m pytest workers/ingestion/tests` — 72/72 PASS;
  `dotnet ef migrations has-pending-model-changes ...` — no model changes.
- Production Compose API/web images rebuilt; all seven services healthy.
- Live API repeat, strict-filter failure simulation, invalid-weight failure,
  closed candidate accounting, OpenAPI and web markers are executable in
  `python scripts/verify_v3_1_recommendation.py`.
- Browser QA passed at 1440 desktop and 320/375/414/768 widths with no horizontal
  overflow; form submit through the web proxy returned the live PostgreSQL result.

## V3.2 — Opt-in accounts and privacy lifecycle — PASS

Delivered:

- Explicit-consent member registration/login with salted PBKDF2-SHA512 password
  hashes, hashed 30-day session tokens, login throttling/lockout, HttpOnly
  SameSite=Strict web cookie and same-origin mutation guard. Anonymous catalog
  and calculators remain the default and continue to persist no profile.
- Account-owned region and affordability profile, saved 2–4 trim comparisons,
  per-trim watchlist preferences and price/promotion/dealer-offer alert policy.
  The account page never puts income, expenses, debt or cash in a URL.
- `/account` privacy workspace plus save actions in compare and vehicle detail.
  The account owner can download a complete JSON export and permanently delete
  the account, sessions, profile, comparisons and watchlist.
- Migration `20260823082136_AddV32UserAccounts` adds consent-guarded accounts,
  hashed sessions, JSON-constrained saved comparisons and unique account/trim
  watchlist rows with ownership/cascade constraints.
- Versioned `/api/v1/accounts/*` OpenAPI surface and generated TypeScript client
  cover register/login/session/profile/comparisons/watchlist/alerts/export/delete.

Product-safe decisions where the design leaves implementation detail open:

- Alerts are opt-in in-app current signals backed only by published PostgreSQL
  data. No unapproved email/SMS provider or user-request-time external call was
  introduced. Price target is optional; promotion can match trim or brand, and
  dealer offer respects watched province or the explicit nationwide scope.
- Export intentionally excludes password hash, session token/hash and client
  fingerprint. Deletion verifies both the current password and the exact word
  `DELETE`; no personally identifying audit residue is retained after deletion.
- At gate time the live reviewed catalog had 11 sourced price signals but no
  currently effective promotion/dealer-offer rows. Those two policies are
  covered by deterministic domain tests instead of seeding fake production
  offers; the live feed remains honest about its current contents.

Gate evidence:

- `dotnet build VietnamCarPlatform.sln --configuration Release --no-restore`
  — PASS, 0 warnings/errors; `.NET` tests — 66/66 PASS, including consent schema,
  salted password hash and all three alert matching policies.
- `npm run check` — lint PASS, 11 files / 17 tests PASS and production Next build
  PASS with `/account` and `/api/account/[...path]` routes. The original CI
  `window is not defined` regression remains resolved.
- `dotnet ef migrations has-pending-model-changes ...` — no model changes after
  the V3.2 migration. Migration applied successfully to the live Compose DB.
- `python scripts/verify_v3_2_accounts.py` — PASS for anonymous rejection,
  required consent, session, profile, saved comparison, 49-row live watchlist,
  provenance-bearing alerts, complete export and authenticated deletion. Exact
  private-row counts changed from `1/1/1/1/49` to `0/0/0/0/0`.
- Browser QA passed registration through the web proxy, HttpOnly/Strict cookie,
  authenticated dashboard and permanent deletion. Desktop and 375px mobile
  layouts have no horizontal overflow; console has no error.

## V3.3 — Trusted real-world consumption — PASS

Delivered:

- Official EEA OBFCM `2023_Cars_Aggregated.csv` ingestion through the existing
  source-first worker. A successful fetch writes the 41,167-byte response to
  immutable MinIO storage before validation or publication; the live snapshot
  hash is `4b4544898ff1cbaf055c1fac909feda2c1fe036f29a612b924343562ed3f74f4`.
- Strict UTF-8 CSV parser and V3.3 cohort contract for separate OBFCM and WLTP
  figures, weighted/unweighted fuel and CO2 metrics, registration year, sample
  size, geography, methodology, attribution and source fact. Invalid headers,
  missing paired metrics, duplicate identities and non-positive samples fail.
- Snapshot reconciliation removes cohorts withdrawn by a later official file
  from the current read model while retaining immutable snapshots/source facts
  as audit history.
- Migration `20260824014143_AddV33RealWorldConsumption` adds provenance-guarded
  `real_world_consumption_aggregates` with sample/year/metric checks, a unique
  dataset-version × registration-year × manufacturer × fuel identity and
  reviewed optional `BrandId` mapping.
- Car detail API and UI expose the latest registration-year cohort per fuel.
  Official Vietnam-trim consumption remains in the original trim contract;
  real-world references are a separate array with `isTrimSpecific=false`,
  sample size, methodology, attribution and original EEA source.
- The UI presents an explicit `OFFICIAL TRIM ≠ REAL-WORLD COHORT` comparison.
  It never replaces the trim figure, and an unmapped/unsupported brand gets a
  truthful empty state.

Product-safe decisions where the design leaves implementation detail open:

- The EEA aggregate is used instead of trying to derive trim measurements from
  the multi-million-row raw file. Its valid scope is manufacturer × fuel ×
  registration year across reporting EU/EEA states, not a Vietnam trim.
- Only a reviewed exact manufacturer-to-brand allowlist is linked. Ambiguous
  corporate groups such as Stellantis, PSA, SAIC and Jaguar Land Rover remain
  unmapped; no group-to-brand inference is allowed.
- The API first filters by explicit trim energy/powertrain facts (for example,
  the petrol Yaris never receives diesel or PHEV cohorts; BEVs receive no
  liquid-fuel cohort). If the fuel is unknown, it preserves labeled brand-level
  alternatives instead of guessing. For a brand/fuel it chooses the latest
  registration year and the largest official sample when multiple legal
  manufacturer entities exist.
  It does not combine or average already-aggregated cohorts.
- EEA attribution and methodology links are part of every API row. The source
  registry records the EEA legal notice/data-policy obligation. See
  `docs/adr/ADR-011-eea-real-world-cohort-scope.md`.

Gate evidence:

- Live fetch/publish: HTTP 200 official CSV, 322 cohorts, 196 exact mapped rows,
  19 mapped Vietnam catalog brands, registration years 2021–2023 and total
  sample size 6,515,134. A second publish held `322 rows / 322 IDs / 322 source
  facts`, proving idempotence.
- `python scripts/verify_v3_3_real_world.py` — PASS: immutable snapshot/source
  policy/audit/provenance, 5.95 l/100 km official Yaris Cross trim fact kept
  separate from one compatible 2023 EEA PETROL cohort reference, OpenAPI and
  rendered page.
- `dotnet build VietnamCarPlatform.sln --configuration Release` — PASS with 0
  warnings/errors; .NET tests — 76/76 PASS, including schema and deterministic
  cohort-selection policy.
- Worker tests — 77/77 PASS, including the attributed real EEA excerpt, strict
  schema/sample rejection and ambiguous manufacturer non-mapping.
- Web lint PASS; Vitest — 12 files / 19 tests PASS; production Next.js build
  PASS. OpenAPI and generated TypeScript client were regenerated.
- Migration applied to the live Compose database with no pending model change.
  A separate empty database applied all migrations through V3.3 and verified
  the new table plus four check constraints before that isolated DB was removed.
- All seven Compose services healthy. Playwright desktop/mobile QA confirmed
  the official/cohort visual hierarchy, zero console errors and no horizontal
  overflow at 390 px.

## V3.4 — PostgreSQL-first search scale — PASS

Delivered:

- Reproducible `scripts/benchmark_v3_4_search.py` creates a uniquely named,
  disposable PostgreSQL database; generates 100,000 performance-only search
  rows outside every production data path; applies production-equivalent
  trigram/facet/price/array indexes; runs five warm `EXPLAIN (ANALYZE, BUFFERS)`
  measurements per query; and force-drops the database in `finally`.
- Catalog candidate loading now stages exact phrase, all-token and strict-word
  trigram fallback. Common exact requests avoid broad fuzzy evaluation while
  typo recovery and the pre-existing honest partial-candidate semantics remain.
- Migration `20260824023048_AddV34SearchSync` adds the durable
  `published_data_events` outbox, retry lifecycle guards/routing indexes and an
  advisory-locked `process_catalog_search_events(integer)` projector function.
- Catalog seed, full-market scope, energy-profile, safe auto-publication and all
  admin create/update/delete/review/rollback/override paths append a
  `CatalogSearchSync.*` event in the same canonical-data transaction. No
  publisher refreshes the materialized search view synchronously.
- A horizontally safe API background worker coalesces pending events, refreshes
  `current_searchable_trims`, marks success or schedules bounded exponential
  retry on failure, and rotates Redis catalog cache only after success.
- The V1.2 publisher was brought forward to the V2.8 `brand_scopes` identity
  `(market, brand_id, effective_from)` and now records market/source/snapshot/
  reviewer provenance, restoring clean-bootstrap idempotence.

Measured decision:

- The final accepted 100,000-row run stayed below the 150 ms p95/query gate:
  substring 4.449 ms, typo-fuzzy 20.265 ms, faceted 0.431 ms and feature lookup
  0.504 ms. PostgreSQL used GIN trigram and B-tree price/index plans for
  the selective paths; its sub-millisecond limited feature scan was correctly
  cheaper than an index plan.
- Live Compose contains 49 reviewed searchable trims. A real publisher event
  and an isolated gate probe both completed in one attempt; probe event-to-index
  latency was 256 ms against the 10-second acceptance bound.
- These measurements do not justify Typesense or Meilisearch. Per design and
  ADR-002, neither service/dependency was added. Reconsider only after the same
  benchmark plus target-traffic API load evidence fails.

Gate evidence:

- `python scripts/verify_v3_4_search.py` — PASS: benchmark, migration/table/
  function/constraints/indexes, real catalog+energy publication events, async
  probe lifecycle, exact/typo searches, no failed queue rows and no external
  search service.
- `dotnet build VietnamCarPlatform.sln --configuration Release` — PASS with 0
  warnings/errors; .NET tests — 78/78 PASS, including outbox lifecycle and
  transactional event construction.
- Worker tests — 78/78 PASS, including the Python transactional enqueue helper.
- Web tests — 19/19 PASS with no unhandled teardown errors; lint and the
  26-route production build also PASS.
- The live migration applied successfully and the API/DB stayed healthy. A
  separate empty database applied every migration through V3.4, verified the
  table/function/two lifecycle checks, then ran the V3.4 down migration and
  verified both objects were removed before the isolated database was dropped.

## V3.5 — Versioned public/partner read API — PASS

Delivered:

- Stable `/api/v1/partner` GET surface for policy, credential metadata, brands,
  catalog search and provenance-bearing vehicle detail. Existing anonymous
  `/api/v1` behavior remains compatible; partner reads reuse the reviewed
  PostgreSQL catalog services and never call an upstream provider.
- 256-bit `vcp_v1_` key material returned only at issuance. PostgreSQL stores a
  non-secret 60-bit lookup prefix and SHA-256 hash, never plaintext. Hashes are
  compared in fixed time; optional expiry, immediate revocation and active-plan
  status are enforced on every request.
- PostgreSQL usage-plan/key lifecycle with `catalog.read` scope, exact policy
  acceptance, unique prefix/hash, coherent expiry/revocation constraints and
  auditable Administrator-only issuance/revocation. Viewer list responses expose
  lifecycle metadata only.
- Atomic Redis fixed UTC-minute/month counters across API replicas. Sandbox is
  30/minute, 10,000/month, page size 25; standard is 300/minute,
  500,000/month, page size 100. Denied calls do not consume quota and counter
  failure denies closed with `503`.
- Common `ApiError` responses plus quota/reset/retry, contract, policy, link and
  no-store headers. OpenAPI declares the `PartnerApiKey` header scheme only on
  protected operations.
- Committed source-specific reuse policy, integration guide, ADR-012, generated
  OpenAPI/TypeScript client and CI drift checks. Fact-level source URL/name/
  authority/hash/status/confidence and EEA cohort attribution remain intact.

Product-safe decisions where the design leaves implementation detail open:

- The established anonymous API was not put behind a key. A separate partner
  namespace prevents a breaking V1 change while giving integrations explicit
  lifecycle and metering controls.
- `SOURCE-SPECIFIC` is deliberately not presented as a blanket content licence.
  Normalized facts may be used with retained provenance; source prose, images,
  PDFs, maps and other assets require their own rights.
- The schema major (`v1`) and accepted data-policy version (`2026-08-24`) are
  independent. A material policy update can require key re-issuance without
  silently changing permissions or the response contract.
- PostgreSQL is the key/plan source of truth; Redis owns only atomic usage
  counters. Protected reads fail closed during a Redis outage rather than allow
  unmetered traffic.

Gate evidence:

- `python scripts/verify_v3_5_partner_api.py` — PASS: public policy, unified
  `401/403/429`, current-policy acceptance, one-time issuance, no hash/plaintext
  in list/audit, reviewed source provenance, all-GET OpenAPI surface, exact
  sandbox limit after 30 accepted calls and immediate post-revoke rejection.
- `python scripts/verify_v3_5_migration.py` — PASS on a disposable database for
  `0 → 20260824032619_AddV35PartnerApi → 20260824023048_AddV34SearchSync →
  20260824032619_AddV35PartnerApi`; both tables/plan seeds/constraints/indexes
  were verified and the database removed.
- `dotnet build VietnamCarPlatform.sln --configuration Release --no-restore`
  — PASS, 0 warnings/errors; .NET tests — 85/85 PASS. EF reports no pending
  model changes.
- Worker tests — 78/78 PASS. Web lint PASS, Vitest 12 files / 19 tests PASS with
  no unhandled teardown errors, and the 26-route production build PASS.
- OpenAPI and TypeScript generation are deterministic. SHA-256 values are
  `aa19b30560c713a9853887b170fca67921a2f6b1c460604077d72ea768d4427c`
  and `2c5365327afd79dd677e95049f90cd5549a6938c2598d231266fb0a422e840ac`.
- Production API image rebuilt and the live API/PostgreSQL/Redis path passed.
  All temporary V3.5 migration databases and roles were removed.

## V3 FINAL GATE — PASS

Acceptance evidence maps exactly to the three plan requirements:

1. **Recommendation is explainable and reproducible.** The final gate reran the
   versioned `v3.1-deterministic-1` methodology twice with identical stable
   output. Hard filters still run first, all 49 candidates have closed
   accounting, and the current honest result remains 0 ranked / 4 withheld;
   incomplete or weak-source candidates receive reasons and never receive an
   overall or P/P score.
2. **User data/privacy controls are complete.** An anonymous private request was
   rejected; registration without consent was rejected; an opted-in account
   saved profile/comparison/49 watchlist rows, exported them, then permanently
   deleted the account. Counts for account/session/profile/comparison/watchlist
   changed to `0/0/0/0/0`, and the deleted session failed immediately.
3. **Search and API pass target traffic.** ADR-013 resolves the design's missing
   RPS number as an initial single-replica target of 20 RPS for 60 seconds,
   1,200 measured real-data responses and zero errors. The mix is 9 catalog
   searches, 6 details, 1 recommendation, 2 partner searches and 2 partner
   details per second. The separate 100,000-row PostgreSQL benchmark preserves
   the target-dataset search evidence without inserting synthetic rows into the
   application database.

Final measured results:

- API load achieved 20.009 RPS with 0 HTTP/transport/payload errors. Warm-cache
  p95: catalog search 38.075 ms (`<300`), detail 39.563 ms (`<400`),
  recommendation 241.501 ms (`<700`), partner search 56.300 ms (`<300`) and
  partner detail 53.973 ms (`<400`). The 251-use standard-plan key stayed inside
  quota and was revoked after measurement.
- Isolated 100,000-row PostgreSQL p95: substring 15.464 ms, fuzzy 30.090 ms,
  faceted 0.580 ms and feature 0.340 ms, all below the 150 ms V3.4 threshold.
  Live event-to-index projection completed in 455 ms with no unfinished event.
- The final restore drill restored all 14 migrations and hash-verified 181
  immutable objects in 25.765 seconds measured local RTO. Its isolated database
  and bucket were removed; workers returned healthy.
- `python scripts/verify_v3_final.py` — PASS and confirms all seven Compose
  services healthy, V3.5 as latest migration, no V3.2 gate account, no active
  final-load key, no unfinished search event and no temporary gate database.
- Full build/regression: .NET 85/85; worker 78/78; web 12 files / 19 tests,
  lint and 26-route production build; EF model/migration parity; deterministic
  OpenAPI/generated client; Compose config/bootstrap syntax; secret scan.

V1, V2 and V3 are now complete under the supplied design and implementation
plan. Any higher traffic target, new source coverage or new product feature is
future scope and must begin with an explicit milestone/ADR rather than silently
weakening these gates.
