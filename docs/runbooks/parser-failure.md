# Runbook: parser failure

1. Confirm the failed source/job and preserve its latest immutable snapshot.
2. Do not delete or overwrite the current published record.
3. Mark parser/source freshness state and allow only bounded retry/backoff.
4. Compare the source structure with the last passing fixture.
5. Fix the domain parser and add/update a regression fixture.
6. Re-run extraction, inspect candidate diff and route high-risk changes to review.
7. Close the alert only when the source freshness SLA has recovered.

