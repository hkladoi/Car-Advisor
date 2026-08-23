# V3 milestone status

Last updated: 2026-08-23

Overall: **IN PROGRESS — V3.2**

- [x] V3.1 Explainable recommendation
- [ ] V3.2 Accounts, privacy, watchlists and alerts
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

V3.2 accounts/privacy is now the only unlocked V3 milestone.
