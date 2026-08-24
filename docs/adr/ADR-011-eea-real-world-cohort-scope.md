# ADR-011: Keep EEA real-world data at cohort scope

Status: Accepted — 2026-08-24

## Context

V3.3 requires real-world consumption only when a trustworthy source is
available, with official and real-world metrics separated and aggregation
methodology/sample size visible. No reviewed Vietnam source currently provides
licensed, statistically documented trim-level measurements at sufficient
scale.

The European Environment Agency publishes OBFCM real-world fuel/CO2 data under
Article 12 of Regulation (EU) 2019/631. Its official aggregate contains
manufacturer × fuel × vehicle-registration-year cohorts and both OBFCM and WLTP
metrics. The accompanying statistical metadata explains the cleaning and
aggregation method, and EEA reuse policy requires attribution.

Primary references:

- [EEA Datahub — real-world emissions from cars and vans](https://www.eea.europa.eu/en/datahub/datahubitem-view/1c1ffad2-34c3-471b-bd69-dd013cdd7b80)
- [EEA statistical metadata](https://sdi.eea.europa.eu/catalogue/srv/api/records/d12422cc-f1b9-4a20-b31e-94fff4d997ed/attachments/Real%20world%20emissions%20for%20cars%20and%20vans_Statistical%20metadata_2024.pdf)
- [Official 2023 cars aggregate CSV](https://sdi.eea.europa.eu/webdav/datastore/public/eea_t_real-world-co2-emission_p_2024_v03_r00/2023_Cars_Aggregated.csv)
- [EEA legal notice](https://www.eea.europa.eu/en/legal-notice) and [data policy](https://www.eea.europa.eu/en/datahub/eea-data-policy)

## Decision

1. Ingest the official aggregate CSV into immutable object storage before
   parsing and publication.
2. Publish each row at its actual cohort scope. Never relabel it as a model,
   trim or Vietnam measurement.
3. Keep OBFCM real-world and cohort WLTP fields separate. Keep official
   Vietnam-trim consumption in the existing trim field/API branch.
4. Every public cohort carries registration year, sample size, geography,
   aggregation scope, methodology URL, attribution and source provenance.
5. Link a cohort to a catalog brand only through a reviewed exact manufacturer
   allowlist. Corporate groups spanning multiple brands remain unmapped.
6. Select the latest registration year and, when legal manufacturer entities
   overlap for a fuel type, expose the largest published sample without
   averaging already-aggregated values.

## Consequences

- Users get a trustworthy real-world reference without a false trim-specific
  claim.
- Coverage is intentionally limited: brands absent from the EEA file or without
  a safe exact mapping show no cohort.
- The raw multi-million-row EEA dataset remains a future option for a separately
  reviewed model-level methodology; it is not silently joined by name today.
- A future Vietnam trim-level source may be added only if its license,
  methodology, sample definition and provenance satisfy the same contract.
