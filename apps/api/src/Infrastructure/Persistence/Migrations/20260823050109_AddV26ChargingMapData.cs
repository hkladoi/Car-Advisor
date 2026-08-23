using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietnamCarPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddV26ChargingMapData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "charging_stations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    external_source = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    external_id = table.Column<int>(type: "integer", nullable: false),
                    external_uuid = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    source_snapshot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    charging_provider_id = table.Column<Guid>(type: "uuid", nullable: true),
                    provider_mapping_reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    provider_mapping_reviewed_by = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    name = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    address_line1 = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    address_line2 = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    town = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    state_or_province = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    postcode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    country_code = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    latitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    longitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    operator_name = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: true),
                    usage_type = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    operational_status = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    is_operational = table.Column<bool>(type: "boolean", nullable: true),
                    number_of_points = table.Column<int>(type: "integer", nullable: true),
                    external_data_quality_level = table.Column<int>(type: "integer", nullable: true),
                    coverage = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    confidence = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    related_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    external_updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    imported_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_charging_stations", x => x.id);
                    table.CheckConstraint("ck_charging_stations_coordinates", "latitude BETWEEN -90 AND 90 AND longitude BETWEEN -180 AND 180");
                    table.CheckConstraint("ck_charging_stations_country", "country_code = 'VN'");
                    table.CheckConstraint("ck_charging_stations_data_quality", "external_data_quality_level IS NULL OR external_data_quality_level BETWEEN 1 AND 5");
                    table.CheckConstraint("ck_charging_stations_external_id", "external_id > 0");
                    table.CheckConstraint("ck_charging_stations_external_source", "external_source = 'OpenChargeMap'");
                    table.CheckConstraint("ck_charging_stations_points", "number_of_points IS NULL OR number_of_points >= 0");
                    table.CheckConstraint("ck_charging_stations_provider_mapping", "(charging_provider_id IS NULL AND provider_mapping_reviewed_at IS NULL AND provider_mapping_reviewed_by IS NULL) OR (charging_provider_id IS NOT NULL AND provider_mapping_reviewed_at IS NOT NULL AND NULLIF(BTRIM(provider_mapping_reviewed_by), '') IS NOT NULL)");
                    table.CheckConstraint("ck_charging_stations_reference_coverage", "coverage = 'ReferenceOnly'");
                    table.ForeignKey(
                        name: "fk_charging_stations_charging_providers_charging_provider_id",
                        column: x => x.charging_provider_id,
                        principalTable: "charging_providers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_charging_stations_source_snapshots_source_snapshot_id",
                        column: x => x.source_snapshot_id,
                        principalTable: "source_snapshots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "charging_station_connectors",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    charging_station_id = table.Column<Guid>(type: "uuid", nullable: false),
                    external_id = table.Column<int>(type: "integer", nullable: false),
                    connector_type = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    charging_level = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    current_type = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    operational_status = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    power_kw = table.Column<decimal>(type: "numeric(9,3)", precision: 9, scale: 3, nullable: true),
                    amps = table.Column<int>(type: "integer", nullable: true),
                    voltage = table.Column<int>(type: "integer", nullable: true),
                    quantity = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_charging_station_connectors", x => x.id);
                    table.CheckConstraint("ck_charging_station_connectors_electrical", "(amps IS NULL OR amps BETWEEN 0 AND 1000) AND (voltage IS NULL OR voltage BETWEEN 0 AND 10000) AND (quantity IS NULL OR quantity BETWEEN 0 AND 500)");
                    table.CheckConstraint("ck_charging_station_connectors_external_id", "external_id > 0");
                    table.CheckConstraint("ck_charging_station_connectors_power", "power_kw IS NULL OR power_kw BETWEEN 0 AND 1000");
                    table.ForeignKey(
                        name: "fk_charging_station_connectors_charging_stations_charging_stat",
                        column: x => x.charging_station_id,
                        principalTable: "charging_stations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_charging_station_connectors_charging_station_id_external_id",
                table: "charging_station_connectors",
                columns: new[] { "charging_station_id", "external_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_charging_station_connectors_connector_type_power_kw",
                table: "charging_station_connectors",
                columns: new[] { "connector_type", "power_kw" });

            migrationBuilder.CreateIndex(
                name: "ix_charging_stations_active_latitude_longitude",
                table: "charging_stations",
                columns: new[] { "active", "latitude", "longitude" });

            migrationBuilder.CreateIndex(
                name: "ix_charging_stations_charging_provider_id_provider_mapping_rev",
                table: "charging_stations",
                columns: new[] { "charging_provider_id", "provider_mapping_reviewed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_charging_stations_external_source_external_id",
                table: "charging_stations",
                columns: new[] { "external_source", "external_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_charging_stations_last_seen_at",
                table: "charging_stations",
                column: "last_seen_at");

            migrationBuilder.CreateIndex(
                name: "ix_charging_stations_source_snapshot_id",
                table: "charging_stations",
                column: "source_snapshot_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "charging_station_connectors");

            migrationBuilder.DropTable(
                name: "charging_stations");
        }
    }
}
