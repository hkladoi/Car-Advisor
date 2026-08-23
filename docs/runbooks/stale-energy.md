# Runbook: stale fuel/electricity/charging data

1. Identify which source and effective period is stale; retain the last verified row.
2. Calculator may use the last verified value only with an explicit stale warning.
3. Check the official publisher and parser signature; never substitute an unreviewed search result.
4. If automated parsing remains unavailable, use the validated manual import path with reviewer/source metadata.
5. Publish a new effective-dated value, rerun affected golden tests and invalidate current-energy caches.

