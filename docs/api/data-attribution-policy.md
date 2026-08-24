# Data attribution and licensing policy

Policy version: `2026-08-24`

Vietnam Car Platform normalizes published vehicle facts from identified source
records. Access to the API does **not** grant a blanket licence to the source
material. Every consumer must respect the terms, copyright, database rights,
branding rules and attribution requirements attached to each upstream source.

## Required attribution

Display or otherwise preserve this platform attribution near reused results:

> Vietnam Car Platform — normalized data with source provenance.

Keep every per-record source name, source URL, authority, fetch timestamp and
attribution string returned by the API. A consumer may change presentation but
must not obscure the source, turn an unknown value into a fact, or present a
manufacturer/fuel/year cohort as trim-specific data.

The EEA real-world-consumption cohort requires its returned European
Environment Agency attribution and methodology link. It remains an EU/EEA
manufacturer × fuel × registration-year reference and is not a Vietnam trim
measurement. Official Vietnam trim consumption must remain visibly separate.

## Permitted use

- Read and analyze normalized, published facts within the assigned usage plan.
- Cache a response only as long as its source-specific terms permit, retaining
  effective dates, source provenance and the API contract/policy versions.
- Link back to an upstream source or Vietnam Car Platform record.

## Prohibited use

- Republishing source-page prose, PDFs, photographs, logos, maps or other media
  unless a separate rights record or upstream licence explicitly permits it.
- Removing or falsifying provenance, confidence, scope, dates or attribution.
- Relabelling aggregate/cohort data as a model-, trim- or Vietnam-specific
  observation.
- Exposing an API key in a browser bundle, URL, analytics event, support ticket
  or application log; sharing a key between unrelated organisations.
- Using the read API to infer or perform a write, review or publication action.

## Contract and policy changes

The stable contract is identified by its URL (`/api/v1`) and the
`X-VCP-Contract-Version` response header. Additive fields may be introduced in
`v1`; existing fields and meanings are not removed or silently redefined.
Breaking changes require a new major path and a documented migration window.

Every issued key records the policy version accepted at issuance. A policy
change that requires renewed acceptance invalidates an older key instead of
silently extending its permissions. The current policy is always available at
`GET /api/v1/partner/policy` without a key.

## Source-specific questions

The API's `SOURCE-SPECIFIC` marker is intentionally not a licence identifier.
If a returned source has no clear reuse grant, treat the response as factual
reference with attribution and link to the source; do not redistribute the
source asset. Operators must resolve ambiguous rights before enabling any new
bulk export or media endpoint.
