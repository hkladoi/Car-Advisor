# Reviewed manual import contract

V1.2 accepts UTF-8 JSON or CSV through the same Pydantic validation contract. JSON is the canonical form used by `data/seed/v1.2-initial-vehicles.json`; the CSV header template is `data/templates/vehicle-import.v1.2.csv`.

Required controls:

- Every batch has `observed_at`, `reviewed_by`, and a non-empty `review_reason`.
- `source_id` and `source_url` must resolve to the same allowlisted entry in `data/source-registry.v1.json`.
- `Official` and `Expected` facts require a value and confidence. `Unknown`, `NotAvailable`, and `NotApplicable` must not carry a value.
- An MSRP must be positive, official, use trusted/verified confidence, and include the source-backed `price_effective_from`. CSV requires an explicit `price_type`: `Msrp` with an amount, or `Unannounced` with no amount and official supporting evidence. A blank price is never inferred and is never converted to zero.
- Duplicate normalized brand/model/model-year/trim identities fail validation before any database transaction starts.
- Brave is discovery-only. Its snippets cannot be selected as a source or published as facts.

Validation command:

```text
python -m ingestion.cli validate-seed --registry data/source-registry.v1.json --seed <file.json|file.csv>
```

Publication additionally requires a manifest of immutable source snapshots and executes as one PostgreSQL transaction. High-risk price/market changes and critical powertrain changes remain linked to the reviewed audit event.
