from __future__ import annotations

import hashlib
from dataclasses import dataclass
from datetime import UTC, datetime
from pathlib import PurePosixPath

import httpx
from tenacity import AsyncRetrying, retry_if_exception_type, stop_after_attempt, wait_exponential_jitter

from ingestion.registry import ContentType, RegistrySource
from ingestion.storage import ObjectStorage


@dataclass(frozen=True, slots=True)
class Snapshot:
    source_id: str
    source_url: str
    final_url: str
    fetched_at: datetime
    content_hash: str
    object_key: str
    http_status: int
    content_type: str
    etag: str | None
    last_modified: str | None
    size_bytes: int
    fetch_method: str


class KnownUrlFetcher:
    def __init__(
        self,
        user_agent: str,
        timeout_seconds: float = 30.0,
        transport: httpx.AsyncBaseTransport | None = None,
    ) -> None:
        self._user_agent = user_agent
        self._timeout = timeout_seconds
        self._transport = transport

    async def fetch(self, source: RegistrySource, storage: ObjectStorage) -> Snapshot:
        if not source.automated_fetch:
            raise ValueError(f"Automated fetch is disabled for {source.id}")
        if not source.allows_url(source.url):
            raise ValueError(f"Source URL is outside the allowlist: {source.url}")

        try:
            response = await self._fetch_http(source.url)
            final_url = str(response.url)
            content = response.content
            status = response.status_code
            headers = response.headers
            fetch_method = "http"
        except (httpx.TransportError, httpx.HTTPStatusError):
            if source.content_type is not ContentType.HTML or self._transport is not None:
                raise
            final_url, content, status, headers = await self._fetch_browser(source.url)
            fetch_method = "playwright"
        if not source.allows_url(final_url):
            raise ValueError(f"Redirect escaped source allowlist: {final_url}")

        digest = hashlib.sha256(content).hexdigest()
        fetched_at = datetime.now(UTC)
        extension = _extension(source.content_type)
        object_key = str(PurePosixPath("sources", source.id, "sha256", f"{digest}.{extension}"))

        storage.ensure_bucket()
        if not storage.exists(object_key):
            storage.put_bytes(
                object_key,
                content,
                headers.get("content-type", "application/octet-stream").split(";", 1)[0],
            )

        return Snapshot(
            source_id=source.id,
            source_url=source.url,
            final_url=final_url,
            fetched_at=fetched_at,
            content_hash=digest,
            object_key=object_key,
            http_status=status,
            content_type=headers.get("content-type", "application/octet-stream"),
            etag=headers.get("etag"),
            last_modified=headers.get("last-modified"),
            size_bytes=len(content),
            fetch_method=fetch_method,
        )

    async def _fetch_http(self, url: str) -> httpx.Response:
        response: httpx.Response | None = None
        async for attempt in AsyncRetrying(
            stop=stop_after_attempt(3),
            wait=wait_exponential_jitter(initial=0.25, max=2),
            retry=retry_if_exception_type((httpx.TransportError, httpx.HTTPStatusError)),
            reraise=True,
        ):
            with attempt:
                async with httpx.AsyncClient(
                    headers={"User-Agent": self._user_agent, "Accept": "text/html,application/pdf,application/json,application/xml;q=0.9,*/*;q=0.8"},
                    follow_redirects=True,
                    timeout=self._timeout,
                    transport=self._transport,
                ) as client:
                    response = await client.get(url)
                response.raise_for_status()
        if response is None:
            raise RuntimeError("Fetcher completed without a response")
        return response

    async def _fetch_browser(self, url: str) -> tuple[str, bytes, int, dict[str, str]]:
        from playwright.async_api import async_playwright

        async with async_playwright() as playwright:
            browser = await playwright.chromium.launch(headless=True)
            try:
                context = await browser.new_context(user_agent=self._user_agent)
                page = await context.new_page()
                response = await page.goto(
                    url,
                    wait_until="domcontentloaded",
                    timeout=int(self._timeout * 1000),
                )
                if response is None:
                    raise httpx.TransportError("Browser navigation returned no response")
                if response.status >= 400:
                    raise httpx.HTTPStatusError(
                        f"Browser fetch returned HTTP {response.status}",
                        request=httpx.Request("GET", page.url),
                        response=httpx.Response(response.status),
                    )
                content = (await page.content()).encode("utf-8")
                headers = await response.all_headers()
                headers["content-type"] = "text/html; charset=utf-8"
                return page.url, content, response.status, headers
            finally:
                await browser.close()


def _extension(content_type: ContentType) -> str:
    return {
        ContentType.HTML: "html",
        ContentType.PDF: "pdf",
        ContentType.JSON: "json",
        ContentType.XML: "xml",
        ContentType.MANUAL_DOCUMENT: "bin",
    }[content_type]
