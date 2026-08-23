# ADR-002: PostgreSQL-first search and filtering

Status: Accepted — 2026-08-22

PostgreSQL with `pg_trgm`, `unaccent`, relational constraints and denormalized current read models handles V1 search/facets. A separate search engine is allowed only after measured `EXPLAIN ANALYZE` and load evidence shows a need.

