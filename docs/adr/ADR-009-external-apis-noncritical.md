# ADR-009: External APIs are non-critical runtime dependencies

Status: Accepted — 2026-08-22

Brave Search, Open Charge Map, Goong and external source sites are worker/discovery inputs. Normal catalog reads use cached published database data. Every external client must have bounded timeout/retry/rate/budget controls.

