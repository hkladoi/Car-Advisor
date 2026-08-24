# Source registry and ownership

Priority: `OFFICIAL_GOV > OFFICIAL_MANUFACTURER > OFFICIAL_DISTRIBUTOR > OFFICIAL_DEALER > TRUSTED_PRESS > OTHER`.

| Category | Authoritative owner | Cadence | Parser owner |
| --- | --- | --- | --- |
| Legal/tax/fee | Competent government authority | Event/daily watch | Registration module + legal parser |
| Fuel | MOIT/DMS official publication | Each adjustment/daily watch | Energy parser |
| Household electricity | EVN/competent authority | Event/daily watch | Energy parser |
| Charging tariff/promotion | Charging provider | Daily | Charging parser |
| MSRP/spec/warranty | Vietnam manufacturer/distributor | Price daily; specs weekly/change | Brand parser |
| Dealer offer | Authorized dealer/branch | Daily for supported sources | Dealer parser |
| Region | Province Open API v2 snapshot | Periodic/manual reviewed | Region importer |
| Real-world fuel/CO2 cohort | EEA OBFCM official aggregate | Monthly snapshot | Strict CSV cohort parser |
| Discovery | Brave Search | Budgeted only when missing/stale | Discovery client |

V1.2 registers exact official domains, owners, robots notes, freshness SLA and fixture paths. No Brave snippet is persisted as a field fact.

The machine-readable authority is `data/source-registry.v1.json`. It contains
exact official product/price sources, brand registries, government and energy
sources, the EEA real-world aggregate, Open Charge Map and a disabled
discovery-only Brave entry. Each entry records authority, allowed redirect
domains, content type, refresh SLA, priority, robots/access note and reuse note.

## V3.3 EEA real-world cohort

`eea-real-world-cars-2023-aggregate` is a competent-authority CSV source. The
worker snapshots the original bytes, validates all expected columns and
publishes manufacturer × fuel × registration-year facts with sample size,
methodology and attribution. It is not a trim source. Only reviewed exact
manufacturer mappings may attach a cohort to a Vietnam catalog brand; ambiguous
corporate groups remain unlinked. See ADR-011.

## V1.2 initial seed

The reviewed initial batch contains one representative current trim from 10 brands: VinFast, Toyota, Kia, Mazda, Honda, Mitsubishi, BYD, Geely, OMODA and BMW. BMW exercises premium pricing/options and the batch spans ICE, HEV/MHEV, PHEV and BEV taxonomy paths.

Every published fact points to a 2xx raw snapshot stored under `sources/<source-id>/sha256/<content-hash>.<extension>`. The database holds URL/domain/authority/fetched time/hash/object key/parser method and the reviewed source facts.

Ford remains registered but its official host rejected the declared crawler during the V1.2 gate. The stale Skoda Karoq URL redirected to a 404. Both are explicitly disabled for automated fetch and excluded from the seed rather than publishing blocked/error content. They can be activated only after an official replacement URL or permitted access is reviewed.
