from __future__ import annotations

from redis import Redis


def invalidate_catalog_cache(redis_url: str) -> int:
    """Remove only API catalog keys after a committed reviewed publish."""
    client = Redis.from_url(redis_url, decode_responses=True)
    try:
        keys = list(client.scan_iter(match="vcp:catalog:v1:*", count=200))
        return int(client.unlink(*keys)) if keys else 0
    finally:
        client.close()
