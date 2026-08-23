# V1 requirements traceability

This matrix reconciles the design document with the execution plan. “V1 complete” means the V1-owned slice has code/data evidence and an executable gate. A later-version slice is explicitly assigned rather than treated as an unfinished V1 requirement.

## Scope decisions

- The design is authoritative for domain semantics, schema and architecture; the plan is authoritative for milestone order and release gates.
- The design release strategy defines V1 as product core plus curated trustworthy data, and V2 as automated discovery/change detection/review rollback/full-market expansion. Therefore V1 does not make a full-market claim. The current coverage gate remains honestly blocked by `BRAND_SCOPE_BELOW_INITIAL_VALIDATION_TARGET` while V2.8 owns the expansion.
- FR-019 names merge/rollback in the complete admin capability, while the design release strategy assigns change detection/review queue/rollback to V2 and the V1.10 plan requires core CRUD/QA. V1 implements authenticated core CRUD, manual review/edit-publish, locks, QA and immutable audit; automated rollback and duplicate-merge workflows are owned by V2 data operations.
- FR-021 is split exactly as the design traceability appendix states: V1 owns the dealer/branch/structured-offer model, CRUD, validation, expiry and eligibility; V2 owns recurring discovery/monitoring for supported official dealer sources.
- No official dealer-offer or bank-rate snapshot is present in the accepted V1 source set. V1 renders the explicit empty state and `UserInput` rate origin rather than publishing synthetic offers or rates.

## Functional requirements

| Requirement | Release owner | V1 implementation/evidence | Status |
| --- | --- | --- | --- |
| FR-001 Catalog identity/status | V1 schema/catalog; V2.8 market expansion | Trim-first brand/model/generation/model-year schema, published catalog and coverage state; `verify_v1_3_catalog.py` | V1 complete; expansion assigned V2.8 |
| FR-002 Search | V1.3 | Diacritic/`đ` normalization, aliases, PostgreSQL trigram/fuzzy search and controlled token matching | Complete |
| FR-003 Filter | V1.3–V1.4 | Shareable filters for identity, body/segment/powertrain/seats/price/on-road/dimensions/features/colors/range/battery with AND/OR semantics | Complete |
| FR-004 Detail | V1.4; V2 automation enrichment | Trim switch, price/effectivity, grouped facts, equipment, colors, warranty, rights-gated gallery, dealer-offer panel and source disclosure | V1 complete |
| FR-005 Pricing | V1.5; V2 history/automation | Effective-dated MSRP/promotion/expected/unannounced/dealer types, structured offers and history-capable schema; pricing/on-road gate | V1 complete; history UX assigned V2.7 |
| FR-006 On-road | V1.5 | Region/date/vehicle/buyer rule evaluation, offer eligibility and sourced breakdown; `verify_v1_5_onroad.py` | Complete |
| FR-007 Fuel energy | V1.6; V2 monitoring | Official current/history energy rows, fuel type and condition-specific consumption; `verify_v1_6_energy.py` | V1 complete |
| FR-008 Home energy | V1.6 | EVN marginal tiers, explicit custom/rental input and charging loss | Complete |
| FR-009 Public energy | V1.6; V2.6 map adapter | Provider tariffs, connector/power, post-charge fees and conditional/free-charging promotion | V1 complete; maps assigned V2.6 |
| FR-010 PHEV/EREV | V1.6 | Separate EV-share, charge-depleting electric and charge-sustaining fuel legs | Complete |
| FR-011 Ownership | V1.7 | Energy, parking, insurance, maintenance, legal recurring cost and tyre reserve with current/normalized/worst bands | Complete |
| FR-012 Salary filter | V1.7 | Quick/Advanced profiles, eligible/excluded/data-insufficient groups and reason codes; `verify_v1_7_affordability.py` | Complete |
| FR-013 Financing | V1.8 | Cash/family/trade-in/down payment/loan/rate/term/annuity/reducing balance, upfront/monthly/interest and separate ownership gate | Complete |
| FR-014 Compare | V1.9 | 2–4 trims, common region/profile, canonical units, differences-only and non-sensitive share URL; `verify_v1_9_compare.py` | Complete |
| FR-015 Recommendation | V3.1 | No opaque score added in V1 | Assigned V3.1 |
| FR-016 Sources | V1 provenance; V2.7 history UX | Field-level source fact, snapshot URL/time/hash, status/confidence and versioned records on important facts | V1 complete |
| FR-017 Images | V1.4 rights gate; V2 enrichment | Only Owned/Licensed/OfficialPressKit/Permitted records render; honest empty state otherwise | V1 complete |
| FR-018 History | V1 versioned storage; V2.7 UX | Effective-dated/history-capable price/rule/energy/offer schema | V1 storage complete; charts assigned V2.7 |
| FR-019 Admin | V1.10 core; V2 automated rollback/merge | RBAC auth, trim/source/dealer/branch/offer CRUD, import validation, review/edit-publish, override/lock, QA, audit; `verify_v1_10_admin.py` | V1 core complete; automated merge/rollback assigned V2 |
| FR-020 Ingestion | V1 known-URL curated pipeline; V2.1–V2.5 automation | Allowlisted fetch, SSRF guard, immutable snapshot-before-parse, validation, transactional publish and hash skip | V1 complete; Brave/daily automation assigned V2 |
| FR-021 Dealer | V1 model/QA; V2.5 automation | Separate dealer/branch entities, structured cash/non-cash benefits, conditions, exclusivity, expiry, provenance and public eligibility | V1 complete |
| FR-022 Coverage | V1.10 dashboard; V2.8 full market | Discovered/mapped/published/blocked/stale, completeness/freshness and reproducible gate failures | V1 dashboard complete; full-market pass assigned V2.8 |
| FR-023 Map/charging | Optional V2.6 | No map dependency on V1 runtime; provider tariff remains authoritative | Assigned V2.6 |
| FR-024 Watchlist | V3.2 | Not introduced in V1 | Assigned V3.2 |
| FR-025 Account/privacy | V3.2 | V1 calculators are anonymous and do not persist profiles | V1 privacy complete; accounts assigned V3.2 |
| FR-026 Unknown semantics | V1 | Official/Expected/Unknown/NotAvailable/NotApplicable remain distinct in schema, API, UI and comparison | Complete |
| FR-027 Region | V1.5 | 34-province local snapshot; one region/date shared by on-road, ownership, financing and compare | Complete |
| FR-028 API | V1 | Versioned REST, deterministic OpenAPI and generated TypeScript client | Complete |
| FR-029 Audit | V1.10; V2 rollback audit | Admin/session/publish/override/reject operations record actor, reason, timestamp and before/after | V1 complete |
| FR-030 Data quality | V1.10 | Impossible values, duplicate identities, stale/missing/conflicting sources and dealer-offer issues | Complete |

## Non-functional requirements

| Requirement | V1 evidence | Status |
| --- | --- | --- |
| NFR-01 Availability | Catalog service reads PostgreSQL/materialized view plus bounded Redis cache only. `verify_v1_final.py` stops worker/scheduler and proves a new API/SSR catalog request still works. | Complete |
| NFR-02 Performance | V1.3 asserts warm catalog p95 <300 ms; final gate asserts warm detail p95 <400 ms and compare p95 <700 ms on the release dataset. | Complete |
| NFR-03 Consistency | Effective dates, source/rule versions and calculation date/profile are returned; V1.5–V1.9 golden recomputation tests are deterministic. | Complete |
| NFR-04 Traceability | Important price/spec/rule/rate/offer fields resolve through SourceFact → immutable snapshot → source URL/hash. | Complete |
| NFR-05 Security | PBKDF2 admin auth, RBAC, hashed rotating sessions, login and per-IP heavy-endpoint limits, crawler SSRF/private-network guard, HttpOnly same-origin BFF; CI Gitleaks and Trivy gates. | Complete |
| NFR-06 Privacy | Anonymous calculators; financing inputs POSTed and not persisted or placed in URL; compare URL contains public presets only. | Complete |
| NFR-07 Maintainability | Modular boundaries, six EF migrations, clean/pending-model gate, deterministic OpenAPI/client generation and three-language test suites. | Complete |
| NFR-08 Observability | JSON logs/correlation IDs, OpenTelemetry/Sentry hooks, live/ready health, worker structured events and admin coverage/quality dashboards; `infra/monitoring/README.md`. | Complete |
| NFR-09 Recovery | Versioned object bucket plus isolated PostgreSQL/object restore drill with 47 SHA-256 verified objects and measured 14.672 s local RTO; `scripts/backup_restore_test.py`. | Complete |
| NFR-10 Accessibility | Labelled/keyboard-usable controls, semantic tables/states, responsive 320/768/1280 browser gates with no page overflow and zero console errors. | V1 basic WCAG target complete |
| NFR-11 SEO | SSR catalog/detail, catalog and dynamic trim metadata, Vehicle JSON-LD, admin/calculator `noindex`. | Complete |
| NFR-12 Cost control | No paid external API is required/enabled in V1 normal paths; known URL first, bounded worker fetch, immutable hash skip and Brave discovery-only. V2 owns non-zero external spend dashboards. | V1 complete; V2 budget owner assigned |

## Plan milestones and final gate ownership

| Plan gate | Executable evidence | Status |
| --- | --- | --- |
| V1.0 repository/infrastructure | Compose/config/health, multi-language build/test, OpenAPI | Passed |
| V1.1 schema/migrations | `verify-v1.1-schema.sql`, isolated migration up/down and EF pending-model check | Passed |
| V1.2 registry/seed | registry/seed validate → real fetch → immutable snapshots → transactional publish → `verify-v1.2-seed.sql` | Passed |
| V1.3 catalog | `verify_v1_3_catalog.py` | Passed |
| V1.4 web detail | `verify_v1_4_web.py` plus Playwright responsive/console evidence | Passed |
| V1.5 on-road | `verify_v1_5_onroad.py` | Passed |
| V1.6 energy | `verify_v1_6_energy.py` | Passed |
| V1.7 ownership/salary | `verify_v1_7_affordability.py` | Passed |
| V1.8 financing | `verify_v1_8_financing.py` | Passed |
| V1.9 compare | `verify_v1_9_compare.py` | Passed |
| V1.10 admin/data QA | `verify_v1_10_admin.py` | Passed |
| V1 final E2E/goldens/external independence | `verify_v1_final.py` | Passed |
| Backup/restore | `backup_restore_test.py`, `verify_restore_objects.py`, measured report | Passed |
| Add brand/trim/dealer/source documentation | `docs/runbooks/onboard-brand-trim-dealer-source.md` | Complete |

Every V1-owned design requirement and every V1 plan gate has an implementation owner and evidence above. Items not owned by V1 are assigned to a named V2/V3 milestone; none is silently dropped.
