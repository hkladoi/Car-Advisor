#!/usr/bin/env python3
"""Apply, roll back and reapply the V3.5 schema in an isolated database."""

from __future__ import annotations

import json
import re
import subprocess
import uuid
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
DATABASE_PATTERN = re.compile(r"^vcp_v35_migration_[0-9a-f]{10}$")
PREVIOUS_MIGRATION = "20260824023048_AddV34SearchSync"
CURRENT_MIGRATION = "20260824032619_AddV35PartnerApi"


def run(*args: str, input_text: str | None = None) -> str:
    try:
        return subprocess.run(
            args,
            cwd=ROOT,
            input=input_text,
            check=True,
            capture_output=True,
            text=True,
            encoding="utf-8",
        ).stdout.strip()
    except subprocess.CalledProcessError as error:
        detail = (error.stderr or error.stdout or "migration command failed").strip()
        raise RuntimeError(detail) from error


def postgres_command(*args: str, input_text: str | None = None) -> str:
    return run(
        "docker", "compose", "exec", "-T", "postgres", *args,
        input_text=input_text,
    )


def psql(database: str, sql: str) -> str:
    return postgres_command(
        "psql", "-U", "vcp", "-d", database,
        "-v", "ON_ERROR_STOP=1", "-A", "-t", "-c", sql,
    )


def migration_sql(start: str, end: str, *, idempotent: bool = False) -> str:
    arguments = [
        "dotnet", "ef", "migrations", "script", start, end,
        "--project", "apps/api/src/Infrastructure/VietnamCarPlatform.Infrastructure.csproj",
        "--startup-project", "apps/api/src/Api/VietnamCarPlatform.Api.csproj",
        "--configuration", "Release", "--no-build",
    ]
    if idempotent:
        arguments.append("--idempotent")
    return run(*arguments)


def apply_sql(database: str, sql: str) -> None:
    postgres_command(
        "psql", "-U", "vcp", "-d", database, "-v", "ON_ERROR_STOP=1", "-f", "-",
        input_text=sql,
    )


def main() -> None:
    database = f"vcp_v35_migration_{uuid.uuid4().hex[:10]}"
    if not DATABASE_PATTERN.fullmatch(database):
        raise RuntimeError("Refusing to create an unexpected migration-gate database name")

    try:
        postgres_command("createdb", "-U", "vcp", "-T", "template0", database)
        apply_sql(database, migration_sql("0", CURRENT_MIGRATION, idempotent=True))
        applied = json.loads(psql(database, """
            SELECT json_build_object(
              'migration', EXISTS (
                SELECT 1 FROM \"__EFMigrationsHistory\"
                WHERE migration_id='20260824032619_AddV35PartnerApi'),
              'plansTable', to_regclass('partner_api_usage_plans') IS NOT NULL,
              'keysTable', to_regclass('partner_api_keys') IS NOT NULL,
              'usagePlans', (SELECT count(*) FROM partner_api_usage_plans),
              'planConstraints', (
                SELECT count(*) FROM pg_constraint
                WHERE conrelid='partner_api_usage_plans'::regclass AND contype='c'),
              'keyConstraints', (
                SELECT count(*) FROM pg_constraint
                WHERE conrelid='partner_api_keys'::regclass AND contype='c'),
              'uniqueIndexes', (
                SELECT count(*) FROM pg_indexes
                WHERE tablename IN ('partner_api_usage_plans','partner_api_keys')
                  AND indexdef LIKE 'CREATE UNIQUE INDEX%')
            )::text;
        """))
        assert applied == {
            "migration": True,
            "plansTable": True,
            "keysTable": True,
            "usagePlans": 2,
            "planConstraints": 2,
            "keyConstraints": 6,
            "uniqueIndexes": 5,
        }, applied
        plans = psql(
            database,
            "SELECT code||':'||requests_per_minute||':'||requests_per_month||':'||max_page_size "
            "FROM partner_api_usage_plans ORDER BY code;",
        ).splitlines()
        assert plans == ["sandbox:30:10000:25", "standard:300:500000:100"], plans

        apply_sql(database, migration_sql(CURRENT_MIGRATION, PREVIOUS_MIGRATION))
        rolled_back = json.loads(psql(database, """
            SELECT json_build_object(
              'migrationRemoved', NOT EXISTS (
                SELECT 1 FROM \"__EFMigrationsHistory\"
                WHERE migration_id='20260824032619_AddV35PartnerApi'),
              'plansRemoved', to_regclass('partner_api_usage_plans') IS NULL,
              'keysRemoved', to_regclass('partner_api_keys') IS NULL
            )::text;
        """))
        assert all(rolled_back.values()), rolled_back

        apply_sql(database, migration_sql(PREVIOUS_MIGRATION, CURRENT_MIGRATION))
        assert psql(
            database,
            "SELECT count(*) FROM partner_api_usage_plans;",
        ) == "2"
    finally:
        if DATABASE_PATTERN.fullmatch(database):
            postgres_command("dropdb", "-U", "vcp", "--if-exists", "--force", database)

    print(json.dumps({
        "gate": "V3.5 migration",
        "status": "PASS",
        "up": CURRENT_MIGRATION,
        "down": PREVIOUS_MIGRATION,
        "reapplied": True,
        "isolatedDatabaseRemoved": True,
    }, separators=(",", ":")))


if __name__ == "__main__":
    main()
