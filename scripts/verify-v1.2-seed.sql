\set ON_ERROR_STOP on

DO $gate$
DECLARE
    brand_count integer;
    trim_count integer;
    bad_price_count integer;
    bad_core_count integer;
    bad_snapshot_count integer;
    duplicate_count integer;
    required_registry_count integer;
BEGIN
    SELECT COUNT(DISTINCT b.id), COUNT(DISTINCT t.id)
    INTO brand_count, trim_count
    FROM brands b
    JOIN models m ON m.brand_id = b.id
    JOIN generations g ON g.model_id = m.id
    JOIN model_years my ON my.generation_id = g.id AND my.market = 'VN'
    JOIN trims t ON t.model_year_id = my.id;

    IF brand_count < 10 OR brand_count > 15 THEN
        RAISE EXCEPTION 'V1.2 seed brand count must be 10-15, found %', brand_count;
    END IF;
    IF trim_count < brand_count THEN
        RAISE EXCEPTION 'Every seeded brand must have at least one trim';
    END IF;

    SELECT COUNT(*) INTO bad_price_count
    FROM trims t
    WHERE NOT EXISTS (
        SELECT 1
        FROM prices p
        WHERE p.trim_id = t.id
          AND p.effective_from <= CURRENT_TIMESTAMP
          AND p.effective_to IS NULL
          AND (
              (p.price_type = 'Msrp' AND p.status = 'Official' AND p.amount > 0)
              OR (p.price_type = 'Unannounced' AND p.status IN ('Unknown', 'Official') AND p.amount IS NULL)
          )
    );
    IF bad_price_count > 0 THEN
        RAISE EXCEPTION '% seeded trims lack official MSRP or explicit Unknown/official Unannounced status', bad_price_count;
    END IF;

    SELECT COUNT(*) INTO bad_core_count
    FROM trims t
    LEFT JOIN LATERAL (
        SELECT
            COUNT(*) AS fact_count,
            COUNT(*) FILTER (
                WHERE (sf.status IN ('Official', 'Expected') AND sf.normalized_value IS NOT NULL AND sf.confidence <> 'Unknown')
                   OR (sf.status IN ('Unknown', 'NotAvailable', 'NotApplicable') AND sf.normalized_value IS NULL AND sf.confidence = 'Unknown')
            ) AS transparent_count
        FROM source_facts sf
        WHERE sf.entity_id = t.id AND sf.entity_type = 'Trim' AND sf.field_path LIKE 'core.%'
    ) facts ON TRUE
    WHERE facts.fact_count < 10
       OR facts.transparent_count::decimal / NULLIF(facts.fact_count, 0) < 0.90;
    IF bad_core_count > 0 THEN
        RAISE EXCEPTION '% seeded trims fail the >=90%% core transparency gate', bad_core_count;
    END IF;

    SELECT COUNT(*) INTO bad_snapshot_count
    FROM source_snapshots ss
    JOIN sources s ON s.id = ss.source_id
    WHERE ss.http_status NOT BETWEEN 200 AND 299
       OR ss.content_hash !~ '^[0-9a-f]{64}$'
       OR ss.object_key NOT LIKE 'sources/%/sha256/%';
    IF bad_snapshot_count > 0 THEN
        RAISE EXCEPTION '% source snapshots are not valid immutable 2xx artifacts', bad_snapshot_count;
    END IF;

    SELECT COUNT(*) INTO required_registry_count
    FROM sources
    WHERE url IN (
        'https://moit.gov.vn/van-ban-phap-luat/van-ban-dieu-hanh',
        'https://dms.gov.vn/x%C4%83ng-d%E1%BA%A7u',
        'https://www.evn.com.vn/',
        'https://vgreen.net/'
    );
    IF required_registry_count <> 4 THEN
        RAISE EXCEPTION 'MOIT/DMS/EVN/V-Green official registry is incomplete';
    END IF;

    SELECT COUNT(*) INTO duplicate_count
    FROM (
        SELECT my.id, lower(unaccent(m.name)), lower(unaccent(t.name)), COUNT(*)
        FROM trims t
        JOIN model_years my ON my.id = t.model_year_id
        JOIN generations g ON g.id = my.generation_id
        JOIN models m ON m.id = g.model_id
        GROUP BY my.id, lower(unaccent(m.name)), lower(unaccent(t.name))
        HAVING COUNT(*) > 1
    ) duplicates;
    IF duplicate_count > 0 THEN
        RAISE EXCEPTION 'Found % obvious normalized model/trim duplicates', duplicate_count;
    END IF;
END
$gate$;

SELECT
    (SELECT COUNT(*) FROM brands) AS brands,
    (SELECT COUNT(*) FROM trims) AS trims,
    (SELECT COUNT(*) FROM sources) AS sources,
    (SELECT COUNT(*) FROM source_snapshots) AS snapshots,
    (SELECT COUNT(*) FROM source_facts WHERE field_path LIKE 'core.%') AS core_facts,
    (SELECT COUNT(*) FROM prices WHERE status = 'Official') AS official_prices,
    (SELECT COUNT(*) FROM data_changes WHERE status = 'Approved') AS reviewed_changes,
    'V1.2 seed gate: PASS' AS result;
