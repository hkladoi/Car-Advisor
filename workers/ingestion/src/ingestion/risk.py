from __future__ import annotations

from enum import StrEnum
from typing import Any


class ChangeRisk(StrEnum):
    LOW = "Low"
    MEDIUM = "Medium"
    HIGH = "High"
    CRITICAL = "Critical"


def classify_change(field_path: str, old_value: Any, new_value: Any) -> ChangeRisk:
    if old_value == new_value:
        return ChangeRisk.LOW
    if field_path in {
        "price",
        "market_status",
        "fuel_price",
        "electricity_price",
        "charging_tariff",
        "charging_promotion",
        "energy_source_content_hash",
    }:
        return ChangeRisk.HIGH
    if field_path == "powertrain":
        return ChangeRisk.CRITICAL
    if field_path in {"body_type", "segment", "seats"}:
        return ChangeRisk.MEDIUM
    return ChangeRisk.LOW


def requires_human_review(risk: ChangeRisk) -> bool:
    return risk in {ChangeRisk.HIGH, ChangeRisk.CRITICAL}
