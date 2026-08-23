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
