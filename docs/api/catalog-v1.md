# Catalog API V1

Base path: `/api/v1`. All catalog results are Vietnam-market trims, not model-level guesses.

## `GET /brands`

Returns active brands represented by at least one current `Active`, `Announced` or `Upcoming` trim, with the current trim count. The response is cached in Redis for five minutes and invalidated after a reviewed publish.

## `GET /cars`

Query parameters:

- Paging/sort: `page`, `pageSize` (1–100), `sort` (`relevance`, `price_asc`, `price_desc`, `name_asc`, `newest`).
- Search: `q`; input is lowercase/diacritic normalized, aliases are included, and PostgreSQL `pg_trgm` is used against the indexed read model.
- Taxonomy: comma-separated `brand`, `model`, `body`, `segment`, `powertrain`; exact `seats`.
- Money: `msrpMin/Max`, `currentPriceMin/Max`, `onRoadMin/Max`.
- Numeric specs: `lengthMin/Max`, `widthMin/Max`, `heightMin/Max`, `rangeMin/Max`, `batteryMin/Max`, `consumptionMin/Max`.
- Equipment: comma-separated canonical `features` plus `featureMode=and|or`; comma-separated canonical `colors`.

`featureMode=and` means every requested feature must be officially present on the same trim. `featureMode=or` means at least one must be officially present. Unknown and official `false` are never treated as present.

Money/spec filters exclude a trim when the filtered value is unknown. The API does not manufacture an on-road estimate: before V1.5 publishes effective-dated regional results, an on-road range filter correctly matches nothing. The same rule applies to unsourced colors, energy values and images.

Every response contains the filtered facets, pagination metadata, data timestamp and explicit feature-filter semantics. Invalid ranges, page sizes, sort values or feature modes return HTTP 400 validation details.

## Read path and freshness

`current_searchable_trims` is a PostgreSQL materialized view with trigram, facet, price, dimension, feature and color indexes. A reviewed ingestion transaction refreshes it only after all facts are published, then invalidates only `vcp:catalog:v1:*` Redis keys. Normal catalog reads never call manufacturer sites or discovery services.

The reproducible gate is `scripts/verify_v1_3_catalog.py`; it verifies all three required queries, honest Tucson semantics, AND/OR behavior, validation, filters and warm-cache p95 below 300 ms.
