from __future__ import annotations

from dataclasses import dataclass
from io import BytesIO
from typing import Protocol
from urllib.parse import urlsplit

from minio import Minio
from minio.error import S3Error

from ingestion.settings import Settings


@dataclass(frozen=True, slots=True)
class StoredObject:
    bucket: str
    key: str
    etag: str | None
    version_id: str | None


class ObjectStorage(Protocol):
    """Boundary for immutable source snapshots and rights-approved assets."""

    def ensure_bucket(self) -> None: ...

    def put_bytes(self, key: str, content: bytes, content_type: str) -> StoredObject: ...

    def get_bytes(self, key: str) -> bytes: ...

    def exists(self, key: str) -> bool: ...


class S3CompatibleObjectStorage:
    """MinIO locally; the same adapter targets an HTTPS R2/S3 endpoint in production."""

    def __init__(self, settings: Settings, client: Minio | None = None) -> None:
        parsed = urlsplit(settings.object_storage_endpoint)
        endpoint = parsed.netloc if parsed.scheme else settings.object_storage_endpoint
        if not endpoint or (parsed.scheme and parsed.path not in ("", "/")):
            raise ValueError("OBJECT_STORAGE_ENDPOINT must be a host[:port] or root http(s) URL")

        self._bucket = settings.object_storage_bucket
        self._client = client or Minio(
            endpoint,
            access_key=settings.object_storage_access_key,
            secret_key=settings.object_storage_secret_key.get_secret_value(),
            secure=parsed.scheme == "https",
            region=settings.object_storage_region,
        )

    def ensure_bucket(self) -> None:
        if not self._client.bucket_exists(self._bucket):
            self._client.make_bucket(self._bucket)

    def put_bytes(self, key: str, content: bytes, content_type: str) -> StoredObject:
        normalized_key = _validate_key(key)
        result = self._client.put_object(
            self._bucket,
            normalized_key,
            BytesIO(content),
            length=len(content),
            content_type=content_type,
        )
        return StoredObject(
            bucket=self._bucket,
            key=normalized_key,
            etag=getattr(result, "etag", None),
            version_id=getattr(result, "version_id", None),
        )

    def get_bytes(self, key: str) -> bytes:
        response = self._client.get_object(self._bucket, _validate_key(key))
        try:
            return response.read()
        finally:
            response.close()
            response.release_conn()

    def exists(self, key: str) -> bool:
        try:
            self._client.stat_object(self._bucket, _validate_key(key))
        except S3Error as error:
            if error.code in {"NoSuchKey", "NoSuchObject", "NotFound"}:
                return False
            raise
        return True


def _validate_key(key: str) -> str:
    normalized = key.strip().replace("\\", "/")
    if not normalized or normalized.startswith("/") or ".." in normalized.split("/"):
        raise ValueError("Object key must be a non-empty relative path without parent traversal")
    return normalized
