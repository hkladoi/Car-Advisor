#!/usr/bin/env python3
"""Copy every restored snapshot reference into an isolated bucket and hash-verify it."""

from __future__ import annotations

import argparse
import hashlib
from io import BytesIO
from urllib.parse import urlsplit

import psycopg
from minio import Minio
from ingestion.settings import Settings


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--database", required=True)
    parser.add_argument("--user", required=True)
    parser.add_argument("--temp-bucket", required=True)
    args = parser.parse_args()
    if not args.temp_bucket.startswith("vcp-restore-gate-"):
        raise ValueError("Temporary bucket must use the vcp-restore-gate- prefix")

    settings = Settings()
    parsed = urlsplit(settings.object_storage_endpoint)
    endpoint = parsed.netloc if parsed.scheme else settings.object_storage_endpoint
    client = Minio(
        endpoint,
        access_key=settings.object_storage_access_key,
        secret_key=settings.object_storage_secret_key.get_secret_value(),
        secure=parsed.scheme == "https",
        region=settings.object_storage_region,
    )
    with psycopg.connect(host="/var/run/postgresql", dbname=args.database, user=args.user) as connection:
        with connection.cursor() as cursor:
            cursor.execute("SELECT DISTINCT object_key, content_hash FROM source_snapshots ORDER BY object_key")
            references = cursor.fetchall()
    if not references:
        raise AssertionError("Restored database contains no source snapshot references")

    created = False
    copied: list[str] = []
    try:
        if client.bucket_exists(args.temp_bucket):
            raise AssertionError(f"Refusing to reuse temporary bucket {args.temp_bucket}")
        client.make_bucket(args.temp_bucket)
        created = True
        for object_key, expected_hash in references:
            source = client.get_object(settings.object_storage_bucket, object_key)
            try:
                content = source.read()
            finally:
                source.close()
                source.release_conn()
            actual_hash = hashlib.sha256(content).hexdigest()
            if actual_hash.lower() != expected_hash.lower():
                raise AssertionError(f"Hash mismatch for source object {object_key}")
            client.put_object(
                args.temp_bucket,
                object_key,
                BytesIO(content),
                len(content),
                content_type="application/octet-stream",
            )
            copied.append(object_key)
            restored = client.get_object(args.temp_bucket, object_key)
            try:
                restored_hash = hashlib.sha256(restored.read()).hexdigest()
            finally:
                restored.close()
                restored.release_conn()
            if restored_hash != actual_hash:
                raise AssertionError(f"Restored object hash mismatch for {object_key}")
        print(f"OBJECT_RESTORE_OK={len(copied)}")
    finally:
        if created:
            for object_key in copied:
                client.remove_object(args.temp_bucket, object_key)
            remaining = list(client.list_objects(args.temp_bucket, recursive=True))
            if remaining:
                raise AssertionError("Temporary restore bucket is not empty after exact-key cleanup")
            client.remove_bucket(args.temp_bucket)


if __name__ == "__main__":
    main()
