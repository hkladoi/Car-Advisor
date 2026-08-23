using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietnamCarPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialDomain : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:btree_gist", ",,")
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,")
                .Annotation("Npgsql:PostgresExtension:unaccent", ",,");

            migrationBuilder.CreateTable(
                name: "affordability_profiles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_subject_id = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    name = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    net_monthly_income = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: false),
                    rent_housing = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: false),
                    essential_expenses = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: false),
                    other_fixed_debt = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: false),
                    savings_target = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: false),
                    monthly_kilometres = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    parking_monthly = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: false),
                    household_base_kwh = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    region_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    policy = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    assumptions_json = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_affordability_profiles", x => x.id);
                    table.CheckConstraint("ck_affordability_profiles_nonnegative", "net_monthly_income >= 0 AND rent_housing >= 0 AND essential_expenses >= 0 AND other_fixed_debt >= 0 AND savings_target >= 0 AND monthly_kilometres >= 0 AND parking_monthly >= 0 AND household_base_kwh >= 0");
                });

            migrationBuilder.CreateTable(
                name: "audit_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    action = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    entity_type = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    before_json = table.Column<string>(type: "jsonb", nullable: true),
                    after_json = table.Column<string>(type: "jsonb", nullable: true),
                    reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    correlation_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_events", x => x.id);
                    table.CheckConstraint("ck_audit_events_reason", "NULLIF(BTRIM(reason), '') IS NOT NULL");
                });

            migrationBuilder.CreateTable(
                name: "brands",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    slug = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    country_code = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    official_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_brands", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "charging_providers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    slug = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    network_type = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    official_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_charging_providers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "colors",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    hex_hint = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: true),
                    type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_colors", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "feature_definitions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    group = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    data_type = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    minimum_numeric_value = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    maximum_numeric_value = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_feature_definitions", x => x.id);
                    table.CheckConstraint("ck_feature_definitions_numeric_range", "maximum_numeric_value IS NULL OR minimum_numeric_value IS NULL OR minimum_numeric_value <= maximum_numeric_value");
                });

            migrationBuilder.CreateTable(
                name: "regions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    type = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    area_class = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    parent_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_regions", x => x.id);
                    table.UniqueConstraint("ak_regions_code", x => x.code);
                    table.ForeignKey(
                        name: "fk_regions_regions_parent_code",
                        column: x => x.parent_code,
                        principalTable: "regions",
                        principalColumn: "code",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sources",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    domain = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    authority_level = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    content_type = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    robots_note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    terms_note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    refresh_interval = table.Column<TimeSpan>(type: "interval", nullable: false),
                    last_fetched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sources", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "spec_definitions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    data_type = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    canonical_unit = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    group = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    minimum_numeric_value = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    maximum_numeric_value = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_spec_definitions", x => x.id);
                    table.CheckConstraint("ck_spec_definitions_numeric_range", "maximum_numeric_value IS NULL OR minimum_numeric_value IS NULL OR minimum_numeric_value <= maximum_numeric_value");
                });

            migrationBuilder.CreateTable(
                name: "brand_scopes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    brand_id = table.Column<Guid>(type: "uuid", nullable: false),
                    included = table.Column<bool>(type: "boolean", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    effective_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    effective_to = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_brand_scopes", x => x.id);
                    table.CheckConstraint("ck_brand_scopes_effective_period", "effective_to IS NULL OR effective_from < effective_to");
                    table.ForeignKey(
                        name: "fk_brand_scopes_brands_brand_id",
                        column: x => x.brand_id,
                        principalTable: "brands",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "dealers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    brand_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    slug = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    official_status = table.Column<bool>(type: "boolean", nullable: false),
                    official_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dealers", x => x.id);
                    table.ForeignKey(
                        name: "fk_dealers_brands_brand_id",
                        column: x => x.brand_id,
                        principalTable: "brands",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "models",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    brand_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    slug = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    body_type = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    segment = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    search_text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_models", x => x.id);
                    table.ForeignKey(
                        name: "fk_models_brands_brand_id",
                        column: x => x.brand_id,
                        principalTable: "brands",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "source_snapshots",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fetched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    content_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    object_key = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    http_status = table.Column<int>(type: "integer", nullable: false),
                    parser_version = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    etag = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    last_modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    fetch_error = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_source_snapshots", x => x.id);
                    table.CheckConstraint("ck_source_snapshots_http_status", "http_status BETWEEN 0 AND 599");
                    table.CheckConstraint("ck_source_snapshots_object_key", "object_key <> ''");
                    table.ForeignKey(
                        name: "fk_source_snapshots_sources_source_id",
                        column: x => x.source_id,
                        principalTable: "sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "dealer_branches",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    dealer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    province_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    address = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    latitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    longitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dealer_branches", x => x.id);
                    table.CheckConstraint("ck_dealer_branches_latitude", "latitude IS NULL OR latitude BETWEEN -90 AND 90");
                    table.CheckConstraint("ck_dealer_branches_longitude", "longitude IS NULL OR longitude BETWEEN -180 AND 180");
                    table.ForeignKey(
                        name: "fk_dealer_branches_dealers_dealer_id",
                        column: x => x.dealer_id,
                        principalTable: "dealers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_dealer_branches_regions_province_code",
                        column: x => x.province_code,
                        principalTable: "regions",
                        principalColumn: "code",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "generations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    model_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    start_year = table.Column<int>(type: "integer", nullable: false),
                    end_year = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_generations", x => x.id);
                    table.CheckConstraint("ck_generations_year_range", "end_year IS NULL OR start_year <= end_year");
                    table.ForeignKey(
                        name: "fk_generations_models_model_id",
                        column: x => x.model_id,
                        principalTable: "models",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "model_aliases",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    model_id = table.Column<Guid>(type: "uuid", nullable: false),
                    alias = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    normalized_alias = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_model_aliases", x => x.id);
                    table.ForeignKey(
                        name: "fk_model_aliases_models_model_id",
                        column: x => x.model_id,
                        principalTable: "models",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_model_aliases_sources_source_id",
                        column: x => x.source_id,
                        principalTable: "sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "source_facts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    snapshot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entity_type = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: true),
                    field_path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    raw_value = table.Column<string>(type: "text", nullable: true),
                    normalized_value = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    confidence = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    extraction_context = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_source_facts", x => x.id);
                    table.CheckConstraint("ck_source_facts_value_semantics", "((status IN ('Expected', 'Official') AND normalized_value IS NOT NULL) OR (status IN ('Unknown', 'NotAvailable', 'NotApplicable') AND normalized_value IS NULL))");
                    table.ForeignKey(
                        name: "fk_source_facts_source_snapshots_snapshot_id",
                        column: x => x.snapshot_id,
                        principalTable: "source_snapshots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "model_years",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    generation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false),
                    market = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_model_years", x => x.id);
                    table.CheckConstraint("ck_model_years_year", "year BETWEEN 1900 AND 2200");
                    table.ForeignKey(
                        name: "fk_model_years_generations_generation_id",
                        column: x => x.generation_id,
                        principalTable: "generations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "charging_promotions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_id = table.Column<Guid>(type: "uuid", nullable: true),
                    brand_id = table.Column<Guid>(type: "uuid", nullable: true),
                    model_id = table.Column<Guid>(type: "uuid", nullable: true),
                    benefit = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    eligibility_json = table.Column<string>(type: "jsonb", nullable: false),
                    caps_json = table.Column<string>(type: "jsonb", nullable: false),
                    benefit_value = table.Column<decimal>(type: "numeric(19,6)", precision: 19, scale: 6, nullable: true),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    effective_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    effective_to = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    source_fact_id = table.Column<Guid>(type: "uuid", nullable: true),
                    manual_override_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_charging_promotions", x => x.id);
                    table.CheckConstraint("ck_charging_promotions_benefit_value", "benefit_value IS NULL OR benefit_value >= 0");
                    table.CheckConstraint("ck_charging_promotions_effective_period", "effective_to IS NULL OR effective_from < effective_to");
                    table.CheckConstraint("ck_charging_promotions_provenance", "source_fact_id IS NOT NULL OR NULLIF(BTRIM(manual_override_reason), '') IS NOT NULL");
                    table.CheckConstraint("ck_charging_promotions_scope", "provider_id IS NOT NULL OR brand_id IS NOT NULL OR model_id IS NOT NULL");
                    table.ForeignKey(
                        name: "fk_charging_promotions_brands_brand_id",
                        column: x => x.brand_id,
                        principalTable: "brands",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_charging_promotions_charging_providers_provider_id",
                        column: x => x.provider_id,
                        principalTable: "charging_providers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_charging_promotions_models_model_id",
                        column: x => x.model_id,
                        principalTable: "models",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_charging_promotions_source_facts_source_fact_id",
                        column: x => x.source_fact_id,
                        principalTable: "source_facts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "charging_tariffs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_id = table.Column<Guid>(type: "uuid", nullable: false),
                    connector_type = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    minimum_power_kw = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    maximum_power_kw = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    amount_per_kwh = table.Column<decimal>(type: "numeric(19,6)", precision: 19, scale: 6, nullable: true),
                    amount_per_session = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: true),
                    overstay_amount_per_minute = table.Column<decimal>(type: "numeric(19,6)", precision: 19, scale: 6, nullable: true),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    region_scope = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    effective_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    effective_to = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    source_fact_id = table.Column<Guid>(type: "uuid", nullable: true),
                    manual_override_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_charging_tariffs", x => x.id);
                    table.CheckConstraint("ck_charging_tariffs_amounts", "COALESCE(amount_per_kwh, amount_per_session, overstay_amount_per_minute) IS NOT NULL AND (amount_per_kwh IS NULL OR amount_per_kwh >= 0) AND (amount_per_session IS NULL OR amount_per_session >= 0) AND (overstay_amount_per_minute IS NULL OR overstay_amount_per_minute >= 0)");
                    table.CheckConstraint("ck_charging_tariffs_effective_period", "effective_to IS NULL OR effective_from < effective_to");
                    table.CheckConstraint("ck_charging_tariffs_power_band", "maximum_power_kw IS NULL OR minimum_power_kw IS NULL OR minimum_power_kw <= maximum_power_kw");
                    table.CheckConstraint("ck_charging_tariffs_provenance", "source_fact_id IS NOT NULL OR NULLIF(BTRIM(manual_override_reason), '') IS NOT NULL");
                    table.ForeignKey(
                        name: "fk_charging_tariffs_charging_providers_provider_id",
                        column: x => x.provider_id,
                        principalTable: "charging_providers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_charging_tariffs_source_facts_source_fact_id",
                        column: x => x.source_fact_id,
                        principalTable: "source_facts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "data_changes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    entity_type = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    field_path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    old_value = table.Column<string>(type: "text", nullable: true),
                    new_value = table.Column<string>(type: "text", nullable: true),
                    risk_level = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    detected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    source_fact_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reviewed_audit_event_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_data_changes", x => x.id);
                    table.ForeignKey(
                        name: "fk_data_changes_audit_events_reviewed_audit_event_id",
                        column: x => x.reviewed_audit_event_id,
                        principalTable: "audit_events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_data_changes_source_facts_source_fact_id",
                        column: x => x.source_fact_id,
                        principalTable: "source_facts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "energy_prices",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    energy_type = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    provider = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    region_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,6)", precision: 19, scale: 6, nullable: false),
                    unit = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    tier_from_inclusive = table.Column<int>(type: "integer", nullable: false),
                    tier_to_inclusive = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    effective_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    effective_to = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    source_fact_id = table.Column<Guid>(type: "uuid", nullable: true),
                    manual_override_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_energy_prices", x => x.id);
                    table.CheckConstraint("ck_energy_prices_amount", "amount >= 0");
                    table.CheckConstraint("ck_energy_prices_effective_period", "effective_to IS NULL OR effective_from < effective_to");
                    table.CheckConstraint("ck_energy_prices_provenance", "source_fact_id IS NOT NULL OR NULLIF(BTRIM(manual_override_reason), '') IS NOT NULL");
                    table.CheckConstraint("ck_energy_prices_tier", "tier_from_inclusive >= 0 AND (tier_to_inclusive IS NULL OR tier_from_inclusive <= tier_to_inclusive)");
                    table.ForeignKey(
                        name: "fk_energy_prices_source_facts_source_fact_id",
                        column: x => x.source_fact_id,
                        principalTable: "source_facts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "registration_rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    component = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    scope_json = table.Column<string>(type: "jsonb", nullable: false),
                    calculation_type = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    parameters_json = table.Column<string>(type: "jsonb", nullable: false),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    effective_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    effective_to = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    source_fact_id = table.Column<Guid>(type: "uuid", nullable: true),
                    manual_override_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_registration_rules", x => x.id);
                    table.CheckConstraint("ck_registration_rules_effective_period", "effective_to IS NULL OR effective_from < effective_to");
                    table.CheckConstraint("ck_registration_rules_priority", "priority >= 0");
                    table.CheckConstraint("ck_registration_rules_provenance", "source_fact_id IS NOT NULL OR NULLIF(BTRIM(manual_override_reason), '') IS NOT NULL");
                    table.CheckConstraint("ck_registration_rules_version", "version > 0");
                    table.ForeignKey(
                        name: "fk_registration_rules_source_facts_source_fact_id",
                        column: x => x.source_fact_id,
                        principalTable: "source_facts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "trims",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    model_year_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    slug = table.Column<string>(type: "character varying(220)", maxLength: 220, nullable: false),
                    normalized_key = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    market_status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    launched_at = table.Column<DateOnly>(type: "date", nullable: true),
                    discontinued_at = table.Column<DateOnly>(type: "date", nullable: true),
                    search_text = table.Column<string>(type: "character varying(1200)", maxLength: 1200, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trims", x => x.id);
                    table.CheckConstraint("ck_trims_market_dates", "discontinued_at IS NULL OR launched_at IS NULL OR launched_at <= discontinued_at");
                    table.ForeignKey(
                        name: "fk_trims_model_years_model_year_id",
                        column: x => x.model_year_id,
                        principalTable: "model_years",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "coverage_metrics",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    brand_id = table.Column<Guid>(type: "uuid", nullable: true),
                    model_id = table.Column<Guid>(type: "uuid", nullable: true),
                    trim_id = table.Column<Guid>(type: "uuid", nullable: true),
                    completeness = table.Column<decimal>(type: "numeric(7,6)", precision: 7, scale: 6, nullable: false),
                    freshness = table.Column<decimal>(type: "numeric(7,6)", precision: 7, scale: 6, nullable: false),
                    missing_core_count = table.Column<int>(type: "integer", nullable: false),
                    discovered_count = table.Column<int>(type: "integer", nullable: false),
                    mapped_count = table.Column<int>(type: "integer", nullable: false),
                    published_count = table.Column<int>(type: "integer", nullable: false),
                    blocked_count = table.Column<int>(type: "integer", nullable: false),
                    stale_count = table.Column<int>(type: "integer", nullable: false),
                    calculated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_coverage_metrics", x => x.id);
                    table.CheckConstraint("ck_coverage_metrics_completeness", "completeness BETWEEN 0 AND 1");
                    table.CheckConstraint("ck_coverage_metrics_counts", "missing_core_count >= 0 AND discovered_count >= 0 AND mapped_count >= 0 AND published_count >= 0 AND blocked_count >= 0 AND stale_count >= 0");
                    table.CheckConstraint("ck_coverage_metrics_freshness", "freshness BETWEEN 0 AND 1");
                    table.ForeignKey(
                        name: "fk_coverage_metrics_brands_brand_id",
                        column: x => x.brand_id,
                        principalTable: "brands",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_coverage_metrics_models_model_id",
                        column: x => x.model_id,
                        principalTable: "models",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_coverage_metrics_trims_trim_id",
                        column: x => x.trim_id,
                        principalTable: "trims",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "dealer_offers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trim_id = table.Column<Guid>(type: "uuid", nullable: false),
                    headline = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    combinability_group = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    conditions_json = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    effective_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    effective_to = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    source_fact_id = table.Column<Guid>(type: "uuid", nullable: true),
                    manual_override_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dealer_offers", x => x.id);
                    table.CheckConstraint("ck_dealer_offers_effective_period", "effective_to IS NULL OR effective_from < effective_to");
                    table.CheckConstraint("ck_dealer_offers_published_provenance", "status <> 'Published' OR source_fact_id IS NOT NULL OR NULLIF(BTRIM(manual_override_reason), '') IS NOT NULL");
                    table.ForeignKey(
                        name: "fk_dealer_offers_dealer_branches_branch_id",
                        column: x => x.branch_id,
                        principalTable: "dealer_branches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_dealer_offers_source_facts_source_fact_id",
                        column: x => x.source_fact_id,
                        principalTable: "source_facts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_dealer_offers_trims_trim_id",
                        column: x => x.trim_id,
                        principalTable: "trims",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "energy_profiles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    trim_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recommended_fuel = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    official_fuel_litres_per100km = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    official_electric_kwh_per100km = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    usable_battery_kwh = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    official_range_km = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    test_cycle = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    ac_max_kw = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    dc_max_kw = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    port_type = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    source_fact_id = table.Column<Guid>(type: "uuid", nullable: true),
                    manual_override_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_energy_profiles", x => x.id);
                    table.ForeignKey(
                        name: "fk_energy_profiles_source_facts_source_fact_id",
                        column: x => x.source_fact_id,
                        principalTable: "source_facts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_energy_profiles_trims_trim_id",
                        column: x => x.trim_id,
                        principalTable: "trims",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "financing_scenarios",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    affordability_profile_id = table.Column<Guid>(type: "uuid", nullable: true),
                    trim_id = table.Column<Guid>(type: "uuid", nullable: false),
                    funding_source = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    purchase_method = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    repayment_method = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    available_cash = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: false),
                    down_payment = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: false),
                    principal = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: false),
                    annual_interest_rate = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    term_months = table.Column<int>(type: "integer", nullable: false),
                    origination_fees = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: false),
                    dealer_financing_conditions_json = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_financing_scenarios", x => x.id);
                    table.CheckConstraint("ck_financing_scenarios_amounts", "available_cash >= 0 AND down_payment >= 0 AND principal >= 0 AND annual_interest_rate >= 0 AND origination_fees >= 0");
                    table.CheckConstraint("ck_financing_scenarios_term", "(purchase_method = 'Cash' AND term_months = 0 AND principal = 0) OR (purchase_method = 'Loan' AND term_months > 0)");
                    table.ForeignKey(
                        name: "fk_financing_scenarios_affordability_profiles_affordability_pr",
                        column: x => x.affordability_profile_id,
                        principalTable: "affordability_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_financing_scenarios_trims_trim_id",
                        column: x => x.trim_id,
                        principalTable: "trims",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "powertrain_profiles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    trim_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    fuel_type = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    engine_displacement_cc = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    engine_power_kw = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    motor_power_kw = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    combined_power_kw = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    torque_nm = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    gearbox = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    drivetrain = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    source_fact_id = table.Column<Guid>(type: "uuid", nullable: true),
                    manual_override_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_powertrain_profiles", x => x.id);
                    table.ForeignKey(
                        name: "fk_powertrain_profiles_source_facts_source_fact_id",
                        column: x => x.source_fact_id,
                        principalTable: "source_facts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_powertrain_profiles_trims_trim_id",
                        column: x => x.trim_id,
                        principalTable: "trims",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "price_history",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    price_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trim_id = table.Column<Guid>(type: "uuid", nullable: false),
                    price_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: true),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    region_scope = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    effective_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    effective_to = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    source_fact_id = table.Column<Guid>(type: "uuid", nullable: true),
                    manual_override_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    archived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_price_history", x => x.id);
                    table.CheckConstraint("ck_price_history_effective_period", "effective_to IS NULL OR effective_from < effective_to");
                    table.ForeignKey(
                        name: "fk_price_history_source_facts_source_fact_id",
                        column: x => x.source_fact_id,
                        principalTable: "source_facts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_price_history_trims_trim_id",
                        column: x => x.trim_id,
                        principalTable: "trims",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "prices",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    trim_id = table.Column<Guid>(type: "uuid", nullable: false),
                    price_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: true),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    region_scope = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    effective_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    effective_to = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    source_fact_id = table.Column<Guid>(type: "uuid", nullable: true),
                    manual_override_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_prices", x => x.id);
                    table.CheckConstraint("ck_prices_amount_semantics", "(price_type = 'Unannounced' AND amount IS NULL) OR (price_type <> 'Unannounced' AND amount IS NOT NULL AND amount > 0)");
                    table.CheckConstraint("ck_prices_effective_period", "effective_to IS NULL OR effective_from < effective_to");
                    table.CheckConstraint("ck_prices_official_provenance", "status <> 'Official' OR source_fact_id IS NOT NULL OR NULLIF(BTRIM(manual_override_reason), '') IS NOT NULL");
                    table.CheckConstraint("ck_prices_version", "version > 0");
                    table.ForeignKey(
                        name: "fk_prices_source_facts_source_fact_id",
                        column: x => x.source_fact_id,
                        principalTable: "source_facts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_prices_trims_trim_id",
                        column: x => x.trim_id,
                        principalTable: "trims",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "promotions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    trim_id = table.Column<Guid>(type: "uuid", nullable: true),
                    brand_id = table.Column<Guid>(type: "uuid", nullable: true),
                    benefit_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    value = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: true),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    conditions_json = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    effective_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    effective_to = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    source_fact_id = table.Column<Guid>(type: "uuid", nullable: true),
                    manual_override_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_promotions", x => x.id);
                    table.CheckConstraint("ck_promotions_effective_period", "effective_to IS NULL OR effective_from < effective_to");
                    table.CheckConstraint("ck_promotions_published_provenance", "status <> 'Published' OR source_fact_id IS NOT NULL OR NULLIF(BTRIM(manual_override_reason), '') IS NOT NULL");
                    table.CheckConstraint("ck_promotions_scope", "(trim_id IS NOT NULL) <> (brand_id IS NOT NULL)");
                    table.CheckConstraint("ck_promotions_value", "value IS NULL OR value >= 0");
                    table.ForeignKey(
                        name: "fk_promotions_brands_brand_id",
                        column: x => x.brand_id,
                        principalTable: "brands",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_promotions_source_facts_source_fact_id",
                        column: x => x.source_fact_id,
                        principalTable: "source_facts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_promotions_trims_trim_id",
                        column: x => x.trim_id,
                        principalTable: "trims",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "trim_aliases",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    trim_id = table.Column<Guid>(type: "uuid", nullable: false),
                    alias = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    normalized_alias = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trim_aliases", x => x.id);
                    table.ForeignKey(
                        name: "fk_trim_aliases_sources_source_id",
                        column: x => x.source_id,
                        principalTable: "sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_trim_aliases_trims_trim_id",
                        column: x => x.trim_id,
                        principalTable: "trims",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "trim_colors",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    trim_id = table.Column<Guid>(type: "uuid", nullable: false),
                    color_id = table.Column<Guid>(type: "uuid", nullable: false),
                    availability = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    extra_price = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: true),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    source_fact_id = table.Column<Guid>(type: "uuid", nullable: true),
                    manual_override_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trim_colors", x => x.id);
                    table.CheckConstraint("ck_trim_colors_extra_price", "extra_price IS NULL OR extra_price >= 0");
                    table.CheckConstraint("ck_trim_colors_official_provenance", "availability <> 'Available' OR source_fact_id IS NOT NULL OR NULLIF(BTRIM(manual_override_reason), '') IS NOT NULL");
                    table.ForeignKey(
                        name: "fk_trim_colors_colors_color_id",
                        column: x => x.color_id,
                        principalTable: "colors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_trim_colors_source_facts_source_fact_id",
                        column: x => x.source_fact_id,
                        principalTable: "source_facts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_trim_colors_trims_trim_id",
                        column: x => x.trim_id,
                        principalTable: "trims",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "trim_features",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    trim_id = table.Column<Guid>(type: "uuid", nullable: false),
                    feature_definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    boolean_value = table.Column<bool>(type: "boolean", nullable: true),
                    numeric_value = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    text_value = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    enum_value = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    marketing_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    source_fact_id = table.Column<Guid>(type: "uuid", nullable: true),
                    manual_override_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trim_features", x => x.id);
                    table.CheckConstraint("ck_trim_features_official_provenance", "status <> 'Official' OR source_fact_id IS NOT NULL OR NULLIF(BTRIM(manual_override_reason), '') IS NOT NULL");
                    table.CheckConstraint("ck_trim_features_value_semantics", "((status IN ('Expected', 'Official') AND ((boolean_value IS NOT NULL)::int + (numeric_value IS NOT NULL)::int + (text_value IS NOT NULL)::int + (enum_value IS NOT NULL)::int) = 1) OR (status IN ('Unknown', 'NotAvailable', 'NotApplicable') AND boolean_value IS NULL AND numeric_value IS NULL AND text_value IS NULL AND enum_value IS NULL))");
                    table.ForeignKey(
                        name: "fk_trim_features_feature_definitions_feature_definition_id",
                        column: x => x.feature_definition_id,
                        principalTable: "feature_definitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_trim_features_source_facts_source_fact_id",
                        column: x => x.source_fact_id,
                        principalTable: "source_facts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_trim_features_trims_trim_id",
                        column: x => x.trim_id,
                        principalTable: "trims",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "trim_specs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    trim_id = table.Column<Guid>(type: "uuid", nullable: false),
                    spec_definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    numeric_value = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    text_value = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    enum_value = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    original_value = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    original_unit = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    source_fact_id = table.Column<Guid>(type: "uuid", nullable: true),
                    manual_override_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trim_specs", x => x.id);
                    table.CheckConstraint("ck_trim_specs_official_provenance", "status <> 'Official' OR source_fact_id IS NOT NULL OR NULLIF(BTRIM(manual_override_reason), '') IS NOT NULL");
                    table.CheckConstraint("ck_trim_specs_value_semantics", "((status IN ('Expected', 'Official') AND ((numeric_value IS NOT NULL)::int + (text_value IS NOT NULL)::int + (enum_value IS NOT NULL)::int) = 1) OR (status IN ('Unknown', 'NotAvailable', 'NotApplicable') AND numeric_value IS NULL AND text_value IS NULL AND enum_value IS NULL))");
                    table.ForeignKey(
                        name: "fk_trim_specs_source_facts_source_fact_id",
                        column: x => x.source_fact_id,
                        principalTable: "source_facts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_trim_specs_spec_definitions_spec_definition_id",
                        column: x => x.spec_definition_id,
                        principalTable: "spec_definitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_trim_specs_trims_trim_id",
                        column: x => x.trim_id,
                        principalTable: "trims",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "vehicle_images",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    trim_id = table.Column<Guid>(type: "uuid", nullable: true),
                    model_id = table.Column<Guid>(type: "uuid", nullable: true),
                    color_id = table.Column<Guid>(type: "uuid", nullable: true),
                    type = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    storage_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    source_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    rights_status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    content_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    rights_note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vehicle_images", x => x.id);
                    table.CheckConstraint("ck_vehicle_images_owner", "(trim_id IS NOT NULL) <> (model_id IS NOT NULL)");
                    table.CheckConstraint("ck_vehicle_images_publishable_rights", "storage_url IS NULL OR rights_status IN ('Owned', 'Licensed', 'OfficialPressKit', 'Permitted')");
                    table.ForeignKey(
                        name: "fk_vehicle_images_colors_color_id",
                        column: x => x.color_id,
                        principalTable: "colors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_vehicle_images_models_model_id",
                        column: x => x.model_id,
                        principalTable: "models",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_vehicle_images_trims_trim_id",
                        column: x => x.trim_id,
                        principalTable: "trims",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "warranty_profiles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    trim_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vehicle_months = table.Column<int>(type: "integer", nullable: true),
                    vehicle_kilometres = table.Column<int>(type: "integer", nullable: true),
                    battery_months = table.Column<int>(type: "integer", nullable: true),
                    battery_kilometres = table.Column<int>(type: "integer", nullable: true),
                    conditions = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    source_fact_id = table.Column<Guid>(type: "uuid", nullable: true),
                    manual_override_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_warranty_profiles", x => x.id);
                    table.ForeignKey(
                        name: "fk_warranty_profiles_source_facts_source_fact_id",
                        column: x => x.source_fact_id,
                        principalTable: "source_facts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_warranty_profiles_trims_trim_id",
                        column: x => x.trim_id,
                        principalTable: "trims",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "dealer_offer_benefits",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    offer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    cash_value = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: true),
                    stated_value = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: true),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    is_cash_equivalent = table.Column<bool>(type: "boolean", nullable: false),
                    exclusivity_group = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dealer_offer_benefits", x => x.id);
                    table.CheckConstraint("ck_dealer_offer_benefits_cash_equivalent", "NOT is_cash_equivalent OR cash_value IS NOT NULL");
                    table.CheckConstraint("ck_dealer_offer_benefits_cash_value", "cash_value IS NULL OR cash_value >= 0");
                    table.CheckConstraint("ck_dealer_offer_benefits_stated_value", "stated_value IS NULL OR stated_value >= 0");
                    table.ForeignKey(
                        name: "fk_dealer_offer_benefits_dealer_offers_offer_id",
                        column: x => x.offer_id,
                        principalTable: "dealer_offers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_affordability_profiles_owner_subject_id_name",
                table: "affordability_profiles",
                columns: new[] { "owner_subject_id", "name" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_events_correlation_id",
                table: "audit_events",
                column: "correlation_id");

            migrationBuilder.CreateIndex(
                name: "ix_audit_events_entity_type_entity_id_occurred_at",
                table: "audit_events",
                columns: new[] { "entity_type", "entity_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_brand_scopes_brand_id_effective_from",
                table: "brand_scopes",
                columns: new[] { "brand_id", "effective_from" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_brands_slug",
                table: "brands",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_charging_promotions_brand_id",
                table: "charging_promotions",
                column: "brand_id");

            migrationBuilder.CreateIndex(
                name: "ix_charging_promotions_eligibility_json",
                table: "charging_promotions",
                column: "eligibility_json")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "jsonb_path_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_charging_promotions_model_id",
                table: "charging_promotions",
                column: "model_id");

            migrationBuilder.CreateIndex(
                name: "ix_charging_promotions_provider_id_brand_id_model_id_effective",
                table: "charging_promotions",
                columns: new[] { "provider_id", "brand_id", "model_id", "effective_from" });

            migrationBuilder.CreateIndex(
                name: "ix_charging_promotions_source_fact_id",
                table: "charging_promotions",
                column: "source_fact_id");

            migrationBuilder.CreateIndex(
                name: "ix_charging_providers_slug",
                table: "charging_providers",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_charging_tariffs_provider_id_connector_type_minimum_power_k",
                table: "charging_tariffs",
                columns: new[] { "provider_id", "connector_type", "minimum_power_kw", "effective_from" });

            migrationBuilder.CreateIndex(
                name: "ix_charging_tariffs_source_fact_id",
                table: "charging_tariffs",
                column: "source_fact_id");

            migrationBuilder.CreateIndex(
                name: "ix_colors_code",
                table: "colors",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_coverage_metrics_brand_id_model_id_trim_id_calculated_at",
                table: "coverage_metrics",
                columns: new[] { "brand_id", "model_id", "trim_id", "calculated_at" });

            migrationBuilder.CreateIndex(
                name: "ix_coverage_metrics_model_id",
                table: "coverage_metrics",
                column: "model_id");

            migrationBuilder.CreateIndex(
                name: "ix_coverage_metrics_trim_id",
                table: "coverage_metrics",
                column: "trim_id");

            migrationBuilder.CreateIndex(
                name: "ix_data_changes_entity_type_entity_id_field_path",
                table: "data_changes",
                columns: new[] { "entity_type", "entity_id", "field_path" });

            migrationBuilder.CreateIndex(
                name: "ix_data_changes_reviewed_audit_event_id",
                table: "data_changes",
                column: "reviewed_audit_event_id");

            migrationBuilder.CreateIndex(
                name: "ix_data_changes_source_fact_id",
                table: "data_changes",
                column: "source_fact_id");

            migrationBuilder.CreateIndex(
                name: "ix_data_changes_status_risk_level_detected_at",
                table: "data_changes",
                columns: new[] { "status", "risk_level", "detected_at" });

            migrationBuilder.CreateIndex(
                name: "ix_dealer_branches_dealer_id_name_province_code",
                table: "dealer_branches",
                columns: new[] { "dealer_id", "name", "province_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_dealer_branches_province_code",
                table: "dealer_branches",
                column: "province_code");

            migrationBuilder.CreateIndex(
                name: "ix_dealer_offer_benefits_offer_id_type_exclusivity_group",
                table: "dealer_offer_benefits",
                columns: new[] { "offer_id", "type", "exclusivity_group" });

            migrationBuilder.CreateIndex(
                name: "ix_dealer_offers_branch_id_trim_id_effective_from",
                table: "dealer_offers",
                columns: new[] { "branch_id", "trim_id", "effective_from" });

            migrationBuilder.CreateIndex(
                name: "ix_dealer_offers_conditions_json",
                table: "dealer_offers",
                column: "conditions_json")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "jsonb_path_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_dealer_offers_source_fact_id",
                table: "dealer_offers",
                column: "source_fact_id");

            migrationBuilder.CreateIndex(
                name: "ix_dealer_offers_trim_id",
                table: "dealer_offers",
                column: "trim_id");

            migrationBuilder.CreateIndex(
                name: "ix_dealers_brand_id_slug",
                table: "dealers",
                columns: new[] { "brand_id", "slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_energy_prices_energy_type_provider_region_code_effective_fr",
                table: "energy_prices",
                columns: new[] { "energy_type", "provider", "region_code", "effective_from" });

            migrationBuilder.CreateIndex(
                name: "ix_energy_prices_source_fact_id",
                table: "energy_prices",
                column: "source_fact_id");

            migrationBuilder.CreateIndex(
                name: "ix_energy_profiles_source_fact_id",
                table: "energy_profiles",
                column: "source_fact_id");

            migrationBuilder.CreateIndex(
                name: "ix_energy_profiles_trim_id",
                table: "energy_profiles",
                column: "trim_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_feature_definitions_code",
                table: "feature_definitions",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_financing_scenarios_affordability_profile_id",
                table: "financing_scenarios",
                column: "affordability_profile_id");

            migrationBuilder.CreateIndex(
                name: "ix_financing_scenarios_trim_id_created_at",
                table: "financing_scenarios",
                columns: new[] { "trim_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_generations_model_id_code",
                table: "generations",
                columns: new[] { "model_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_model_aliases_model_id_normalized_alias",
                table: "model_aliases",
                columns: new[] { "model_id", "normalized_alias" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_model_aliases_normalized_alias",
                table: "model_aliases",
                column: "normalized_alias")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_model_aliases_source_id",
                table: "model_aliases",
                column: "source_id");

            migrationBuilder.CreateIndex(
                name: "ix_model_years_generation_id_year_market",
                table: "model_years",
                columns: new[] { "generation_id", "year", "market" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_models_brand_id_slug",
                table: "models",
                columns: new[] { "brand_id", "slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_models_search_text",
                table: "models",
                column: "search_text")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_powertrain_profiles_source_fact_id",
                table: "powertrain_profiles",
                column: "source_fact_id");

            migrationBuilder.CreateIndex(
                name: "ix_powertrain_profiles_trim_id",
                table: "powertrain_profiles",
                column: "trim_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_price_history_price_id_archived_at",
                table: "price_history",
                columns: new[] { "price_id", "archived_at" });

            migrationBuilder.CreateIndex(
                name: "ix_price_history_source_fact_id",
                table: "price_history",
                column: "source_fact_id");

            migrationBuilder.CreateIndex(
                name: "ix_price_history_trim_id",
                table: "price_history",
                column: "trim_id");

            migrationBuilder.CreateIndex(
                name: "ix_prices_source_fact_id",
                table: "prices",
                column: "source_fact_id");

            migrationBuilder.CreateIndex(
                name: "ix_prices_trim_id_effective_from",
                table: "prices",
                columns: new[] { "trim_id", "effective_from" });

            migrationBuilder.CreateIndex(
                name: "ix_prices_trim_id_price_type_region_scope_version",
                table: "prices",
                columns: new[] { "trim_id", "price_type", "region_scope", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_promotions_brand_id_effective_from",
                table: "promotions",
                columns: new[] { "brand_id", "effective_from" });

            migrationBuilder.CreateIndex(
                name: "ix_promotions_conditions_json",
                table: "promotions",
                column: "conditions_json")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "jsonb_path_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_promotions_source_fact_id",
                table: "promotions",
                column: "source_fact_id");

            migrationBuilder.CreateIndex(
                name: "ix_promotions_trim_id_effective_from",
                table: "promotions",
                columns: new[] { "trim_id", "effective_from" });

            migrationBuilder.CreateIndex(
                name: "ix_regions_parent_code",
                table: "regions",
                column: "parent_code");

            migrationBuilder.CreateIndex(
                name: "ix_registration_rules_component_effective_from_priority",
                table: "registration_rules",
                columns: new[] { "component", "effective_from", "priority" });

            migrationBuilder.CreateIndex(
                name: "ix_registration_rules_scope_json",
                table: "registration_rules",
                column: "scope_json")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "jsonb_path_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_registration_rules_source_fact_id",
                table: "registration_rules",
                column: "source_fact_id");

            migrationBuilder.CreateIndex(
                name: "ix_source_facts_entity_type_entity_id_field_path",
                table: "source_facts",
                columns: new[] { "entity_type", "entity_id", "field_path" });

            migrationBuilder.CreateIndex(
                name: "ix_source_facts_snapshot_id_field_path",
                table: "source_facts",
                columns: new[] { "snapshot_id", "field_path" });

            migrationBuilder.CreateIndex(
                name: "ix_source_snapshots_object_key",
                table: "source_snapshots",
                column: "object_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_source_snapshots_source_id_content_hash",
                table: "source_snapshots",
                columns: new[] { "source_id", "content_hash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_source_snapshots_source_id_fetched_at",
                table: "source_snapshots",
                columns: new[] { "source_id", "fetched_at" });

            migrationBuilder.CreateIndex(
                name: "ix_sources_domain_active_priority",
                table: "sources",
                columns: new[] { "domain", "active", "priority" });

            migrationBuilder.CreateIndex(
                name: "ix_sources_url",
                table: "sources",
                column: "url",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_spec_definitions_code",
                table: "spec_definitions",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_trim_aliases_normalized_alias",
                table: "trim_aliases",
                column: "normalized_alias")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_trim_aliases_source_id",
                table: "trim_aliases",
                column: "source_id");

            migrationBuilder.CreateIndex(
                name: "ix_trim_aliases_trim_id_normalized_alias",
                table: "trim_aliases",
                columns: new[] { "trim_id", "normalized_alias" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_trim_colors_color_id",
                table: "trim_colors",
                column: "color_id");

            migrationBuilder.CreateIndex(
                name: "ix_trim_colors_source_fact_id",
                table: "trim_colors",
                column: "source_fact_id");

            migrationBuilder.CreateIndex(
                name: "ix_trim_colors_trim_id_color_id",
                table: "trim_colors",
                columns: new[] { "trim_id", "color_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_trim_features_feature_definition_id",
                table: "trim_features",
                column: "feature_definition_id");

            migrationBuilder.CreateIndex(
                name: "ix_trim_features_source_fact_id",
                table: "trim_features",
                column: "source_fact_id");

            migrationBuilder.CreateIndex(
                name: "ix_trim_features_trim_id_feature_definition_id",
                table: "trim_features",
                columns: new[] { "trim_id", "feature_definition_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_trim_specs_source_fact_id",
                table: "trim_specs",
                column: "source_fact_id");

            migrationBuilder.CreateIndex(
                name: "ix_trim_specs_spec_definition_id",
                table: "trim_specs",
                column: "spec_definition_id");

            migrationBuilder.CreateIndex(
                name: "ix_trim_specs_trim_id_spec_definition_id",
                table: "trim_specs",
                columns: new[] { "trim_id", "spec_definition_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_trims_model_year_id_market_status",
                table: "trims",
                columns: new[] { "model_year_id", "market_status" });

            migrationBuilder.CreateIndex(
                name: "ix_trims_model_year_id_normalized_key",
                table: "trims",
                columns: new[] { "model_year_id", "normalized_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_trims_search_text",
                table: "trims",
                column: "search_text")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_vehicle_images_color_id",
                table: "vehicle_images",
                column: "color_id");

            migrationBuilder.CreateIndex(
                name: "ix_vehicle_images_content_hash",
                table: "vehicle_images",
                column: "content_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_vehicle_images_model_id",
                table: "vehicle_images",
                column: "model_id");

            migrationBuilder.CreateIndex(
                name: "ix_vehicle_images_trim_id",
                table: "vehicle_images",
                column: "trim_id");

            migrationBuilder.CreateIndex(
                name: "ix_warranty_profiles_source_fact_id",
                table: "warranty_profiles",
                column: "source_fact_id");

            migrationBuilder.CreateIndex(
                name: "ix_warranty_profiles_trim_id",
                table: "warranty_profiles",
                column: "trim_id",
                unique: true);

            migrationBuilder.Sql(
                """
                ALTER TABLE prices
                ADD CONSTRAINT ex_prices_official_msrp_no_overlap
                EXCLUDE USING gist (
                    trim_id WITH =,
                    price_type WITH =,
                    region_scope WITH =,
                    priority WITH =,
                    tstzrange(effective_from, effective_to, '[)') WITH &&
                )
                WHERE (price_type = 'Msrp' AND status = 'Official');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "brand_scopes");

            migrationBuilder.DropTable(
                name: "charging_promotions");

            migrationBuilder.DropTable(
                name: "charging_tariffs");

            migrationBuilder.DropTable(
                name: "coverage_metrics");

            migrationBuilder.DropTable(
                name: "data_changes");

            migrationBuilder.DropTable(
                name: "dealer_offer_benefits");

            migrationBuilder.DropTable(
                name: "energy_prices");

            migrationBuilder.DropTable(
                name: "energy_profiles");

            migrationBuilder.DropTable(
                name: "financing_scenarios");

            migrationBuilder.DropTable(
                name: "model_aliases");

            migrationBuilder.DropTable(
                name: "powertrain_profiles");

            migrationBuilder.DropTable(
                name: "price_history");

            migrationBuilder.DropTable(
                name: "prices");

            migrationBuilder.DropTable(
                name: "promotions");

            migrationBuilder.DropTable(
                name: "registration_rules");

            migrationBuilder.DropTable(
                name: "trim_aliases");

            migrationBuilder.DropTable(
                name: "trim_colors");

            migrationBuilder.DropTable(
                name: "trim_features");

            migrationBuilder.DropTable(
                name: "trim_specs");

            migrationBuilder.DropTable(
                name: "vehicle_images");

            migrationBuilder.DropTable(
                name: "warranty_profiles");

            migrationBuilder.DropTable(
                name: "charging_providers");

            migrationBuilder.DropTable(
                name: "audit_events");

            migrationBuilder.DropTable(
                name: "dealer_offers");

            migrationBuilder.DropTable(
                name: "affordability_profiles");

            migrationBuilder.DropTable(
                name: "feature_definitions");

            migrationBuilder.DropTable(
                name: "spec_definitions");

            migrationBuilder.DropTable(
                name: "colors");

            migrationBuilder.DropTable(
                name: "dealer_branches");

            migrationBuilder.DropTable(
                name: "source_facts");

            migrationBuilder.DropTable(
                name: "trims");

            migrationBuilder.DropTable(
                name: "dealers");

            migrationBuilder.DropTable(
                name: "regions");

            migrationBuilder.DropTable(
                name: "source_snapshots");

            migrationBuilder.DropTable(
                name: "model_years");

            migrationBuilder.DropTable(
                name: "sources");

            migrationBuilder.DropTable(
                name: "generations");

            migrationBuilder.DropTable(
                name: "models");

            migrationBuilder.DropTable(
                name: "brands");
        }
    }
}
