\set ON_ERROR_STOP on

BEGIN;

INSERT INTO brands (id, name, slug, active, created_at, updated_at)
VALUES ('00000000-0000-0000-0000-000000000101', 'Gate Brand', 'gate-brand', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP);

INSERT INTO models (id, brand_id, name, slug, body_type, segment, search_text, created_at, updated_at)
VALUES ('00000000-0000-0000-0000-000000000102', '00000000-0000-0000-0000-000000000101', 'Gate Model', 'gate-model', 'Suv', 'C', 'gate model', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP);

INSERT INTO generations (id, model_id, code, start_year, created_at, updated_at)
VALUES ('00000000-0000-0000-0000-000000000103', '00000000-0000-0000-0000-000000000102', 'GATE-1', 2026, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP);

INSERT INTO model_years (id, generation_id, year, market, created_at, updated_at)
VALUES ('00000000-0000-0000-0000-000000000104', '00000000-0000-0000-0000-000000000103', 2026, 'VN', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP);

INSERT INTO trims (id, model_year_id, name, slug, normalized_key, market_status, search_text, created_at, updated_at)
VALUES ('00000000-0000-0000-0000-000000000105', '00000000-0000-0000-0000-000000000104', 'Gate Trim', 'gate-trim', 'gate-trim', 'Active', 'gate trim', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP);

INSERT INTO feature_definitions (id, code, "group", data_type, label, created_at, updated_at)
VALUES ('00000000-0000-0000-0000-000000000106', 'GATE_BOOLEAN', 'QA', 'Boolean', 'Gate boolean', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP);

DO $gate$
BEGIN
    BEGIN
        INSERT INTO trims (id, model_year_id, name, slug, normalized_key, market_status, search_text, created_at, updated_at)
        VALUES ('00000000-0000-0000-0000-000000000107', '00000000-0000-0000-0000-000000000104', 'Duplicate', 'duplicate', 'gate-trim', 'Active', 'duplicate', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP);
        RAISE EXCEPTION 'unique trim constraint did not reject a duplicate normalized key';
    EXCEPTION WHEN unique_violation THEN
        NULL;
    END;

    BEGIN
        INSERT INTO brand_scopes (id, brand_id, included, reason, effective_from, effective_to, created_at, updated_at)
        VALUES ('00000000-0000-0000-0000-000000000108', '00000000-0000-0000-0000-000000000101', TRUE, 'gate', '2026-08-23T00:00:00Z', '2026-08-22T00:00:00Z', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP);
        RAISE EXCEPTION 'effective-period constraint accepted an inverted range';
    EXCEPTION WHEN check_violation THEN
        NULL;
    END;

    BEGIN
        INSERT INTO trim_features (id, trim_id, feature_definition_id, status, boolean_value, created_at, updated_at)
        VALUES ('00000000-0000-0000-0000-000000000109', '00000000-0000-0000-0000-000000000105', '00000000-0000-0000-0000-000000000106', 'Unknown', FALSE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP);
        RAISE EXCEPTION 'UNKNOWN feature accepted a concrete false value';
    EXCEPTION WHEN check_violation THEN
        NULL;
    END;
END
$gate$;

INSERT INTO trim_features (id, trim_id, feature_definition_id, status, boolean_value, manual_override_reason, created_at, updated_at)
VALUES ('00000000-0000-0000-0000-000000000110', '00000000-0000-0000-0000-000000000105', '00000000-0000-0000-0000-000000000106', 'Official', FALSE, 'V1.1 gate verifies false is not unknown', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP);

INSERT INTO prices (id, trim_id, price_type, amount, currency, region_scope, status, priority, version, effective_from, manual_override_reason, created_at, updated_at)
VALUES ('00000000-0000-0000-0000-000000000111', '00000000-0000-0000-0000-000000000105', 'Msrp', 1000000000, 'VND', 'VN', 'Official', 0, 1, '2026-01-01T00:00:00Z', 'V1.1 gate only', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP);

DO $gate$
BEGIN
    BEGIN
        INSERT INTO prices (id, trim_id, price_type, amount, currency, region_scope, status, priority, version, effective_from, manual_override_reason, created_at, updated_at)
        VALUES ('00000000-0000-0000-0000-000000000112', '00000000-0000-0000-0000-000000000105', 'Msrp', 1100000000, 'VND', 'VN', 'Official', 0, 2, '2026-06-01T00:00:00Z', 'V1.1 gate only', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP);
        RAISE EXCEPTION 'official MSRP exclusion constraint accepted an overlap at the same priority';
    EXCEPTION WHEN exclusion_violation THEN
        NULL;
    END;
END
$gate$;

INSERT INTO prices (id, trim_id, price_type, amount, currency, region_scope, status, priority, version, effective_from, manual_override_reason, created_at, updated_at)
VALUES ('00000000-0000-0000-0000-000000000113', '00000000-0000-0000-0000-000000000105', 'Msrp', 1200000000, 'VND', 'VN', 'Official', 1, 2, '2026-06-01T00:00:00Z', 'Explicit higher priority is permitted by design', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP);

ROLLBACK;

SELECT 'V1.1 schema constraints: PASS' AS result;
