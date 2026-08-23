using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietnamCarPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddV16EnergyPricing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "consumption_notes",
                table: "energy_profiles",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "electric_consumption_condition",
                table: "energy_profiles",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "fuel_consumption_condition",
                table: "energy_profiles",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "tax_included",
                table: "energy_prices",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "tax_rate",
                table: "energy_prices",
                type: "numeric(9,6)",
                precision: 9,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "overstay_cap_per_session",
                table: "charging_tariffs",
                type: "numeric(19,2)",
                precision: 19,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "overstay_rules_json",
                table: "charging_tariffs",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'{}'::jsonb");

            migrationBuilder.AddColumn<bool>(
                name: "tax_included",
                table: "charging_tariffs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "manual_override_reason",
                table: "charging_providers",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "source_fact_id",
                table: "charging_providers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_energy_prices_tax_rate",
                table: "energy_prices",
                sql: "tax_rate >= 0 AND tax_rate <= 1");

            migrationBuilder.AddCheckConstraint(
                name: "ck_charging_tariffs_overstay_cap",
                table: "charging_tariffs",
                sql: "overstay_cap_per_session IS NULL OR overstay_cap_per_session >= 0");

            migrationBuilder.CreateIndex(
                name: "ix_charging_providers_source_fact_id",
                table: "charging_providers",
                column: "source_fact_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_charging_providers_provenance",
                table: "charging_providers",
                sql: "source_fact_id IS NOT NULL OR NULLIF(BTRIM(manual_override_reason), '') IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "fk_charging_providers_source_facts_source_fact_id",
                table: "charging_providers",
                column: "source_fact_id",
                principalTable: "source_facts",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_charging_providers_source_facts_source_fact_id",
                table: "charging_providers");

            migrationBuilder.DropCheckConstraint(
                name: "ck_energy_prices_tax_rate",
                table: "energy_prices");

            migrationBuilder.DropCheckConstraint(
                name: "ck_charging_tariffs_overstay_cap",
                table: "charging_tariffs");

            migrationBuilder.DropIndex(
                name: "ix_charging_providers_source_fact_id",
                table: "charging_providers");

            migrationBuilder.DropCheckConstraint(
                name: "ck_charging_providers_provenance",
                table: "charging_providers");

            migrationBuilder.DropColumn(
                name: "consumption_notes",
                table: "energy_profiles");

            migrationBuilder.DropColumn(
                name: "electric_consumption_condition",
                table: "energy_profiles");

            migrationBuilder.DropColumn(
                name: "fuel_consumption_condition",
                table: "energy_profiles");

            migrationBuilder.DropColumn(
                name: "tax_included",
                table: "energy_prices");

            migrationBuilder.DropColumn(
                name: "tax_rate",
                table: "energy_prices");

            migrationBuilder.DropColumn(
                name: "overstay_cap_per_session",
                table: "charging_tariffs");

            migrationBuilder.DropColumn(
                name: "overstay_rules_json",
                table: "charging_tariffs");

            migrationBuilder.DropColumn(
                name: "tax_included",
                table: "charging_tariffs");

            migrationBuilder.DropColumn(
                name: "manual_override_reason",
                table: "charging_providers");

            migrationBuilder.DropColumn(
                name: "source_fact_id",
                table: "charging_providers");
        }
    }
}
