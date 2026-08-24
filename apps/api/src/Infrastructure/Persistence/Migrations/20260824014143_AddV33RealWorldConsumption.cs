using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietnamCarPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddV33RealWorldConsumption : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "real_world_consumption_aggregates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    brand_id = table.Column<Guid>(type: "uuid", nullable: true),
                    dataset_reporting_year = table.Column<int>(type: "integer", nullable: false),
                    vehicle_registration_year = table.Column<int>(type: "integer", nullable: false),
                    dataset_version = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    manufacturer = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    normalized_manufacturer = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    fuel_type = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    sample_size = table.Column<int>(type: "integer", nullable: false),
                    real_world_fuel_litres_per100km = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: true),
                    official_wltp_fuel_litres_per100km = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: true),
                    fuel_absolute_gap_litres_per100km = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: true),
                    fuel_percentage_gap = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: true),
                    real_world_co2_grams_per_km = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: true),
                    official_wltp_co2_grams_per_km = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: true),
                    co2_absolute_gap_grams_per_km = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: true),
                    co2_percentage_gap = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: true),
                    real_world_fuel_weighted_litres_per100km = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: true),
                    official_wltp_fuel_weighted_litres_per100km = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: true),
                    fuel_weighted_absolute_gap_litres_per100km = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: true),
                    fuel_weighted_percentage_gap = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: true),
                    real_world_co2_weighted_grams_per_km = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: true),
                    official_wltp_co2_weighted_grams_per_km = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: true),
                    co2_weighted_absolute_gap_grams_per_km = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: true),
                    co2_weighted_percentage_gap = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: true),
                    geography = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    aggregation_scope = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    methodology_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    attribution = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    source_fact_id = table.Column<Guid>(type: "uuid", nullable: true),
                    manual_override_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_real_world_consumption_aggregates", x => x.id);
                    table.CheckConstraint("ck_real_world_consumption_metrics", "real_world_fuel_litres_per100km IS NOT NULL OR real_world_co2_grams_per_km IS NOT NULL");
                    table.CheckConstraint("ck_real_world_consumption_provenance", "source_fact_id IS NOT NULL");
                    table.CheckConstraint("ck_real_world_consumption_sample", "sample_size > 0");
                    table.CheckConstraint("ck_real_world_consumption_years", "dataset_reporting_year BETWEEN 2000 AND 2200 AND vehicle_registration_year BETWEEN 2000 AND dataset_reporting_year");
                    table.ForeignKey(
                        name: "fk_real_world_consumption_aggregates_brands_brand_id",
                        column: x => x.brand_id,
                        principalTable: "brands",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_real_world_consumption_aggregates_source_facts_source_fact_",
                        column: x => x.source_fact_id,
                        principalTable: "source_facts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_real_world_consumption_aggregates_brand_id_vehicle_registra",
                table: "real_world_consumption_aggregates",
                columns: new[] { "brand_id", "vehicle_registration_year", "fuel_type" });

            migrationBuilder.CreateIndex(
                name: "ix_real_world_consumption_aggregates_dataset_version_vehicle_r",
                table: "real_world_consumption_aggregates",
                columns: new[] { "dataset_version", "vehicle_registration_year", "normalized_manufacturer", "fuel_type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_real_world_consumption_aggregates_source_fact_id",
                table: "real_world_consumption_aggregates",
                column: "source_fact_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "real_world_consumption_aggregates");
        }
    }
}
