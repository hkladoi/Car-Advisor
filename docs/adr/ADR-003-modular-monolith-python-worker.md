# ADR-003: Modular monolith plus Python ingestion worker

Status: Accepted — 2026-08-22

User-facing domain/calculation modules share one ASP.NET Core deployment and database transaction boundary. Web ingestion runs in a separate Python worker because HTTP parsing, Playwright and data tooling have different dependencies and failure modes. No additional microservice is created without an operational/scale justification.

