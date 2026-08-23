# PLAN V1 / V2 / V3 — Vietnam Car Platform — Engineering Execution v2

**Tài liệu thiết kế đi kèm:** `thiet-ke-project-vietnam-car-platform-full-v3.docx`  
**Ngày:** 22/08/2026  
**Nguyên tắc:** design doc là source of truth về kiến trúc/nghiệp vụ; file này là source of truth về thứ tự triển khai và gate.

---

## A. Stack kỹ thuật phải dựng trước khi code feature

- **Web:** Next.js App Router + TypeScript + Tailwind CSS + shadcn/ui + TanStack Query + React Hook Form + Zod.
- **API:** ASP.NET Core (.NET LTS) + EF Core + OpenAPI.
- **Database:** PostgreSQL + `pg_trgm` + `unaccent`; PostGIS chỉ bật khi charging map cần.
- **Cache/queue:** Redis.
- **Ingestion:** Python + httpx + selectolax/BeautifulSoup + Playwright fallback + Pydantic.
- **Object storage:** MinIO local, Cloudflare R2/S3 production.
- **Discovery:** Brave Search API, server-side/worker only.
- **CI/CD:** GitHub Actions + GHCR.
- **Observability:** structured logs + OpenTelemetry + Sentry; metrics dashboard cho crawler/data freshness.

### A.1 Architecture guardrails

- [ ] Frontend không hard-code công thức on-road/energy/affordability/financing.
- [ ] User request path không gọi Brave/Playwright.
- [ ] Published data và candidate ingestion tách trạng thái.
- [ ] Redis không là source of truth.
- [ ] High-risk changes không auto-publish ngoài policy.
- [ ] Mọi external API có timeout/retry/rate-limit/budget guard.
- [ ] UNKNOWN không render thành false/no.
- [ ] Mọi calculator trả assumptions + applied rule/source IDs.

---

## B. Repository target

```text
apps/web
apps/api
workers/ingestion
packages/contracts
packages/taxonomy
data/seeds
data/imports
data/fixtures
infra/docker
infra/compose
infra/monitoring
docs/architecture
docs/adr
docs/runbooks
scripts
.github/workflows
```

### B.1 Mandatory engineering files

- [ ] Root `README.md`: local start, architecture overview, troubleshooting.
- [ ] `.env.example`: tất cả biến môi trường, không chứa secret.
- [ ] `docker-compose.yml`: web/api/postgres/redis/minio/worker/scheduler.
- [ ] `docs/adr/`: ADR-001..ADR-010 theo design doc.
- [ ] `docs/runbooks/`: parser-failure, stale-energy, legal-rule-change, restore.
- [ ] `docs/data-sources/`: source registry và parser ownership.
- [ ] OpenAPI generated client cho web.

---

## C. Definition of Done áp dụng cho mọi milestone

Một task user-facing chỉ được tick Done khi:

- [ ] Domain/schema đã có.
- [ ] API contract đã có.
- [ ] UI đã nối API nếu applicable.
- [ ] Unit/integration/E2E hoặc golden tests phù hợp đã có.
- [ ] Source/provenance/unknown semantics được xử lý.
- [ ] Logging/metrics/error handling tối thiểu đã có.
- [ ] Migration/seed/update docs đã cập nhật.
- [ ] Không còn TODO blocking hoặc mock data ở path production nếu đã có nguồn thật.

---

## 0. Nguyên tắc thực thi

1. **Trim-first:** không thiết kế schema chỉ theo model.
2. **Source-first:** field quan trọng phải có `source_id`, `verified_at`, `confidence`.
3. **Versioned:** giá, promotion, fuel/electricity/charging tariff, registration rule đều có `effective_from/effective_to`.
4. **Unknown != false:** chưa có dữ liệu không được hiển thị thành “không có”.
5. **User search DB, crawler search Web:** Brave không nằm trong request path của người dùng.
6. **High-risk changes need review:** giá, ADAS, trim status, luật/phí.
7. **Current vs normalized ownership cost:** luôn tách chi phí có ưu đãi tạm thời và chi phí sau ưu đãi.
8. **Purchase != ownership:** “nuôi được” và “mua/vay được” là hai trục riêng; hỗ trợ family-funded/cash/trade-in/loan.
9. **Dealer offer is structured:** tách cash/non-cash/fee support/trade-in/financing benefit và exclusivity; không cộng quà thành giá tiền mặt.
10. **Coverage is measurable:** full-market chỉ được tuyên bố khi BrandScope/ACTIVE trims pass coverage gate.

---

# V1 — Product Core + Trustworthy Data Foundation

## V1 Goal

Có một product chạy end-to-end với catalog theo trim, filter/search, detail, compare, giá ra biển, chi phí năng lượng và filter theo lương; dữ liệu seed đủ sâu ở một nhóm hãng để chứng minh kiến trúc.

## V1.0 — Repository & infrastructure

- [ ] Tạo monorepo theo cấu trúc trong design doc.
- [ ] `apps/web`: Next.js + TypeScript + Tailwind + shadcn/ui.
- [ ] `apps/api`: ASP.NET Core Web API.
- [ ] `workers/ingestion`: Python.
- [ ] PostgreSQL + Redis bằng Docker Compose.
- [ ] MinIO/R2 abstraction cho snapshots/assets permitted.
- [ ] `.env.example`, secret handling, local bootstrap script.
- [ ] GitHub Actions: lint, unit test, build, integration test.
- [ ] OpenAPI generation từ ASP.NET -> TS client.
- [ ] Logging structured + correlation id.

### Gate V1.0

- `docker compose up` dựng được web/api/db/redis/worker.
- API health/readiness pass.
- CI green trên clean clone.

---

## V1.1 — Domain schema & migrations

### Entities

- [ ] Brand / BrandScope
- [ ] Model / aliases
- [ ] Generation / ModelYear
- [ ] Trim / aliases / market status
- [ ] SpecDefinition / TrimSpec
- [ ] FeatureDefinition / TrimFeature
- [ ] Color / TrimColor
- [ ] VehicleImage + rights_status
- [ ] Price / PriceHistory
- [ ] Promotion
- [ ] Dealer / DealerBranch / DealerOffer + benefit taxonomy/exclusivity
- [ ] AffordabilityProfile / FinancingScenario (optional persistence)
- [ ] Source / SourceSnapshot
- [ ] RegistrationRule
- [ ] EnergyPrice / ChargingProvider / ChargingTariff / ChargingPromotion
- [ ] DataChange / AuditEvent

### Taxonomy

- [ ] Powertrain: ICE / HEV / PHEV / EREV / BEV.
- [ ] Body type + segment.
- [ ] ADAS canonical feature codes: ACC, AEB, FCW, LKA, LCC/LFA, BSD, RCTA, TSR...
- [ ] Convenience: remote start, remote climate, app control, HUD, 360 camera, ventilated/heated seats, seat memory, panoramic roof...
- [ ] Numeric specs có canonical unit và conversion.

### Gate V1.1

- Migration up/down chạy được.
- Unit test unique constraints, effective-date rules, `UNKNOWN/NULL` semantics.

---

## V1.2 — Source registry & seed pipeline

- [ ] Tạo registry official domain cho từng brand/NPP.
- [ ] Nguồn chính phủ/EVN/MOIT/DMS/V-Green.
- [ ] Manual import JSON/CSV có validation.
- [ ] Python known-URL fetcher + content hash.
- [ ] Save source metadata: URL/domain/authority/fetched_at/hash.
- [ ] Không auto-write từ Brave snippet.

### Initial brand batch

Seed trước 10–15 hãng có volume/interest cao để test product:

- [ ] VinFast
- [ ] Toyota
- [ ] Hyundai
- [ ] Kia
- [ ] Mazda
- [ ] Ford
- [ ] Honda
- [ ] Mitsubishi
- [ ] Geely
- [ ] Omoda & Jaecoo
- [ ] BYD
- [ ] Lynk & Co
- [ ] MG
- [ ] Volkswagen / Skoda (nếu resource cho phép)
- [ ] Một premium brand để test schema premium options (BMW/Mercedes/Porsche)

**Không yêu cầu 100% toàn thị trường trước khi validate schema.** Sau khi pipeline ổn mới mở rộng batch.

### Gate V1.2

- Mỗi seeded trim có source cho MSRP hoặc trạng thái `UNANNOUNCED`.
- ≥90% fields “core” của seeded trims có source/confidence hoặc UNKNOWN minh bạch.
- Không duplicate model/trim rõ ràng.

---

## V1.3 — Catalog/search/filter API

- [ ] `GET /brands`
- [ ] `GET /cars` với pagination/sort/facets.
- [ ] Search normalization có dấu/không dấu + aliases + `pg_trgm`.
- [ ] Filter brand/model/body/segment/powertrain/seats.
- [ ] Filter MSRP/current price/on-road range.
- [ ] Filter dimensions/range/battery/consumption.
- [ ] Filter canonical features & colors.
- [ ] Materialized/read view cho current searchable trim.
- [ ] Redis cache facets/current data.

### Gate V1.3

- p95 catalog < 300ms cache warm trên dataset seed.
- Query “ex5”, “vf6”, “tucson hybrid” trả đúng candidates.
- Multiple feature filters dùng AND/OR rõ ràng và test được.

---

## V1.4 — Web UI catalog & detail

- [ ] Home/discover.
- [ ] Catalog desktop sidebar + mobile filter drawer.
- [ ] Vehicle card: image, trim, powertrain, MSRP/promo, on-road region, monthly cost summary.
- [ ] Detail: trim switch, MSRP/promo/dealer offers, source badge, gallery, specs, features, colors, warranty fields.
- [ ] Dealer offer panel: cash discount vs non-cash benefits, branch/region, conditions, expiry.
- [ ] Unknown/unannounced/expected price states.
- [ ] Region selector persisted locally.
- [ ] Source detail popover/modal.
- [ ] URL/filter state shareable.

### Gate V1.4

- E2E catalog -> filter -> detail.
- No field with null renders as false/no.
- Images only from approved rights status.

---

## V1.5 — Pricing & on-road engine

- [ ] RegistrationRule evaluator: fixed/percent/tiered + condition tree.
- [ ] Effective date lookup.
- [ ] Province/area mapping independent from UI name.
- [ ] Import Province Open API v2 snapshot into local DB.
- [ ] Seed current 2026 plate fee rules.
- [ ] Seed BEV first-registration-tax current rule and future-effective rule from 01/03/2027.
- [ ] Breakdown response with source for each component.
- [ ] Promotion price eligibility hooks.
- [ ] Dealer offer evaluator: benefit components, region/branch, compatibility/exclusivity, cash vs non-cash.
- [ ] `EffectiveCashPurchasePrice` separate from total gift/value headline.

### Golden tests

- [ ] Hà Nội/Khu vực I ≤9 seat plate fee current date.
- [ ] Khu vực II plate fee current date.
- [ ] BEV calculation on 22/08/2026.
- [ ] BEV calculation on 01/03/2027 applies future rule.

### Gate V1.5

- No fee/tax hard-coded in frontend.
- Changing one rule row changes output without redeploy code.

---

## V1.6 — Energy Cost Engine

### Fuel

- [ ] Fuel types + price history.
- [ ] Seed current E5RON92 / E10RON95-III / diesel.
- [ ] Official fuel price fetcher from MOIT/DMS known source.
- [ ] Daily stale/change job.

### Home charging

- [ ] Simple custom VND/kWh.
- [ ] EVN six-tier tariff evaluator.
- [ ] Base household consumption + marginal EV charge calculation.
- [ ] Rental/custom fixed electricity mode.
- [ ] Charging efficiency/loss.

### Public charging

- [ ] Provider/tariff model.
- [ ] Seed V-Green current tariff.
- [ ] Session/overstay additional fee model.
- [ ] Promotion eligibility with session/month cap.
- [ ] Seed current VinFast free charging policy as versioned promotion.

### PHEV

- [ ] EV share input.
- [ ] Home/public electric mix.
- [ ] Fuel + electricity combination.

### Gate V1.6

- Unit/golden tests for ICE, BEV home, BEV public, free charging, PHEV mixed usage.
- Output contains `currentCost` and `normalizedCost`.

---

## V1.7 — Ownership affordability / salary filter

### Quick mode

- [ ] Net salary.
- [ ] Monthly km default/profile.
- [ ] Conservative/Balanced/Aggressive.
- [ ] Default essential expense assumptions visible/editable.

### Advanced mode

- [ ] Rent + essential expenses.
- [ ] Parking cost.
- [ ] Home/public charging ratio + custom electricity.
- [ ] PHEV EV share.
- [ ] Insurance/maintenance/tyre/road reserves.
- [ ] Savings target / max monthly vehicle spend.

### Calculation

- [ ] `OperatingOwnershipCost` excludes loan payment.
- [ ] `IncomeRatio` + `DisposableRatio`.
- [ ] current / normalized / worst-reasonable bands.
- [ ] Explain why a car was excluded.

### Gate V1.7

- Same salary with different rent/parking/charging produces different eligible cars.
- Removing VinFast charging promotion increases normalized cost rather than silently keeping 0.
- UI labels calculation as estimate, not financial advice.

---

## V1.8 — Purchase & Financing Affordability

### Inputs

- [ ] Cash available.
- [ ] Family contribution / external funding.
- [ ] Trade-in net value.
- [ ] Cash purchase mode.
- [ ] Down payment % or amount.
- [ ] Annual interest rate + term months.
- [ ] Payment type: reducing balance / annuity.
- [ ] Existing monthly debt.
- [ ] Financing/trade-in bonuses and eligibility conditions.
- [ ] Optional bank/dealer official rate snapshot with source/as-of; user input remains supported.

### Calculator

- [ ] Upfront cash need.
- [ ] Loan principal.
- [ ] First-month / average or annuity payment.
- [ ] Total interest + total repayment.
- [ ] `VehicleDebtRatio` / `TotalCommitmentRatio` / post-payment disposable.
- [ ] `TotalMonthlyVehicleCommitment = OperatingOwnershipCost + FinancingPayment`.
- [ ] `EXTERNALLY_FUNDED/NOT_APPLICABLE` purchase status when family buys vehicle outright.

### Gate V1.8

- Family-funded purchase skips user cash/loan gate but ownership affordability still runs.
- Cash purchase requires enough upfront cash and has zero financing payment.
- Reducing-balance and annuity golden tests match known formulas.
- Dealer financing bonus cannot be applied when its financing condition is false.

---

## V1.9 — Compare

- [ ] 2–4 trims.
- [ ] Sticky trim headers.
- [ ] Same region/ownership profile/financing scenario applied to all cars.
- [ ] Difference-only mode.
- [ ] Unknown state visible.
- [ ] Shareable compare URL.
- [ ] Compare MSRP/promo/dealer cash price, on-road, upfront cash, installment and operating cost.

### Gate V1.9

- E2E compare from catalog.
- Values use canonical units.
- Price/on-road/operating/financing results recompute when profile changes.

---

## V1.10 — Admin & data QA

- [ ] Admin auth.
- [ ] CRUD core entities.
- [ ] Source registry.
- [ ] Manual import + validation report.
- [ ] Field lock/manual override + reason.
- [ ] Dealer-offer QA: expiry, duplicate benefits, exclusivity conflicts, region/branch mismatch.
- [ ] Coverage dashboard: discovered/mapped/published/blocked/stale + completeness.
- [ ] Data quality checks: impossible values, duplicates, stale sources, missing core fields.
- [ ] Audit log.

### V1 FINAL GATE

- [ ] End-to-end catalog -> detail -> offer -> on-road -> ownership -> financing -> purchase filter -> compare works.
- [ ] Seeded brands have trustworthy trim data and sources.
- [ ] On-road/energy/ownership/financing golden tests green.
- [ ] No external API required for normal catalog page request.
- [ ] Backup/restore tested.
- [ ] Documentation for adding one new brand/trim/dealer/source.
- [ ] Requirements traceability checklist has no unowned V1 requirement.

---

# V2 — Automated Data Operations

## V2 Goal

Từ product có data curated chuyển sang platform có thể tự phát hiện/cập nhật phần lớn thay đổi, nhưng vẫn có human review khi rủi ro cao.

## V2.1 Brave discovery

- [ ] Brave Search client server-side.
- [ ] Monthly query budget & spend guard.
- [ ] Search templates per brand/data type.
- [ ] Known URL first; Brave only when discovery needed.
- [ ] Deduplicate results/domains/URLs.
- [ ] Do not persist snippets as facts.

**Budget target:** tận dụng $5 free monthly credits trước; ở pricing hiện tại tương đương khoảng 1.000 Search requests/tháng nếu không vượt credit.

## V2.2 Domain parsers

- [ ] Parser registry per domain.
- [ ] HTTP parser first.
- [ ] Playwright fallback only for JS pages.
- [ ] PDF/brochure metadata + extraction workflow.
- [ ] Parser fixtures checked into `data/fixtures` when legally permissible.
- [ ] Content hash -> skip unchanged content.

## V2.3 Structured extraction

- [ ] Deterministic extraction first.
- [ ] Optional local LLM structured extraction behind JSON schema for difficult pages.
- [ ] Unit normalization.
- [ ] Model/trim entity resolution.
- [ ] Confidence calculation.

## V2.4 Change detection & review queue

- [ ] Old/new diff.
- [ ] Anomaly thresholds.
- [ ] Auto-publish safe changes.
- [ ] Queue high-risk changes.
- [ ] Admin source snapshot side-by-side.
- [ ] Approve/reject/edit.
- [ ] Rollback published version.

## V2.5 Automated monitoring

- [ ] Daily manufacturer price/promotion jobs.
- [ ] Daily dealer-offer jobs for supported official dealer sources.
- [ ] Optional official bank/dealer finance campaign watch; store as reference/as-of, never approval promise.
- [ ] Daily fuel/legal/charging watch.
- [ ] Weekly specs/features/images.
- [ ] New model discovery.
- [ ] Stale source alerts.
- [ ] Parser failure alerts.

## V2.6 Charging/map data

- [ ] Open Charge Map API adapter.
- [ ] Coverage/confidence flag.
- [ ] Goong adapter for optional geocode/map.
- [ ] Keep charging tariff authoritative from provider, not OCM.

## V2.7 Price / offer history UX

- [ ] Timeline MSRP/manufacturer promotion/dealer cash offers.
- [ ] “Current low vs 12-month range” only when enough history.
- [ ] Fuel/electricity price history.
- [ ] Ownership cost recompute historically optional.

## V2.8 Full-market coverage completion

- [ ] Build authoritative `BrandScope` list for included Vietnam new-car brands; Porsche included, configured supercar exclusions excluded.
- [ ] Discover every official ACTIVE/COMING_SOON model candidate from brand/NPP listings.
- [ ] Resolve every candidate to `PUBLISHED` or `BLOCKED_WITH_REASON` — no silent drop.
- [ ] 100% ACTIVE trim candidates have a trim record/status even when price/spec is UNKNOWN.
- [ ] Core completeness target per published trim (e.g. ≥90%) or explicit source gap.
- [ ] Freshness SLA for price/promo/dealer offer/energy/legal data.
- [ ] Public/admin coverage report reproducible from DB.

## V2 FINAL GATE

- [ ] ≥80% recurring price/promo/dealer-offer updates for supported sources detected without manual search.
- [ ] No high-risk change auto-published outside policy.
- [ ] Full Vietnam BrandScope passes Full-market coverage gate.
- [ ] Freshness/coverage dashboard visible and has no unexplained candidate gaps.
- [ ] Failed parser does not corrupt current published data.

---

# V3 — Recommendation, Personalization & Scale

## V3.1 Explainable recommendation

- [ ] Ranking based on hard filters first.
- [ ] Configurable weights: price/value, running cost, space, safety/ADAS, comfort, performance, tech.
- [ ] Score components visible; no opaque “AI score”.
- [ ] P/P score only after data completeness threshold is met.

## V3.2 User accounts (opt-in)

- [ ] Save region/profile.
- [ ] Saved comparisons.
- [ ] Watchlist.
- [ ] Price/promotion/dealer-offer alerts.
- [ ] Data export/delete.

## V3.3 Real-world data

- [ ] Add licensed/reliable real consumption if a trustworthy source is available.
- [ ] Separate official vs real-world metrics.
- [ ] Aggregate only with methodology and sample-size visibility.

## V3.4 Search scale

- [ ] Benchmark PostgreSQL first.
- [ ] Add Typesense/Meilisearch only if measured need.
- [ ] Async index synchronization from published data events.

## V3.5 Public/partner API

- [ ] Read-only API keys.
- [ ] Usage plans/rate limits.
- [ ] Data attribution/licensing policy.
- [ ] Stable versioned contracts.

## V3 FINAL GATE

- [ ] Recommendation is explainable and reproducible.
- [ ] User data/privacy controls complete.
- [ ] Search and API load tested at target traffic.

---

# Functional coverage checklist

| Yêu cầu | V1/V2/V3 owner |
|---|---|
| Toàn bộ xe + mọi trim đang bán ở VN | V1 schema + V2.8 full-market gate |
| Hãng/màu/giá/tính năng/ADAS/remote filter | V1.3/V1.4 |
| Salary filter “nuôi được” | V1.7 |
| Cash/loan/family/trade-in “mua/vay được” | V1.8 |
| MSRP/promo/expected/unannounced | V1.5 |
| Dealer offer có cấu trúc | V1.5 + V2.5 |
| Giá ra biển theo region/effective date | V1.5 |
| Xăng/dầu cập nhật | V1.6/V2.5 |
| Sạc nhà EVN/custom/nhà trọ | V1.6 |
| Sạc public/provider/free charging | V1.6 + V2.6 |
| Charging loss + PHEV EV share | V1.6 |
| Current/normalized/worst cost | V1.6/V1.7 |
| Compare trim | V1.9 |
| Ảnh đúng trim/màu/rights | V1.4 + V2.5 |
| Source/confidence/history | V1 + V2.7 |
| Brave auto-update | V2.1–V2.5 |
| Recommendation/P-P | V3.1 |
| Watchlist/alerts | V3.2 |

**Traceability rule:** không code một chức năng mới nếu chưa có requirement/module/schema-API/task/gate tương ứng trong design + plan.

---

# External API / source integration matrix

| Integration | Use | V1 | V2 | Runtime dependency? |
|---|---|---:|---:|---|
| Province Open API v2 | 34 tỉnh/thành + ward data | ✅ snapshot | ✅ refresh | No |
| Brave Search API | discovery nguồn mới | optional/manual | ✅ | No |
| Open Charge Map | charging locations | — | ✅ | No, cached |
| Goong | map/geocode | optional | optional | No |
| NHTSA vPIC/APIs | VIN/make/model/safety supplemental | optional | optional | No |
| MOIT/DMS | fuel official source | ✅ crawler | ✅ monitor | No, cached |
| EVN/MOIT | electricity official source | ✅ seed/crawler | ✅ monitor | No, cached |
| V-Green | public charging tariff | ✅ seed/crawler | ✅ monitor | No, cached |
| Manufacturer/NPP sites | vehicle truth source | ✅ | ✅ | No, cached DB |

---

# Scheduler defaults

```yaml
jobs:
  vehicle_price_promotion: daily
  dealer_offers: daily
  finance_campaign_reference: daily_watch
  new_model_discovery: daily
  fuel_price: daily
  electricity_tariff: daily_watch
  charging_tariff_promotion: daily
  registration_legal_rules: daily_watch
  vehicle_specs_features: weekly
  vehicle_images_colors: weekly
  source_staleness_check: daily
```

---

# Data source acceptance rules

```text
OFFICIAL_GOV > OFFICIAL_MANUFACTURER > OFFICIAL_DISTRIBUTOR > OFFICIAL_DEALER > TRUSTED_PRESS > OTHER
```

Exceptions:

- Legal/tax/fee: only Government/competent authority should be authoritative.
- Promotion: manufacturer/distributor policy wins over press.
- Dealer offer: official dealer/branch wins for that dealer/region; keep benefit components and exclusivity.
- Loan/rate: official bank/dealer campaign is a dated reference only; user-entered scenario controls personal calculation.
- Specs: official brochure/product page wins; press can only fill provisional gaps with lower confidence.
- Charging tariff: provider source wins; Open Charge Map is location/reference data only.

---

# Current test fixtures (as of 22/08/2026 — NEVER hard-code)

Use as golden-test seed with effective dates and sources:

- 34 provincial-level administrative units after 2025 reorganization.
- Plate fee from 01/01/2026: ≤9-seat cars — Area I 14,000,000 VND; Area II 140,000 VND.
- BEV first registration tax: 0% through 28/02/2027 under current rule; NĐ 202/2026 effective 01/03/2027 continues 0% through 31/12/2030.
- Fuel period 20/08/2026: E5RON92 21,833; E10RON95-III 22,668; diesel 28,543 VND/litre.
- Household electricity tariff from 10/05/2025: 1,984 / 2,050 / 2,380 / 2,998 / 3,350 / 3,460 VND/kWh before VAT by six tiers.
- V-Green public tariff: 3,858 VND/kWh incl. VAT plus applicable post-charge service fees.
- VinFast personal customers buying from 10/02/2026: free first 10 charging sessions/car/month at V-Green through 10/02/2029, subject to policy conditions.

---

# Recommended implementation order for Codex

1. Repo + Docker + CI.
2. Domain schema + migrations + taxonomy.
3. Seed/source tooling.
4. Catalog/search/filter API.
5. Web catalog/detail.
6. Pricing/on-road engine.
7. Energy engine.
8. Ownership affordability engine.
9. Purchase/financing engine.
10. Compare.
11. Admin/data QA + coverage dashboard.
12. Final V1 gate.
13. Only then implement Brave automation/V2 and full-market completion.

**Rule for executor:** Do not jump to recommendation AI or complex crawler until V1 golden tests and data model are stable.


---

# D. Cross-cutting execution gates bổ sung

## D.1 Database gate
- [ ] Mọi bảng effective-dated có overlap tests.
- [ ] Published rows có source fact hoặc manual override reason.
- [ ] Indexes cho catalog/search được benchmark bằng `EXPLAIN ANALYZE`.
- [ ] Migration up/down hoặc documented irreversible migration.
- [ ] Seed idempotent.

## D.2 API gate
- [ ] OpenAPI generation deterministic.
- [ ] Error model thống nhất `code/message/fieldErrors/traceId`.
- [ ] Calculator responses có `assumptions`, `appliedRules`, `warnings`, `calculatedAt`.
- [ ] Rate limits cho anonymous heavy endpoints.
- [ ] No N+1 query trên catalog/detail/compare.

## D.3 Frontend gate
- [ ] Filter state shareable URL.
- [ ] Desktop + mobile responsive.
- [ ] Loading/error/empty/unknown states.
- [ ] Keyboard usable cho filter/compare.
- [ ] Không expose API keys.
- [ ] SEO metadata cho public brand/model/trim pages.

## D.4 Worker gate
- [ ] Fetch timeout + retry bounded.
- [ ] SSRF/private-network guard.
- [ ] Snapshot trước parse.
- [ ] Parser fixture + regression test.
- [ ] Hash unchanged → skip extract.
- [ ] Failure không mutate published data.
- [ ] Per-source freshness SLA + alert.

## D.5 Security gate
- [ ] Admin auth/RBAC.
- [ ] Secret scanning.
- [ ] Dependency/container vulnerability scan.
- [ ] Sanitization crawled text.
- [ ] Audit log cho admin publish/override/rollback.

## D.6 Operations gate
- [ ] Backup configured.
- [ ] Restore drill chạy trên staging.
- [ ] API/crawler/data-quality dashboards.
- [ ] Alert cho stale fuel/charging/legal data.
- [ ] External API spend dashboard/guard.

---

# E. Codex execution protocol

1. Đọc toàn bộ design doc v3 trước khi code.
2. Làm theo đúng thứ tự V1.x → gate → milestone tiếp theo.
3. Nếu phát hiện design thiếu/mâu thuẫn, ghi ADR/issue trước khi thay kiến trúc.
4. Không bỏ task vì “chưa cần” nếu task nằm trong gate của version đang làm.
5. Sau mỗi milestone chạy build + relevant tests + migrations + smoke.
6. Cập nhật checklist và `docs/status/v1-status.md` (sau này V2/V3 tương ứng).
7. Chỉ chuyển version khi FINAL GATE version trước pass.
