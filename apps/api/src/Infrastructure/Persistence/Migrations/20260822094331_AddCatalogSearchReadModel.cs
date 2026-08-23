using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietnamCarPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCatalogSearchReadModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE MATERIALIZED VIEW current_searchable_trims AS
                SELECT
                    t.id AS trim_id,
                    b.id AS brand_id,
                    b.name AS brand_name,
                    b.slug AS brand_slug,
                    m.id AS model_id,
                    m.name AS model_name,
                    m.slug AS model_slug,
                    g.code AS generation_code,
                    my.year AS model_year,
                    t.name AS trim_name,
                    t.slug AS trim_slug,
                    t.market_status,
                    m.body_type,
                    m.segment,
                    COALESCE(pp.type, 'Unknown') AS powertrain_type,
                    BTRIM(CONCAT_WS(
                        ' ',
                        m.search_text,
                        t.search_text,
                        aliases.model_aliases,
                        aliases.trim_aliases,
                        CASE
                            WHEN pp.type = 'Hev' THEN 'hybrid hev'
                            WHEN pp.type = 'Phev' THEN 'hybrid plug in phev'
                            WHEN pp.type = 'Erev' THEN 'electric range extender erev'
                            WHEN pp.type = 'Bev' THEN 'electric ev bev'
                            WHEN pp.type = 'Ice' THEN 'petrol diesel ice'
                            ELSE NULL
                        END
                    )) AS search_text,
                    msrp.amount AS msrp_amount,
                    msrp.currency AS msrp_currency,
                    current_price.amount AS current_price_amount,
                    current_price.price_type AS current_price_type,
                    current_price.currency AS current_price_currency,
                    NULL::numeric(19, 2) AS on_road_min_amount,
                    NULL::numeric(19, 2) AS on_road_max_amount,
                    specifications.seats,
                    specifications.length_mm,
                    specifications.width_mm,
                    specifications.height_mm,
                    specifications.wheelbase_mm,
                    ep.official_range_km,
                    ep.usable_battery_kwh,
                    ep.official_fuel_litres_per100km AS fuel_litres_per100_km,
                    ep.official_electric_kwh_per100km AS electric_kwh_per100_km,
                    features.feature_codes,
                    colors.color_codes,
                    image.storage_url AS primary_image_url,
                    GREATEST(
                        b.updated_at,
                        m.updated_at,
                        t.updated_at,
                        COALESCE(pp.updated_at, t.updated_at),
                        COALESCE(ep.updated_at, t.updated_at),
                        COALESCE(msrp.updated_at, t.updated_at),
                        COALESCE(current_price.updated_at, t.updated_at)
                    ) AS data_updated_at
                FROM trims AS t
                INNER JOIN model_years AS my ON my.id = t.model_year_id
                INNER JOIN generations AS g ON g.id = my.generation_id
                INNER JOIN models AS m ON m.id = g.model_id
                INNER JOIN brands AS b ON b.id = m.brand_id
                LEFT JOIN powertrain_profiles AS pp ON pp.trim_id = t.id
                LEFT JOIN energy_profiles AS ep ON ep.trim_id = t.id
                LEFT JOIN LATERAL (
                    SELECT
                        STRING_AGG(DISTINCT ma.normalized_alias, ' ') AS model_aliases,
                        STRING_AGG(DISTINCT ta.normalized_alias, ' ') AS trim_aliases
                    FROM model_aliases AS ma
                    FULL OUTER JOIN trim_aliases AS ta ON FALSE
                    WHERE ma.model_id = m.id OR ta.trim_id = t.id
                ) AS aliases ON TRUE
                LEFT JOIN LATERAL (
                    SELECT
                        MAX(ts.numeric_value) FILTER (WHERE sd.code = 'SEATS') AS seats,
                        MAX(ts.numeric_value) FILTER (WHERE sd.code = 'LENGTH_MM') AS length_mm,
                        MAX(ts.numeric_value) FILTER (WHERE sd.code = 'WIDTH_MM') AS width_mm,
                        MAX(ts.numeric_value) FILTER (WHERE sd.code = 'HEIGHT_MM') AS height_mm,
                        MAX(ts.numeric_value) FILTER (WHERE sd.code = 'WHEELBASE_MM') AS wheelbase_mm
                    FROM trim_specs AS ts
                    INNER JOIN spec_definitions AS sd ON sd.id = ts.spec_definition_id
                    WHERE ts.trim_id = t.id
                      AND ts.status IN ('Official', 'Expected')
                ) AS specifications ON TRUE
                LEFT JOIN LATERAL (
                    SELECT p.amount, p.currency, p.updated_at
                    FROM prices AS p
                    WHERE p.trim_id = t.id
                      AND p.price_type = 'Msrp'
                      AND p.status = 'Official'
                      AND p.effective_from <= CURRENT_TIMESTAMP
                      AND (p.effective_to IS NULL OR p.effective_to > CURRENT_TIMESTAMP)
                    ORDER BY p.priority ASC, p.version DESC, p.effective_from DESC
                    LIMIT 1
                ) AS msrp ON TRUE
                LEFT JOIN LATERAL (
                    SELECT p.amount, p.currency, p.price_type, p.updated_at
                    FROM prices AS p
                    WHERE p.trim_id = t.id
                      AND p.status = 'Official'
                      AND p.amount IS NOT NULL
                      AND p.effective_from <= CURRENT_TIMESTAMP
                      AND (p.effective_to IS NULL OR p.effective_to > CURRENT_TIMESTAMP)
                    ORDER BY
                        CASE p.price_type
                            WHEN 'PromotionPrice' THEN 0
                            WHEN 'DealerCashPrice' THEN 1
                            WHEN 'Msrp' THEN 2
                            WHEN 'ExpectedPrice' THEN 3
                            ELSE 4
                        END,
                        p.priority ASC,
                        p.version DESC,
                        p.effective_from DESC
                    LIMIT 1
                ) AS current_price ON TRUE
                LEFT JOIN LATERAL (
                    SELECT COALESCE(ARRAY_AGG(fd.code ORDER BY fd.code), ARRAY[]::text[]) AS feature_codes
                    FROM trim_features AS tf
                    INNER JOIN feature_definitions AS fd ON fd.id = tf.feature_definition_id
                    WHERE tf.trim_id = t.id
                      AND tf.status = 'Official'
                      AND (
                          tf.boolean_value IS TRUE
                          OR tf.numeric_value IS NOT NULL
                          OR tf.text_value IS NOT NULL
                          OR tf.enum_value IS NOT NULL
                      )
                ) AS features ON TRUE
                LEFT JOIN LATERAL (
                    SELECT COALESCE(ARRAY_AGG(c.code ORDER BY c.code), ARRAY[]::text[]) AS color_codes
                    FROM trim_colors AS tc
                    INNER JOIN colors AS c ON c.id = tc.color_id
                    WHERE tc.trim_id = t.id
                      AND tc.availability = 'Available'
                ) AS colors ON TRUE
                LEFT JOIN LATERAL (
                    SELECT vi.storage_url
                    FROM vehicle_images AS vi
                    WHERE (vi.trim_id = t.id OR vi.model_id = m.id)
                      AND vi.storage_url IS NOT NULL
                      AND vi.rights_status IN ('Owned', 'Licensed', 'OfficialPressKit', 'Permitted')
                    ORDER BY
                        CASE WHEN vi.trim_id = t.id THEN 0 ELSE 1 END,
                        CASE vi.type WHEN 'Hero' THEN 0 WHEN 'Exterior' THEN 1 ELSE 2 END,
                        vi.created_at
                    LIMIT 1
                ) AS image ON TRUE
                WHERE b.active
                  AND t.market_status IN ('Active', 'Announced', 'Upcoming')
                WITH DATA;

                CREATE UNIQUE INDEX ux_current_searchable_trims_trim_id
                    ON current_searchable_trims (trim_id);
                CREATE INDEX ix_current_searchable_trims_search_text_trgm
                    ON current_searchable_trims USING gin (search_text gin_trgm_ops);
                CREATE INDEX ix_current_searchable_trims_facets
                    ON current_searchable_trims (brand_slug, model_slug, body_type, segment, powertrain_type);
                CREATE INDEX ix_current_searchable_trims_prices
                    ON current_searchable_trims (current_price_amount, msrp_amount);
                CREATE INDEX ix_current_searchable_trims_dimensions
                    ON current_searchable_trims (seats, length_mm, width_mm, height_mm);
                CREATE INDEX ix_current_searchable_trims_features
                    ON current_searchable_trims USING gin (feature_codes);
                CREATE INDEX ix_current_searchable_trims_colors
                    ON current_searchable_trims USING gin (color_codes);

                CREATE FUNCTION refresh_current_searchable_trims()
                RETURNS void
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    REFRESH MATERIALIZED VIEW current_searchable_trims;
                END;
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP FUNCTION IF EXISTS refresh_current_searchable_trims();
                DROP MATERIALIZED VIEW IF EXISTS current_searchable_trims;
                """);
        }
    }
}
