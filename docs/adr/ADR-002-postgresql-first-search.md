# ADR-002: PostgreSQL-first search and filtering

Status: Accepted — 2026-08-22

PostgreSQL with `pg_trgm`, `unaccent`, relational constraints and denormalized current read models handles V1 search/facets. A separate search engine is allowed only after measured `EXPLAIN ANALYZE` and load evidence shows a need.

## V3.4 measured review — 2026-08-24

The decision remains PostgreSQL-first. `scripts/benchmark_v3_4_search.py`
creates a uniquely named disposable database, loads 100,000 performance-only
rows, applies the production-equivalent GIN/B-tree indexes, runs five measured
`EXPLAIN (ANALYZE, BUFFERS)` executions per query and force-drops the database
in `finally`. The accepted gate requires p95 ≤150 ms and an index/bitmap plan
for substring, typo-fuzzy and faceted queries.

The final V3.4 gate measured p95 4.449 ms substring, 20.265 ms fuzzy typo,
0.431 ms faceted and 0.504 ms feature lookup. The planner deliberately selected
a short sequential scan for the limited feature query because it was cheaper;
it used the trigram and price indexes for the other paths. These results do not justify
Typesense or Meilisearch.

Search projection refresh is no longer coupled to a publisher transaction.
Publishers atomically append `CatalogSearchSync.*` outbox events; a retryable,
advisory-locked background projector refreshes `current_searchable_trims` and
invalidates cache after success. A separate engine may be introduced later only
if this benchmark and target-traffic API load tests exceed their gates.
