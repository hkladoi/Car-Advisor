#!/usr/bin/env python3
"""V1 restore drill: custom DB backup, isolated restore, integrity and object-copy verification."""

from __future__ import annotations

import json
import os
import re
import subprocess
import time
import uuid
from datetime import datetime, timezone
from pathlib import Path


ROOT = Path(__file__).resolve().parent.parent
SOURCE_DATABASE = os.getenv("POSTGRES_DB", "vietnam_car_platform")
DATABASE_USER = os.getenv("POSTGRES_USER", "vcp")
SAFE_NAME = re.compile(r"^[a-z][a-z0-9_]{1,62}$")


def run(args: list[str], *, input_bytes: bytes | None = None, capture: bool = True) -> subprocess.CompletedProcess[bytes]:
    return subprocess.run(
        args,
        cwd=ROOT,
        input=input_bytes,
        stdout=subprocess.PIPE if capture else None,
        stderr=subprocess.PIPE,
        check=True,
    )


def psql(database: str, sql: str) -> str:
    result = run([
        "docker", "compose", "exec", "-T", "postgres", "psql", "-X", "-v", "ON_ERROR_STOP=1",
        "-U", DATABASE_USER, "-d", database, "-At", "-c", sql,
    ])
    return result.stdout.decode("utf-8").strip()


def assert_safe_database(name: str) -> None:
    if not SAFE_NAME.fullmatch(name) or not name.startswith("vcp_restore_gate_"):
        raise ValueError(f"Unsafe restore database name: {name}")


def main() -> None:
    suffix = uuid.uuid4().hex[:10]
    restored_database = f"vcp_restore_gate_{suffix}"
    temp_bucket = f"vcp-restore-gate-{suffix}"
    assert_safe_database(restored_database)
    started = time.monotonic()
    started_at = datetime.now(timezone.utc)
    dump: bytes | None = None
    created_database = False
    workers_stopped = False
    report_dir = ROOT / "output" / "restore-drill"
    report_dir.mkdir(parents=True, exist_ok=True)

    try:
        run(["docker", "compose", "stop", "ingestion-worker", "ingestion-scheduler"], capture=False)
        workers_stopped = True
        dump_started = time.monotonic()
        dump = run([
            "docker", "compose", "exec", "-T", "postgres", "pg_dump", "-U", DATABASE_USER,
            "-d", SOURCE_DATABASE, "--format=custom", "--compress=6", "--no-owner", "--no-privileges",
        ]).stdout
        dump_seconds = time.monotonic() - dump_started
        if len(dump) < 10_000 or not dump.startswith(b"PGDMP"):
            raise AssertionError("PostgreSQL custom backup is missing or unexpectedly small")

        psql("postgres", f'CREATE DATABASE "{restored_database}"')
        created_database = True
        restore_started = time.monotonic()
        run([
            "docker", "compose", "exec", "-T", "postgres", "pg_restore", "-U", DATABASE_USER,
            "-d", restored_database, "--no-owner", "--no-privileges", "--exit-on-error",
        ], input_bytes=dump)
        database_restore_seconds = time.monotonic() - restore_started

        live_migrations = int(psql(SOURCE_DATABASE, 'SELECT count(*) FROM "__EFMigrationsHistory"'))
        restored_migrations = int(psql(restored_database, 'SELECT count(*) FROM "__EFMigrationsHistory"'))
        if live_migrations == 0 or restored_migrations != live_migrations:
            raise AssertionError((live_migrations, restored_migrations))

        required_counts: dict[str, int] = {}
        for table, minimum in {
            "brands": 10, "models": 10, "trims": 10, "prices": 10, "sources": 20,
            "source_snapshots": 10, "source_facts": 100, "regions": 34,
            "registration_rules": 8, "energy_prices": 3, "audit_events": 1,
        }.items():
            count = int(psql(restored_database, f'SELECT count(*) FROM "{table}"'))
            if count < minimum:
                raise AssertionError(f"Restored {table} count {count} is below {minimum}")
            required_counts[table] = count

        integrity_sql = """
        SELECT CASE WHEN
          NOT EXISTS (SELECT normalized_key FROM trims GROUP BY normalized_key HAVING count(*) > 1)
          AND NOT EXISTS (
            SELECT 1 FROM prices a JOIN prices b ON a.id < b.id
             AND a.trim_id=b.trim_id AND a.price_type=b.price_type AND a.region_scope=b.region_scope
             AND a.priority=b.priority AND a.status='Official' AND b.status='Official'
             AND tstzrange(a.effective_from,a.effective_to,'[)') && tstzrange(b.effective_from,b.effective_to,'[)'))
          AND NOT EXISTS (SELECT 1 FROM source_snapshots WHERE object_key='' OR length(content_hash)<>64)
          AND NOT EXISTS (SELECT 1 FROM prices WHERE status='Official' AND source_fact_id IS NULL AND manual_override_reason IS NULL)
        THEN 'PASS' ELSE 'FAIL' END
        """
        if psql(restored_database, integrity_sql) != "PASS":
            raise AssertionError("Restored database integrity query failed")

        object_started = time.monotonic()
        scripts_mount = f"{(ROOT / 'scripts').resolve()}:/app/gate:ro"
        object_result = run([
            "docker", "compose", "run", "--rm", "--no-deps", "--volume", scripts_mount,
            "ingestion-worker", "python", "/app/gate/verify_restore_objects.py",
            "--database", restored_database, "--user", DATABASE_USER, "--temp-bucket", temp_bucket,
        ])
        object_output = object_result.stdout.decode("utf-8")
        match = re.search(r"OBJECT_RESTORE_OK=(\d+)", object_output)
        if not match:
            raise AssertionError(object_output)
        object_count = int(match.group(1))
        if object_count != required_counts["source_snapshots"]:
            raise AssertionError((object_count, required_counts["source_snapshots"]))
        object_restore_seconds = time.monotonic() - object_started

        report = {
            "status": "PASS",
            "startedAt": started_at.isoformat(),
            "sourceDatabase": SOURCE_DATABASE,
            "isolatedRestoreDatabase": restored_database,
            "backupBytes": len(dump),
            "dumpSeconds": round(dump_seconds, 3),
            "databaseRestoreSeconds": round(database_restore_seconds, 3),
            "objectRestoreSeconds": round(object_restore_seconds, 3),
            "totalRtoSeconds": round(time.monotonic() - started, 3),
            "measuredRpo": "transactionally consistent pg_dump plus immutable object references at drill start",
            "migrationCount": restored_migrations,
            "objectCount": object_count,
            "restoredCounts": required_counts,
        }
        report_path = report_dir / "v1-final-restore-report.json"
        report_path.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
        print(
            f"PASS restore drill: {len(dump)} backup bytes, {restored_migrations} migrations, "
            f"{object_count} hash-verified objects, RTO {report['totalRtoSeconds']}s"
        )
    finally:
        if created_database:
            assert_safe_database(restored_database)
            psql("postgres", f'DROP DATABASE IF EXISTS "{restored_database}" WITH (FORCE)')
        if workers_stopped:
            run(["docker", "compose", "up", "--detach", "--wait", "ingestion-worker", "ingestion-scheduler"], capture=False)


if __name__ == "__main__":
    main()
