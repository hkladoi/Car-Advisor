#!/usr/bin/env python3
"""Benchmark PostgreSQL search indexes in an isolated, disposable database.

The generated rows are performance-only and never enter the application database
or a production data path. The temporary database is force-dropped in ``finally``.
"""

from __future__ import annotations

import argparse
import json
import re
import statistics
import subprocess
import uuid
from pathlib import Path
from typing import Any


ROOT = Path(__file__).resolve().parents[1]
DATABASE_PATTERN = re.compile(r"^vcp_v34_bench_[0-9a-f]{10}$")
QUERY_LIMIT_MS = 150.0


def run(*args: str, input_text: str | None = None) -> subprocess.CompletedProcess[str]:
    try:
        return subprocess.run(
            args,
            cwd=ROOT,
            input=input_text,
            check=True,
            capture_output=True,
            text=True,
            encoding="utf-8",
        )
    except subprocess.CalledProcessError as error:
        detail = (error.stderr or error.stdout or str(error)).strip()
        raise RuntimeError(f"Command failed: {detail}") from error


def postgres_command(*args: str, input_text: str | None = None) -> str:
    return run(
        "docker", "compose", "exec", "-T", "postgres", *args,
        input_text=input_text,
    ).stdout.strip()


def psql(database: str, sql: str, *, tuples_only: bool = False) -> str:
    args = ["psql", "-U", "vcp", "-d", database, "-v", "ON_ERROR_STOP=1"]
    if tuples_only:
        args.extend(["-A", "-t"])
    args.extend(["-c", sql])
    return postgres_command(*args)


def plan(database: str, sql: str) -> dict[str, Any]:
    output = psql(database, f"EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON) {sql}", tuples_only=True)
    return json.loads(output)[0]


def node_types(node: dict[str, Any]) -> list[str]:
    values = [str(node.get("Node Type", ""))]
    for child in node.get("Plans", []):
        values.extend(node_types(child))
    return values


def index_names(node: dict[str, Any]) -> list[str]:
    values = [str(node["Index Name"])] if node.get("Index Name") else []
    for child in node.get("Plans", []):
        values.extend(index_names(child))
    return values


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--rows", type=int, default=100_000)
    parser.add_argument("--runs", type=int, default=5)
    args = parser.parse_args()
    if args.rows < 50_000 or args.rows > 1_000_000:
        raise ValueError("--rows must be between 50,000 and 1,000,000")
    if args.runs < 3 or args.runs > 20:
        raise ValueError("--runs must be between 3 and 20")

    database = f"vcp_v34_bench_{uuid.uuid4().hex[:10]}"
    if not DATABASE_PATTERN.fullmatch(database):
        raise RuntimeError("Refusing to create an unexpected benchmark database name")

    try:
        postgres_command("createdb", "-U", "vcp", "-T", "template0", database)
        psql(database, f"""
            CREATE EXTENSION pg_trgm;
            CREATE TABLE searchable_trims (
                trim_id bigint PRIMARY KEY,
                brand_slug text NOT NULL,
                model_slug text NOT NULL,
                body_type text NOT NULL,
                segment text NOT NULL,
                powertrain_type text NOT NULL,
                current_price_amount numeric(19,2),
                search_text text NOT NULL,
                feature_codes text[] NOT NULL,
                color_codes text[] NOT NULL
            );
            INSERT INTO searchable_trims
            SELECT
                value,
                (ARRAY['toyota','vinfast','hyundai','kia','mazda','ford','honda','bmw','audi','volvo'])[1 + value % 10],
                CASE WHEN value % 1000 = 420
                    THEN 'yaris-cross'
                    ELSE concat('model-', substr(md5((value % 1000)::text), 1, 12))
                END,
                (ARRAY['Suv','Sedan','Hatchback','Mpv'])[1 + value % 4],
                (ARRAY['B','C','D','E'])[1 + value % 4],
                (ARRAY['Ice','Bev','Hev','Phev'])[1 + value % 4],
                500000000::numeric + (value % 2500) * 1000000::numeric,
                concat_ws(' ',
                    (ARRAY['toyota','vinfast','hyundai','kia','mazda','ford','honda','bmw','audi','volvo'])[1 + value % 10],
                    CASE WHEN value % 1000 = 420
                        THEN 'yaris cross'
                        ELSE concat('model ', substr(md5((value % 1000)::text), 1, 12))
                    END,
                    (ARRAY['petrol ice','electric ev bev','hybrid hev','hybrid plug in phev'])[1 + value % 4],
                    'benchmark variant', value::text),
                ARRAY[CASE WHEN value % 2 = 0 THEN 'AEB' ELSE 'ACC' END],
                ARRAY[CASE WHEN value % 3 = 0 THEN 'WHITE' ELSE 'BLACK' END]
            FROM generate_series(1, {args.rows}) AS value;
            CREATE INDEX ix_searchable_trims_search_text_trgm
                ON searchable_trims USING gin (search_text gin_trgm_ops);
            CREATE INDEX ix_searchable_trims_facets
                ON searchable_trims (brand_slug, model_slug, body_type, segment, powertrain_type);
            CREATE INDEX ix_searchable_trims_prices
                ON searchable_trims (current_price_amount);
            CREATE INDEX ix_searchable_trims_features
                ON searchable_trims USING gin (feature_codes);
            ANALYZE searchable_trims;
        """)

        queries = {
            "substring": """
                SELECT trim_id FROM searchable_trims
                WHERE search_text LIKE '%toyota yaris cross%'
            """,
            "fuzzy": """
                SELECT trim_id FROM searchable_trims
                WHERE 'toytoa yaris cros petrol' <<% search_text
            """,
            "faceted": """
                SELECT trim_id FROM searchable_trims
                WHERE brand_slug='toyota' AND powertrain_type='Ice'
                  AND current_price_amount BETWEEN 500000000 AND 1500000000
                ORDER BY current_price_amount LIMIT 24
            """,
            "feature": """
                SELECT trim_id FROM searchable_trims
                WHERE feature_codes @> ARRAY['AEB']::text[]
                  AND brand_slug='toyota' LIMIT 24
            """,
        }
        results: dict[str, Any] = {}
        for name, query in queries.items():
            plan(database, query)  # Warm relation/index pages before measuring.
            measurements = [plan(database, query) for _ in range(args.runs)]
            execution_times = [float(value["Execution Time"]) for value in measurements]
            p95_ms = statistics.quantiles(execution_times, n=20, method="inclusive")[18]
            slowest = max(measurements, key=lambda value: float(value["Execution Time"]))
            nodes = node_types(slowest["Plan"])
            indexes = index_names(slowest["Plan"])
            assert p95_ms <= QUERY_LIMIT_MS, (name, p95_ms, measurements)
            if name in {"substring", "fuzzy", "faceted"}:
                assert any("Index" in node or "Bitmap" in node for node in nodes), (name, nodes, slowest)
            results[name] = {
                "runs": args.runs,
                "executionMsP95": round(p95_ms, 3),
                "executionMsMax": max(execution_times),
                "planningMsMax": max(float(value["Planning Time"]) for value in measurements),
                "nodeTypes": nodes,
                "indexes": indexes,
                "returnedRows": int(slowest["Plan"].get("Actual Rows", 0)),
            }

        print(json.dumps({
            "gate": "V3.4-postgresql-benchmark",
            "status": "PASS",
            "isolatedBenchmarkRows": args.rows,
            "perQueryLimitMs": QUERY_LIMIT_MS,
            "queries": results,
            "decision": "PostgreSQL remains sufficient; Typesense/Meilisearch is not justified by measured need.",
        }, indent=2))
    finally:
        if DATABASE_PATTERN.fullmatch(database):
            postgres_command("dropdb", "-U", "vcp", "--if-exists", "--force", database)


if __name__ == "__main__":
    main()
