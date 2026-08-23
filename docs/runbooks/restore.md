# Runbook: database/object-store restore drill

Run the automated drill from a healthy Compose environment:

```powershell
python scripts/backup_restore_test.py
```

The script stops only the ingestion worker/scheduler, takes a compressed PostgreSQL custom-format backup, restores it into an exact disposable `vcp_restore_gate_*` database, and never rolls the live database back. It then verifies:

- the restored EF migration count matches live;
- required catalog, source, rule, region, energy and audit tables have data;
- trim identities and official price periods are not duplicated/overlapping;
- official price rows have source facts or a manual override reason;
- every restored snapshot object can be copied to an isolated versioned bucket and matches its recorded SHA-256 hash.

The script removes only its exact temporary database/bucket and restarts the worker/scheduler in `finally`. After it passes, run the calculator goldens and API/web/admin smoke paths with `python scripts/verify_v1_final.py`.

## V1 final measured drill — 2026-08-22

- Backup size: 620,469 bytes, PostgreSQL custom format.
- Restored migrations: 6.
- Hash-verified snapshot objects: 47.
- Database dump: 0.781 s; database restore: 3.188 s; object restore verification: 3.312 s.
- Measured total RTO for the isolated drill: 14.672 s.
- Measured RPO basis: transactionally consistent `pg_dump` plus immutable object references as of drill start.
- Report: `output/restore-drill/v1-final-restore-report.json`.

Production RTO/RPO must be measured again on staging with production-scale data and storage latency; this local result is evidence for the V1 implementation, not a production SLA promise.
