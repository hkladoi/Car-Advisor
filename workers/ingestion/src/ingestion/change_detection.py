from __future__ import annotations

import uuid
from dataclasses import dataclass
from decimal import Decimal, InvalidOperation

import psycopg
from pydantic import BaseModel, ConfigDict, Field

from ingestion.contracts import Confidence
from ingestion.extraction import CandidateFact, CandidateFactRepository, ExtractionBatch, SupportedField


class RiskAssessment(BaseModel):
    model_config = ConfigDict(extra="forbid")

    risk_level: str
    anomaly_code: str | None = None
    relative_delta: float | None = None
    auto_publish: bool = False
    reason: str


class ChangeDetectionOutcome(BaseModel):
    model_config = ConfigDict(extra="forbid")

    detected: int = Field(ge=0)
    auto_published: int = Field(ge=0)
    queued_for_review: int = Field(ge=0)
    unchanged: int = Field(ge=0)


class AnomalyPolicy:
    _DIMENSIONS = {
        SupportedField.LENGTH,
        SupportedField.WIDTH,
        SupportedField.HEIGHT,
        SupportedField.WHEELBASE,
    }

    def assess(
        self,
        fact: CandidateFact,
        old_value: str | None,
        field_locked: bool,
        resolved_trim: bool,
    ) -> RiskAssessment:
        if old_value == fact.normalized_value:
            return RiskAssessment(risk_level="Low", reason="Candidate equals current value")
        if field_locked:
            return RiskAssessment(
                risk_level="High",
                anomaly_code="FIELD_LOCKED",
                reason="Active field lock blocks crawler publication",
            )
        if not resolved_trim:
            return RiskAssessment(
                risk_level="Critical",
                anomaly_code="ENTITY_UNRESOLVED",
                reason="Candidate is not uniquely resolved to a Vietnam trim",
            )
        if fact.conflict:
            return RiskAssessment(
                risk_level="High",
                anomaly_code="SOURCE_VALUE_CONFLICT",
                reason="One snapshot produced conflicting normalized values",
            )
        if fact.field_path is SupportedField.MSRP:
            if old_value is None:
                return RiskAssessment(
                    risk_level="High",
                    anomaly_code="NEW_PRICE_VALUE",
                    reason="Initial MSRP publication is high risk and requires review",
                )
            relative = _relative_delta(old_value, fact.normalized_value)
            return RiskAssessment(
                risk_level="Critical" if relative is not None and relative > 0.30 else "High",
                anomaly_code="PRICE_DELTA_OVER_30_PERCENT" if relative is not None and relative > 0.30 else None,
                relative_delta=relative,
                reason="MSRP changes are high risk and require review",
            )
        if old_value is None:
            return RiskAssessment(
                risk_level="Medium",
                anomaly_code="NEW_FIELD_VALUE",
                reason="A new canonical value requires initial review",
            )
        relative = _relative_delta(old_value, fact.normalized_value)
        if fact.field_path in self._DIMENSIONS:
            if relative is not None and relative > 0.20:
                return RiskAssessment(
                    risk_level="Critical", anomaly_code="DIMENSION_DELTA_OVER_20_PERCENT",
                    relative_delta=relative, reason="Dimension change exceeds the critical threshold",
                )
            if relative is not None and relative > 0.05:
                return RiskAssessment(
                    risk_level="High", anomaly_code="DIMENSION_DELTA_OVER_5_PERCENT",
                    relative_delta=relative, reason="Dimension change exceeds the safe threshold",
                )
            safe = fact.confidence is Confidence.VERIFIED_OFFICIAL and relative is not None and relative <= 0.03
            return RiskAssessment(
                risk_level="Low" if safe else "Medium",
                relative_delta=relative,
                auto_publish=safe,
                reason=(
                    "Verified official dimension changed within the 3% safe policy"
                    if safe else "Dimension change needs review because confidence/delta is outside safe policy"
                ),
            )
        if fact.field_path is SupportedField.SEATS:
            absolute = abs(Decimal(old_value) - Decimal(fact.normalized_value))
            return RiskAssessment(
                risk_level="High" if absolute > 2 else "Medium",
                anomaly_code="SEAT_DELTA_OVER_TWO" if absolute > 2 else None,
                relative_delta=relative,
                reason="Seat-count changes require review",
            )
        critical = relative is not None and relative > 0.30
        return RiskAssessment(
            risk_level="Critical" if critical else "High",
            anomaly_code="TECHNICAL_DELTA_OVER_30_PERCENT" if critical else None,
            relative_delta=relative,
            reason="Powertrain and energy facts require review",
        )


class CandidateChangeRepository:
    _CHANGE_NAMESPACE = uuid.UUID("b46acdfc-d3b2-48c4-8a70-0ac3079e718f")
    _PUBLICATION_NAMESPACE = uuid.UUID("a19e2ddb-ad7c-4439-a55f-c353ef844a1e")

    _SPEC_CODES = {
        SupportedField.LENGTH: "LENGTH_MM",
        SupportedField.WIDTH: "WIDTH_MM",
        SupportedField.HEIGHT: "HEIGHT_MM",
        SupportedField.WHEELBASE: "WHEELBASE_MM",
        SupportedField.SEATS: "SEATS",
    }

    def __init__(self, dsn: str, policy: AnomalyPolicy | None = None) -> None:
        self._dsn = dsn
        self._policy = policy or AnomalyPolicy()

    def detect_and_apply(self, batch: ExtractionBatch) -> ChangeDetectionOutcome:
        detected = auto_published = queued = unchanged = 0
        with psycopg.connect(self._dsn) as connection, connection.transaction(), connection.cursor() as cursor:
            for fact in batch.facts:
                fact_id = CandidateFactRepository.fact_id(batch, fact)
                trim_id = (
                    batch.entity_resolution.entity_id
                    if batch.entity_resolution.status == "resolved_trim"
                    else None
                )
                if trim_id is None:
                    entity_type, entity_id = "SourceFact", fact_id
                    old_value = None
                    before_source_fact_id = None
                    locked = False
                else:
                    entity_type, entity_id = "Trim", trim_id
                    old_value, before_source_fact_id = self._current_value(cursor, trim_id, fact.field_path)
                    locked = self._field_locked(cursor, trim_id, fact.field_path.value)
                if old_value == fact.normalized_value:
                    unchanged += 1
                    continue
                assessment = self._policy.assess(
                    fact, old_value, locked, trim_id is not None
                )
                change_id = uuid.uuid5(
                    self._CHANGE_NAMESPACE,
                    f"{fact_id}|{entity_type}|{entity_id}|{fact.field_path.value}|{old_value}|{fact.normalized_value}",
                )
                status = "AutoPublished" if assessment.auto_publish else "PendingReview"
                cursor.execute(
                    """
                    INSERT INTO data_changes
                        (id, entity_type, entity_id, field_path, old_value, new_value,
                         risk_level, status, detected_at, anomaly_code, detection_context, source_fact_id,
                         reviewed_audit_event_id, created_at, updated_at)
                    VALUES (%s, %s, %s, %s, %s, %s, %s, %s, CURRENT_TIMESTAMP, %s, %s::jsonb, %s, NULL,
                            CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
                    ON CONFLICT (id) DO NOTHING
                    """,
                    (
                        change_id, entity_type, entity_id, fact.field_path.value,
                        old_value, fact.normalized_value, assessment.risk_level, status,
                        assessment.anomaly_code,
                        assessment.model_dump_json(),
                        fact_id,
                    ),
                )
                if cursor.rowcount == 0:
                    continue
                detected += 1
                if assessment.auto_publish and trim_id is not None:
                    self._publish_safe_spec(cursor, trim_id, fact, fact_id)
                    publication_id = uuid.uuid5(self._PUBLICATION_NAMESPACE, str(change_id))
                    cursor.execute(
                        """
                        INSERT INTO publication_versions
                            (id, data_change_id, entity_type, entity_id, field_path,
                             before_value, after_value, before_source_fact_id, source_fact_id, status,
                             published_at, published_by, created_at, updated_at)
                        VALUES (%s, %s, 'Trim', %s, %s, %s, %s, %s, %s, 'Published',
                                CURRENT_TIMESTAMP, 'ingestion-policy', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
                        ON CONFLICT (data_change_id) DO NOTHING
                        """,
                        (
                            publication_id, change_id, trim_id, fact.field_path.value,
                            old_value, fact.normalized_value, before_source_fact_id, fact_id,
                        ),
                    )
                    auto_published += 1
                else:
                    queued += 1
            if auto_published:
                cursor.execute("SELECT refresh_current_searchable_trims()")
        return ChangeDetectionOutcome(
            detected=detected,
            auto_published=auto_published,
            queued_for_review=queued,
            unchanged=unchanged,
        )

    def _current_value(
        self,
        cursor: psycopg.Cursor,
        trim_id: uuid.UUID,
        field: SupportedField,
    ) -> tuple[str | None, uuid.UUID | None]:
        if field is SupportedField.MSRP:
            cursor.execute(
                """
                SELECT amount::text, source_fact_id FROM prices
                WHERE trim_id = %s AND price_type = 'Msrp' AND status = 'Official'
                  AND effective_from <= CURRENT_TIMESTAMP
                  AND (effective_to IS NULL OR effective_to > CURRENT_TIMESTAMP)
                ORDER BY priority DESC, version DESC LIMIT 1
                """,
                (trim_id,),
            )
        elif field in self._SPEC_CODES:
            cursor.execute(
                """
                SELECT ts.numeric_value::text, ts.source_fact_id
                FROM trim_specs ts
                JOIN spec_definitions sd ON sd.id = ts.spec_definition_id
                WHERE ts.trim_id = %s AND sd.code = %s
                LIMIT 1
                """,
                (trim_id, self._SPEC_CODES[field]),
            )
        elif field in {SupportedField.POWER, SupportedField.TORQUE}:
            column = "COALESCE(combined_power_kw, motor_power_kw, engine_power_kw)" if field is SupportedField.POWER else "torque_nm"
            cursor.execute(f"SELECT {column}::text, source_fact_id FROM powertrain_profiles WHERE trim_id = %s LIMIT 1", (trim_id,))
        else:
            column = {
                SupportedField.BATTERY: "usable_battery_kwh",
                SupportedField.RANGE: "official_range_km",
                SupportedField.FUEL_CONSUMPTION: "official_fuel_litres_per100km",
                SupportedField.ELECTRIC_CONSUMPTION: "official_electric_kwh_per100km",
            }[field]
            cursor.execute(f"SELECT {column}::text, source_fact_id FROM energy_profiles WHERE trim_id = %s LIMIT 1", (trim_id,))
        row = cursor.fetchone()
        if not row or row[0] is None:
            return None, row[1] if row else None
        return _canonical_decimal(str(row[0])), row[1]

    @staticmethod
    def _field_locked(cursor: psycopg.Cursor, trim_id: uuid.UUID, field_path: str) -> bool:
        cursor.execute(
            """
            SELECT EXISTS(
                SELECT 1 FROM field_locks
                WHERE entity_type = 'Trim' AND entity_id = %s AND field_path = %s
                  AND active = TRUE AND (expires_at IS NULL OR expires_at > CURRENT_TIMESTAMP)
            )
            """,
            (trim_id, field_path),
        )
        return bool(cursor.fetchone()[0])

    def _publish_safe_spec(
        self,
        cursor: psycopg.Cursor,
        trim_id: uuid.UUID,
        fact: CandidateFact,
        fact_id: uuid.UUID,
    ) -> None:
        code = self._SPEC_CODES.get(fact.field_path)
        if code is None or fact.field_path is SupportedField.SEATS:
            raise RuntimeError("Only dimension specs can use the V2.4 auto-publish policy")
        cursor.execute(
            """
            UPDATE trim_specs ts
            SET numeric_value = %s, original_value = %s, original_unit = %s,
                source_fact_id = %s, manual_override_reason = NULL,
                updated_at = CURRENT_TIMESTAMP
            FROM spec_definitions sd
            WHERE ts.spec_definition_id = sd.id AND ts.trim_id = %s AND sd.code = %s
            """,
            (
                Decimal(fact.normalized_value), fact.raw_value, fact.original_unit,
                fact_id, trim_id, code,
            ),
        )
        if cursor.rowcount != 1:
            raise RuntimeError("Safe auto-publish requires an existing canonical TrimSpec row")


def _relative_delta(old_value: str, new_value: str) -> float | None:
    try:
        old = Decimal(old_value)
        new = Decimal(new_value)
    except InvalidOperation:
        return None
    if old == 0:
        return None
    return float(abs(new - old) / abs(old))


def _canonical_decimal(value: str) -> str:
    decimal = Decimal(value)
    normalized = format(decimal.normalize(), "f")
    return normalized.rstrip("0").rstrip(".") if "." in normalized else normalized
