from __future__ import annotations

from pydantic import Field, SecretStr
from pydantic_settings import BaseSettings, SettingsConfigDict


class Settings(BaseSettings):
    model_config = SettingsConfigDict(case_sensitive=False)

    redis_url: str = "redis://localhost:6379/0"
    ingestion_queue: str = "ingestion:jobs"
    ingestion_user_agent: str = "VietnamCarPlatformBot/0.1"
    ingestion_max_concurrency: int = Field(default=4, ge=1, le=32)
    ingestion_schedule_seconds: int = Field(default=60, ge=10, le=86400)
    source_registry_path: str = "data/source-registry.v1.json"
    snapshot_event_queue: str = "ingestion:snapshots"
    postgres_dsn: str = "host=localhost dbname=vietnam_car_platform user=vcp password=vcp-local-dev"
    object_storage_endpoint: str = "http://localhost:9000"
    object_storage_health_endpoint: str = "http://localhost:9000/minio/health/live"
    object_storage_bucket: str = "vcp-snapshots"
    object_storage_access_key: str = "vcp-local"
    object_storage_secret_key: SecretStr = SecretStr("vcp-local-dev")
    object_storage_region: str = "auto"
    object_storage_force_path_style: bool = True
    brave_search_api_key: SecretStr = SecretStr("")
    brave_monthly_request_budget: int = Field(default=1000, ge=1, le=1_000_000)
    brave_search_endpoint: str = "https://api.search.brave.com/res/v1/web/search"
    brave_search_timeout_seconds: float = Field(default=15.0, ge=1.0, le=60.0)
    brave_discovery_cache_seconds: int = Field(default=86400, ge=60, le=2_592_000)
    brave_discovery_max_queries: int = Field(default=4, ge=1, le=20)
    discovery_query_templates_path: str = "data/discovery-query-templates.v2.json"
    discovery_candidate_queue: str = "ingestion:discovery-candidates"
    parser_registry_path: str = "data/parser-registry.v2.json"
    parsed_document_queue: str = "ingestion:parsed-documents"
    parser_max_content_bytes: int = Field(default=20_000_000, ge=1024, le=100_000_000)
    parser_max_pdf_pages: int = Field(default=500, ge=1, le=5000)
    local_llm_base_url: str = ""
    local_llm_model: str = ""
    local_llm_api_key: SecretStr = SecretStr("")
    local_llm_timeout_seconds: float = Field(default=60.0, ge=1.0, le=300.0)
    local_llm_max_input_chars: int = Field(default=40_000, ge=1000, le=200_000)
    extracted_candidate_queue: str = "ingestion:extracted-candidates"
    parser_failure_alert_threshold: int = Field(default=3, ge=2, le=20)
    open_charge_map_api_key: SecretStr = SecretStr("")
    open_charge_map_timeout_seconds: float = Field(default=15.0, ge=1.0, le=60.0)
    open_charge_map_retries: int = Field(default=3, ge=1, le=5)
    open_charge_map_page_size: int = Field(default=1000, ge=1, le=5000)
    open_charge_map_max_stations: int = Field(default=20_000, ge=1, le=100_000)
    open_charge_map_max_response_bytes: int = Field(default=25_000_000, ge=1024, le=100_000_000)
