# Runbook: onboard a brand, trim, source, dealer and offer

This is the V1 reviewed-data path. Replace every `<placeholder>` with evidence from the real official source. A draft may be created without facts, but nothing may be presented as official merely to make a coverage number look complete.

## 1. Accept the source before entering facts

1. Prefer the Vietnam government, manufacturer, distributor or authorized dealer page that owns the fact. Press is provisional and Brave snippets are discovery only.
2. Review robots/access conditions, reuse rights, redirect domains and freshness SLA.
3. Add the source to `data/source-registry.v1.json` with a stable lowercase ID, exact HTTPS URL, explicit `allowed_domains`, authority, content type, refresh interval, priority, `robots_note` and `terms_note`.
4. Do not enable automated fetch if the declared crawler is blocked or the access terms are unclear. Keep the source registry-only with `"automated_fetch": false` and a reason.
5. Validate the complete registry/seed pair before any fetch:

```powershell
docker compose run --rm --no-deps ingestion-worker python -m ingestion.cli validate-seed `
  --registry /app/data/source-registry.v1.json `
  --seed /app/data/seed/<reviewed-batch>.json
```

The admin `POST /api/v1/admin/sources` endpoint can maintain a manual database-only source, but a recurring worker source must also exist in the versioned registry. Do not let the file and database describe different URLs or authority levels.

## 2. Add the brand and trim through the reviewed seed

Copy one record shape from `data/seed/v1.2-initial-vehicles.json`, or start from `data/templates/vehicle-import.v1.2.csv`.

- `observed_at`, `reviewed_by` and `review_reason` are mandatory.
- Brand/model/generation/model-year/trim identity must be canonical and unique.
- `source_id` and `source_url` must match the registry entry exactly.
- Use `Official` only for a value stated by the accepted source. Preserve `raw_value` for review.
- Use explicit `Unknown` with no value when the source is silent. Never convert an absent price to zero.
- An official MSRP requires amount, VND currency, effective date and verified official confidence. An announced trim without a public price uses `Unannounced` with a supporting official source.

Fetch immutable snapshots and publish only after review:

```powershell
New-Item -ItemType Directory -Force .tmp | Out-Null
docker compose run --rm --no-deps --volume "${PWD}/.tmp:/app/.tmp" ingestion-worker python -m ingestion.cli fetch-seed `
  --registry /app/data/source-registry.v1.json `
  --seed /app/data/seed/<reviewed-batch>.json `
  --manifest /app/.tmp/<reviewed-batch>-snapshots.json

docker compose run --rm --no-deps --volume "${PWD}/.tmp:/app/.tmp:ro" ingestion-worker python -m ingestion.cli publish-seed `
  --registry /app/data/source-registry.v1.json `
  --seed /app/data/seed/<reviewed-batch>.json `
  --manifest /app/.tmp/<reviewed-batch>-snapshots.json `
  --dsn "host=/var/run/postgresql dbname=vietnam_car_platform user=vcp"
```

Publication is one PostgreSQL transaction. It writes source/snapshot/fact
provenance and a durable `CatalogSearchSync.*` outbox event. The API projector
then refreshes `current_searchable_trims` asynchronously and invalidates the
distributed catalog generation after success. Before checking the API, confirm
the new event reached `Completed` (normally below one second; gate maximum 10
seconds):

```sql
SELECT event_type, status, attempts, processed_at, last_error
FROM published_data_events
ORDER BY occurred_at DESC
LIMIT 10;
```

Verify the new trim through `/api/v1/cars?q=<model>` and
`/api/v1/cars/<trim-id>`; the detail must expose the accepted URL, fetch time
and 64-character content hash.

The admin console can create an isolated `Draft` trim before the batch is ready. That path is for work-in-progress identity only; publish the actual facts with the reviewed snapshot pipeline above.

## 3. Add the dealer and branch

Authenticate as an Editor or Administrator and keep the bearer token outside browser storage. The web admin BFF uses an HttpOnly cookie; direct API examples below use `Authorization: Bearer <admin-token>`.

Resolve the real brand ID from `GET /api/v1/brands`, then create the dealer:

```json
POST /api/v1/admin/dealers
{
  "brandId": "<brand-guid>",
  "name": "<legal-or-public-dealer-name>",
  "slug": "<stable-dealer-slug>",
  "officialStatus": true,
  "officialUrl": "https://<authorized-dealer-domain>/",
  "reason": "Reviewed authorized-dealer evidence at <official-url> on <date>."
}
```

`officialStatus` must be false until brand authorization is actually verified. Then create a physical branch using a canonical province code returned by `GET /api/v1/regions`:

```json
POST /api/v1/admin/dealer-branches
{
  "dealerId": "<dealer-guid>",
  "name": "<branch-name>",
  "provinceCode": "<VN-province-code>",
  "address": "<published-branch-address>",
  "latitude": null,
  "longitude": null,
  "reason": "Address reviewed on the authorized dealer source at <official-url>."
}
```

Leave coordinates null unless a permitted, reviewed source supplies them. Dealer and branch are separate entities; never encode a branch or province inside the dealer name.

## 4. Add a dealer offer without inventing value

Register/fetch the authorized dealer offer page first. After publication of its snapshot/fact, resolve the exact source fact ID read-only:

```sql
SELECT sf.id, s.url, ss.fetched_at, ss.content_hash
FROM source_facts sf
JOIN source_snapshots ss ON ss.id = sf.snapshot_id
JOIN sources s ON s.id = ss.source_id
WHERE s.url = '<exact-offer-url>'
ORDER BY ss.fetched_at DESC, sf.created_at DESC;
```

Create the offer as `Draft` or `PendingReview`; use `Published` only after the URL, trim, branch, dates, conditions and every benefit have been reviewed:

```json
POST /api/v1/admin/dealer-offers
{
  "branchId": "<branch-guid>",
  "trimId": "<eligible-trim-guid>",
  "headline": "<verbatim-short-offer-heading>",
  "combinabilityGroup": "<group-or-null>",
  "conditionsJson": "{\"customerType\":\"<published-condition>\"}",
  "status": "PendingReview",
  "effectiveFrom": "<ISO-8601-with-offset>",
  "effectiveTo": "<ISO-8601-with-offset-or-null>",
  "sourceFactId": "<source-fact-guid>",
  "benefits": [
    {
      "type": "CashDiscount",
      "cashValue": "<published-cash-amount-or-null>",
      "statedValue": "<published-stated-value-or-null>",
      "currency": "VND",
      "isCashEquivalent": true,
      "exclusivityGroup": "<exclusive-group-or-null>",
      "note": "<published-condition-or-null>"
    }
  ],
  "reason": "Offer transcribed and reviewed against snapshot <content-hash>."
}
```

Cash-equivalent benefits and gifts must remain separate. Do not turn a gift's advertised value into a cash discount. The API rejects invalid dates, malformed condition JSON, missing entities, duplicate benefits, exclusivity conflicts and region/branch mismatches. Expired published offers remain history and cannot be deleted; update them to `Expired`.

## 5. Release checks

Before declaring onboarding complete:

1. `GET /api/v1/admin/quality` has no new impossible, duplicate, provenance or dealer-offer issue.
2. `GET /api/v1/admin/coverage` shows the expected discovered/mapped/published movement; a missing source remains blocked/stale rather than silently dropped.
3. `GET /api/v1/admin/audit` contains actor, reason, timestamp and before/after for each admin change.
4. Catalog and detail show the trim and only currently effective published offers. Offer/source disclosure opens correctly.
5. Run `python scripts/verify_v1_final.py`. If schema changed, also run the migration and restore gates before release.
