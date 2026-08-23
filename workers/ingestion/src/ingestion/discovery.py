from __future__ import annotations

import hashlib
import ipaddress
import json
import re
from dataclasses import dataclass
from datetime import UTC, datetime, timedelta
from pathlib import Path
from typing import Any, Protocol
from urllib.parse import parse_qsl, urlencode, urlsplit, urlunsplit

import httpx
from pydantic import BaseModel, ConfigDict, Field, model_validator
from tenacity import AsyncRetrying, retry_if_exception, stop_after_attempt, wait_exponential_jitter


class DiscoveryError(RuntimeError):
    """Base error for discovery failures that must not affect published data."""


class MissingBraveApiKey(DiscoveryError):
    pass


class BraveBudgetExceeded(DiscoveryError):
    pass


class UnsafeDiscoveryUrl(DiscoveryError):
    pass


class AsyncDiscoveryCache(Protocol):
    async def get(self, name: str) -> str | None: ...

    async def set(self, name: str, value: str, *, ex: int) -> Any: ...

    async def eval(self, script: str, numkeys: int, *keys_and_args: Any) -> Any: ...


class QueryTemplateCatalog(BaseModel):
    model_config = ConfigDict(extra="forbid")

    schema_version: str
    templates: dict[str, str] = Field(min_length=1)

    @model_validator(mode="after")
    def validate_placeholders(self) -> "QueryTemplateCatalog":
        for data_type, template in self.templates.items():
            if not re.fullmatch(r"[a-z][a-z0-9_]*", data_type):
                raise ValueError(f"Invalid discovery data type: {data_type}")
            if "{brand}" not in template or "{domain}" not in template:
                raise ValueError(f"Template {data_type} must contain brand and domain placeholders")
        return self

    @classmethod
    def load(cls, path: Path) -> "QueryTemplateCatalog":
        return cls.model_validate_json(path.read_text(encoding="utf-8"))

    def queries(self, brand: str, data_type: str, domains: list[str]) -> list[str]:
        normalized_brand = " ".join(brand.split())
        if not normalized_brand:
            raise ValueError("brand is required")
        try:
            template = self.templates[data_type]
        except KeyError as error:
            raise ValueError(f"Unsupported discovery data type: {data_type}") from error
        queries = [
            " ".join(template.format(brand=normalized_brand, domain=domain).split())
            for domain in deduplicate_domains(domains)
        ]
        for query in queries:
            if len(query) > 400 or len(query.split()) > 50:
                raise ValueError("Brave query exceeds the provider limit")
        return list(dict.fromkeys(queries))


class DiscoveryRequest(BaseModel):
    model_config = ConfigDict(extra="forbid")

    brand: str = Field(min_length=1, max_length=160)
    data_type: str = Field(pattern=r"^[a-z][a-z0-9_]*$")
    allowed_domains: list[str] = Field(min_length=1)
    known_urls: list[str] = Field(default_factory=list)
    force_discovery: bool = False

    @model_validator(mode="after")
    def normalize_scope(self) -> "DiscoveryRequest":
        self.brand = " ".join(self.brand.split())
        self.allowed_domains = deduplicate_domains(self.allowed_domains)
        self.known_urls = deduplicate_urls(self.known_urls, self.allowed_domains)
        return self


class DiscoveryCandidate(BaseModel):
    model_config = ConfigDict(extra="forbid")

    url: str
    domain: str
    provider: str
    query: str | None = None
    rank: int = Field(ge=0)
    discovered_at: datetime


class DiscoveryPage(BaseModel):
    model_config = ConfigDict(extra="forbid")

    query: str
    candidates: list[DiscoveryCandidate]
    cache_hit: bool
    charged_requests: int


class DiscoveryBatch(BaseModel):
    model_config = ConfigDict(extra="forbid")

    brand: str
    data_type: str
    strategy: str
    queries: list[str]
    domains: list[str]
    candidates: list[DiscoveryCandidate]
    cache_hits: int
    charged_requests: int


@dataclass(frozen=True, slots=True)
class BraveSearchOptions:
    api_key: str
    monthly_request_budget: int
    endpoint: str = "https://api.search.brave.com/res/v1/web/search"
    timeout_seconds: float = 15.0
    cache_seconds: int = 86400
    country: str = "VN"
    search_lang: str = "vi"
    count: int = 20


_BUDGET_SCRIPT = """
local current = tonumber(redis.call('GET', KEYS[1]) or '0')
local budget = tonumber(ARGV[1])
if current >= budget then
  return -1
end
current = redis.call('INCR', KEYS[1])
redis.call('EXPIREAT', KEYS[1], tonumber(ARGV[2]))
return current
"""


class BraveSearchClient:
    def __init__(
        self,
        cache: AsyncDiscoveryCache,
        options: BraveSearchOptions,
        transport: httpx.AsyncBaseTransport | None = None,
    ) -> None:
        self._cache = cache
        self._options = options
        self._transport = transport

    async def search(self, query: str, allowed_domains: list[str]) -> DiscoveryPage:
        domains = deduplicate_domains(allowed_domains)
        cache_key = self._cache_key(query, domains)
        cached = await self._cache.get(cache_key)
        if cached is not None:
            candidates = [DiscoveryCandidate.model_validate(item) for item in json.loads(cached)]
            return DiscoveryPage(
                query=query,
                candidates=candidates,
                cache_hit=True,
                charged_requests=0,
            )

        api_key = self._options.api_key.strip()
        if not api_key:
            raise MissingBraveApiKey(
                "BRAVE_SEARCH_API_KEY is required for a discovery cache miss"
            )
        charged_requests = 0
        response: httpx.Response | None = None
        async with httpx.AsyncClient(
            transport=self._transport,
            timeout=self._options.timeout_seconds,
            headers={
                "Accept": "application/json",
                "Accept-Encoding": "gzip",
                "X-Subscription-Token": api_key,
            },
        ) as client:
            async for attempt in AsyncRetrying(
                stop=stop_after_attempt(3),
                wait=wait_exponential_jitter(initial=0.25, max=2),
                retry=retry_if_exception(_is_retryable_brave_error),
                reraise=True,
            ):
                with attempt:
                    await self._reserve_budget()
                    charged_requests += 1
                    response = await client.get(
                        self._options.endpoint,
                        params={
                            "q": query,
                            "country": self._options.country,
                            "search_lang": self._options.search_lang,
                            "safesearch": "strict",
                            "count": self._options.count,
                        },
                    )
                    response.raise_for_status()
        if response is None:
            raise DiscoveryError("Brave request completed without a response")
        payload = response.json()

        now = datetime.now(UTC)
        candidates: list[DiscoveryCandidate] = []
        seen_urls: set[str] = set()
        results = payload.get("web", {}).get("results", [])
        if not isinstance(results, list):
            raise DiscoveryError("Brave response web.results must be a list")
        for rank, result in enumerate(results):
            if not isinstance(result, dict) or not isinstance(result.get("url"), str):
                continue
            try:
                url = normalize_discovery_url(result["url"], domains)
            except UnsafeDiscoveryUrl:
                continue
            if url in seen_urls:
                continue
            seen_urls.add(url)
            candidates.append(
                DiscoveryCandidate(
                    url=url,
                    domain=urlsplit(url).hostname or "",
                    provider="brave",
                    query=query,
                    rank=rank,
                    discovered_at=now,
                )
            )

        # Deliberately cache only normalized URL candidates. Brave snippets and
        # third-party page text are discarded and cannot become product facts.
        await self._cache.set(
            cache_key,
            json.dumps([item.model_dump(mode="json") for item in candidates]),
            ex=self._options.cache_seconds,
        )
        return DiscoveryPage(
            query=query,
            candidates=candidates,
            cache_hit=False,
            charged_requests=charged_requests,
        )

    async def _reserve_budget(self) -> int:
        now = datetime.now(UTC)
        next_month = (now.replace(day=28) + timedelta(days=4)).replace(
            day=1, hour=0, minute=0, second=0, microsecond=0
        )
        key = f"ingestion:brave:requests:{now:%Y-%m}"
        usage = int(
            await self._cache.eval(
                _BUDGET_SCRIPT,
                1,
                key,
                self._options.monthly_request_budget,
                int((next_month + timedelta(days=7)).timestamp()),
            )
        )
        if usage < 0:
            raise BraveBudgetExceeded(
                f"Brave monthly request budget of {self._options.monthly_request_budget} is exhausted"
            )
        return usage

    def _cache_key(self, query: str, domains: list[str]) -> str:
        fingerprint = json.dumps(
            {
                "q": query,
                "domains": domains,
                "country": self._options.country,
                "lang": self._options.search_lang,
                "count": self._options.count,
            },
            ensure_ascii=False,
            sort_keys=True,
        )
        digest = hashlib.sha256(fingerprint.encode("utf-8")).hexdigest()
        return f"ingestion:brave:cache:v2.1:{digest}"


class DiscoveryService:
    def __init__(
        self,
        client: BraveSearchClient,
        templates: QueryTemplateCatalog,
        max_queries: int = 4,
    ) -> None:
        self._client = client
        self._templates = templates
        self._max_queries = max_queries

    async def discover(self, request: DiscoveryRequest) -> DiscoveryBatch:
        if request.known_urls and not request.force_discovery:
            now = datetime.now(UTC)
            candidates = [
                DiscoveryCandidate(
                    url=url,
                    domain=urlsplit(url).hostname or "",
                    provider="registry",
                    rank=rank,
                    discovered_at=now,
                )
                for rank, url in enumerate(request.known_urls)
            ]
            return DiscoveryBatch(
                brand=request.brand,
                data_type=request.data_type,
                strategy="known_url_first",
                queries=[],
                domains=sorted({candidate.domain for candidate in candidates}),
                candidates=candidates,
                cache_hits=0,
                charged_requests=0,
            )

        queries = self._templates.queries(
            request.brand, request.data_type, request.allowed_domains
        )[: self._max_queries]
        pages = [await self._client.search(query, request.allowed_domains) for query in queries]
        unique: dict[str, DiscoveryCandidate] = {}
        for page in pages:
            for candidate in page.candidates:
                unique.setdefault(candidate.url, candidate)
        candidates = list(unique.values())
        return DiscoveryBatch(
            brand=request.brand,
            data_type=request.data_type,
            strategy="brave_discovery",
            queries=queries,
            domains=sorted({candidate.domain for candidate in candidates}),
            candidates=candidates,
            cache_hits=sum(1 for page in pages if page.cache_hit),
            charged_requests=sum(page.charged_requests for page in pages),
        )


def deduplicate_domains(domains: list[str]) -> list[str]:
    normalized: list[str] = []
    seen: set[str] = set()
    for raw_domain in domains:
        domain = raw_domain.strip().lower().strip(".")
        try:
            domain = domain.encode("idna").decode("ascii")
        except UnicodeError as error:
            raise UnsafeDiscoveryUrl(f"Invalid domain: {raw_domain}") from error
        if not domain or "/" in domain or "@" in domain or ":" in domain:
            raise UnsafeDiscoveryUrl(f"Invalid domain: {raw_domain}")
        if domain == "localhost" or domain.endswith(".local") or "." not in domain:
            raise UnsafeDiscoveryUrl(f"Non-public domain is not allowed: {raw_domain}")
        try:
            address = ipaddress.ip_address(domain)
        except ValueError:
            address = None
        if address is not None:
            raise UnsafeDiscoveryUrl(f"IP literals are not valid official domains: {raw_domain}")
        if domain not in seen:
            seen.add(domain)
            normalized.append(domain)
    if not normalized:
        raise UnsafeDiscoveryUrl("At least one official public domain is required")
    return normalized


def deduplicate_urls(urls: list[str], allowed_domains: list[str]) -> list[str]:
    return list(
        dict.fromkeys(normalize_discovery_url(url, allowed_domains) for url in urls)
    )


def normalize_discovery_url(url: str, allowed_domains: list[str]) -> str:
    parsed = urlsplit(url.strip())
    if parsed.scheme.lower() != "https" or not parsed.hostname:
        raise UnsafeDiscoveryUrl("Discovery URLs must be absolute HTTPS URLs")
    if parsed.username or parsed.password:
        raise UnsafeDiscoveryUrl("Discovery URLs cannot contain credentials")
    try:
        port = parsed.port
    except ValueError as error:
        raise UnsafeDiscoveryUrl("Discovery URL has an invalid port") from error
    if port not in (None, 443):
        raise UnsafeDiscoveryUrl("Discovery URLs cannot use a non-HTTPS port")

    hostname = parsed.hostname.lower().strip(".").encode("idna").decode("ascii")
    domains = deduplicate_domains(allowed_domains)
    if not any(hostname == domain or hostname.endswith(f".{domain}") for domain in domains):
        raise UnsafeDiscoveryUrl(f"Discovery URL is outside the official domain scope: {hostname}")

    query = [
        (key, value)
        for key, value in parse_qsl(parsed.query, keep_blank_values=True)
        if not key.lower().startswith("utm_")
        and key.lower() not in {"fbclid", "gclid", "msclkid"}
    ]
    path = re.sub(r"/{2,}", "/", parsed.path or "/")
    if path != "/":
        path = path.rstrip("/")
    return urlunsplit(("https", hostname, path, urlencode(sorted(query)), ""))


def _is_retryable_brave_error(error: BaseException) -> bool:
    if isinstance(error, httpx.TransportError):
        return True
    if isinstance(error, httpx.HTTPStatusError):
        return error.response.status_code == 429 or error.response.status_code >= 500
    return False
