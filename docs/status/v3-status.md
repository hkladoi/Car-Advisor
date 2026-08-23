# V3 milestone status

Last updated: 2026-08-23

Overall: **IN PROGRESS — V3.3**

- [x] V3.1 Explainable recommendation
- [x] V3.2 Accounts, privacy, watchlists and alerts
- [ ] V3.3 Trusted real-world data
- [ ] V3.4 Search scale (only after benchmark evidence)
- [ ] V3.5 Versioned public/partner API
- [ ] V3 FINAL GATE

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

V3.3 trusted real-world data is now the only unlocked V3 milestone.
