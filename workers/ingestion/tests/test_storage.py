from __future__ import annotations

from dataclasses import dataclass
from io import BytesIO

import pytest

from ingestion.settings import Settings
from ingestion.storage import S3CompatibleObjectStorage


@dataclass
class PutResult:
    etag: str = "etag-1"
    version_id: str = "version-1"


class Response(BytesIO):
    def release_conn(self) -> None:
        return None


class FakeMinio:
    def __init__(self) -> None:
        self.buckets: set[str] = set()
        self.objects: dict[tuple[str, str], bytes] = {}

    def bucket_exists(self, bucket: str) -> bool:
        return bucket in self.buckets

    def make_bucket(self, bucket: str) -> None:
        self.buckets.add(bucket)

    def put_object(self, bucket: str, key: str, data: BytesIO, **_: object) -> PutResult:
        self.objects[(bucket, key)] = data.read()
        return PutResult()

    def get_object(self, bucket: str, key: str) -> Response:
        return Response(self.objects[(bucket, key)])

    def stat_object(self, bucket: str, key: str) -> object:
        if (bucket, key) not in self.objects:
            raise KeyError(key)
        return object()


def test_s3_adapter_round_trips_bytes_without_leaking_provider_details() -> None:
    client = FakeMinio()
    storage = S3CompatibleObjectStorage(Settings(), client=client)  # type: ignore[arg-type]

    storage.ensure_bucket()
    stored = storage.put_bytes("sources/example/sha256.html", b"source", "text/html")

    assert stored.bucket == "vcp-snapshots"
    assert stored.etag == "etag-1"
    assert storage.get_bytes(stored.key) == b"source"


@pytest.mark.parametrize("key", ["", "/absolute", "sources/../secret"])
def test_s3_adapter_rejects_unsafe_object_keys(key: str) -> None:
    storage = S3CompatibleObjectStorage(Settings(), client=FakeMinio())  # type: ignore[arg-type]

    with pytest.raises(ValueError):
        storage.put_bytes(key, b"source", "text/plain")
