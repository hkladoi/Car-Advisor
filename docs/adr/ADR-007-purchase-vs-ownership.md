# ADR-007: Purchase affordability is separate from ownership affordability

Status: Accepted — 2026-08-22

Cash/family/trade-in/loan feasibility is calculated independently from monthly operating ownership. A family-funded vehicle can be `EXTERNALLY_FUNDED/NOT_APPLICABLE` for purchase while still failing ownership affordability. UI and APIs must never collapse these ratings.

