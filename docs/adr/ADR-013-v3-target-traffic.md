# ADR-013: Define the V3 target-traffic gate

Status: Accepted — 2026-08-24

## Context

The design fixes warm-cache NFRs—catalog p95 below 300 ms, detail below 400 ms
and heavy calculators below 700 ms—and V3 FINAL requires search/API load at
target traffic. It intentionally does not name an initial request rate. The
production topology starts with one API replica, PostgreSQL and Redis and scales
horizontally before changing domain architecture.

An executable, honest target is required before claiming V3 complete. It must
exercise real reviewed data and the partner quota path without using synthetic
records in the production database or calling external providers.

## Decision

The initial single-replica V3 target is 20 requests/second sustained for 60
seconds (1,200 measured responses) with a 32-worker client pool and zero HTTP,
transport or payload-validation errors. Each second contains:

- 9 public catalog searches;
- 6 public provenance-bearing vehicle details;
- 1 deterministic recommendation calculation;
- 2 partner catalog searches;
- 2 partner vehicle details.

The route-class p95 limits remain the design NFRs. Five real search signatures
and five real trims are warmed before measurement. The partner portion uses a
fresh standard-plan key, remains below 300 requests/minute, and the key is
revoked after the test. PostgreSQL search is separately benchmarked with
100,000 isolated performance-only rows by the V3.4 gate; no benchmark row enters
the application database.

The gate fails unless achieved throughput is at least 95% of target. It records
p50/p95/p99/max per route, validates response semantics, and confirms the
partner Redis counter. No Brave, OCM, Goong or source-site call is permitted.

## Consequences

- “Target traffic” is reproducible rather than an undefined completion claim.
- The result characterizes the current single-replica local/CI topology; it is
  not a claim of internet-scale capacity or production network latency.
- Raise and rerun this gate when forecast peak traffic exceeds 20 RPS, the
  dataset/search shape materially changes, or any route approaches 70% of its
  p95 budget. Scale web/API replicas first; revisit the search engine decision
  only with measured PostgreSQL evidence.
