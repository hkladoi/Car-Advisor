# Monitoring contract

V1 emits JSON logs, correlation IDs, ASP.NET/OpenTelemetry traces and worker JSON events. Set `OTEL_EXPORTER_OTLP_ENDPOINT` to forward traces and `SENTRY_DSN` for error capture without default PII.

Operational views are split by ownership:

- API availability: `/health/live` and `/health/ready`, with PostgreSQL, Redis and object-store readiness.
- Data operations: `/admin` and `/admin/coverage` show active counts, completeness, freshness, blocked/stale states and data-quality totals.
- Detailed machine-readable checks: `/api/v1/admin/coverage` and `/api/v1/admin/quality`.
- Crawler health: worker/scheduler container health plus structured fetch/parser/snapshot events; stale sources are surfaced by the data-quality view.

Alert conditions for V1 are failed readiness, worker restart/failure, stale fuel/charging/legal sources and any critical quality issue. Provider-specific dashboard provisioning remains deployment configuration; the application emits the health, trace and structured data needed by that provider.
