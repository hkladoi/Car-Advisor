from ingestion.change_detection import AnomalyPolicy
from ingestion.contracts import Confidence, FactStatus
from ingestion.extraction import CandidateFact, SupportedField


def candidate(
    field: SupportedField,
    value: str,
    *,
    confidence: Confidence = Confidence.VERIFIED_OFFICIAL,
    conflict: bool = False,
) -> CandidateFact:
    return CandidateFact(
        field_path=field,
        raw_value=value,
        normalized_value=value,
        original_unit="mm" if field is not SupportedField.MSRP else "VND",
        canonical_unit="mm" if field is not SupportedField.MSRP else "VND",
        status=FactStatus.OFFICIAL,
        confidence=confidence,
        confidence_score=0.98,
        extraction_method="deterministic_anchor",
        extraction_context="official specification table",
        conflict=conflict,
    )


def test_verified_official_dimension_within_three_percent_is_safe_to_auto_publish() -> None:
    result = AnomalyPolicy().assess(
        candidate(SupportedField.LENGTH, "4320"), "4310", False, True
    )

    assert result.risk_level == "Low"
    assert result.auto_publish is True
    assert result.relative_delta is not None and result.relative_delta < 0.03


def test_price_change_over_thirty_percent_is_critical_and_never_auto_publishes() -> None:
    result = AnomalyPolicy().assess(
        candidate(SupportedField.MSRP, "1000000000"), "650000000", False, True
    )

    assert result.risk_level == "Critical"
    assert result.anomaly_code == "PRICE_DELTA_OVER_30_PERCENT"
    assert result.auto_publish is False


def test_initial_price_is_high_risk() -> None:
    result = AnomalyPolicy().assess(
        candidate(SupportedField.MSRP, "650000000"), None, False, True
    )

    assert result.risk_level == "High"
    assert result.anomaly_code == "NEW_PRICE_VALUE"
    assert result.auto_publish is False


def test_field_lock_has_priority_over_safe_change() -> None:
    result = AnomalyPolicy().assess(
        candidate(SupportedField.WIDTH, "1801"), "1800", True, True
    )

    assert result.risk_level == "High"
    assert result.anomaly_code == "FIELD_LOCKED"
    assert result.auto_publish is False


def test_unresolved_entity_and_conflicting_fact_are_reviewed() -> None:
    unresolved = AnomalyPolicy().assess(
        candidate(SupportedField.HEIGHT, "1600"), None, False, False
    )
    conflict = AnomalyPolicy().assess(
        candidate(SupportedField.HEIGHT, "1600", conflict=True), "1590", False, True
    )

    assert unresolved.risk_level == "Critical"
    assert unresolved.anomaly_code == "ENTITY_UNRESOLVED"
    assert conflict.risk_level == "High"
    assert conflict.anomaly_code == "SOURCE_VALUE_CONFLICT"
