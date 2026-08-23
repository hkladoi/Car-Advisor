from __future__ import annotations

import asyncio

import httpx
import redis.asyncio as redis

from ingestion.settings import Settings


async def check() -> None:
    settings = Settings()
    client = redis.from_url(settings.redis_url)
    try:
        await asyncio.wait_for(client.ping(), timeout=2)
        async with httpx.AsyncClient(timeout=2) as http:
            response = await http.get(settings.object_storage_health_endpoint)
            response.raise_for_status()
    finally:
        await client.aclose()


def main() -> None:
    asyncio.run(check())


if __name__ == "__main__":
    main()

