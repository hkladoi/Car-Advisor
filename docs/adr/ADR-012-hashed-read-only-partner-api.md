# ADR-012: Isolate a hashed, read-only partner API

Status: Accepted — 2026-08-24

## Context

V3.5 requires a stable versioned public/partner contract, API keys, usage plans,
rate limits and explicit data licensing/attribution. Existing `/api/v1` routes
are anonymous product endpoints and must not be broken. Keys must work across
multiple replicas without creating a new source of catalog truth or leaking a
reusable credential through the database, UI, OpenAPI examples or logs.

## Decision

1. Preserve existing anonymous `/api/v1` routes. Add integration endpoints only
   below `/api/v1/partner` and allow GET/HEAD semantics only.
2. Generate a 256-bit random secret with a non-secret lookup prefix. Return the
   full value once, store only SHA-256 plus the prefix, and compare hashes in
   fixed time. Revocation and optional expiry take effect on the next request.
3. Persist plan/key lifecycle in PostgreSQL and audit issuance/revocation.
   Keys bind to one `catalog.read` scope and the exact accepted policy version.
4. Use an atomic Redis script for per-key UTC-minute and UTC-month counters.
   Fail closed when distributed quota enforcement is unavailable.
5. Wrap the proven catalog read models instead of creating duplicate partner
   data. Detail responses preserve fact-level provenance and source attribution.
6. Treat licensing as source-specific. The platform does not grant blanket
   rights over source prose, images or assets.
7. Keep the OpenAPI v1 document committed and generated-client drift checked in
   CI. Breaking contract changes require a new major URL.

## Consequences

- Anonymous users and existing clients remain compatible, while partner traffic
  receives explicit lifecycle, quota and policy controls.
- A database leak does not reveal immediately usable API keys. Operators cannot
  recover a lost plaintext key and must issue a replacement.
- Redis is required for protected partner reads; denying a request during a
  counter outage is safer than allowing unmetered cross-replica traffic.
- Policy acceptance can be enforced independently from the schema version.
- Source-specific restrictions remain visible rather than being hidden behind
  an inaccurate platform-wide licence label.
