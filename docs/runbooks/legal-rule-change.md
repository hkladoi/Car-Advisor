# Runbook: legal or registration-rule change

1. Capture the competent-authority source and immutable snapshot.
2. Create a new `RegistrationRule` row with its future/current effective period; keep the old rule.
3. Add golden tests for the day before and the effective day.
4. Require admin review for scope, priority and formula parameters.
5. Publish, invalidate affected caches and verify on-road responses expose the new applied rule/source ID.
6. Do not deploy a frontend formula change.

