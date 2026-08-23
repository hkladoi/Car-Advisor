using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietnamCarPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRegionProvenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "manual_override_reason",
                table: "regions",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "source_fact_id",
                table: "regions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_regions_source_fact_id",
                table: "regions",
                column: "source_fact_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_regions_provenance",
                table: "regions",
                sql: "source_fact_id IS NOT NULL OR NULLIF(BTRIM(manual_override_reason), '') IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "fk_regions_source_facts_source_fact_id",
                table: "regions",
                column: "source_fact_id",
                principalTable: "source_facts",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_regions_source_facts_source_fact_id",
                table: "regions");

            migrationBuilder.DropIndex(
                name: "ix_regions_source_fact_id",
                table: "regions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_regions_provenance",
                table: "regions");

            migrationBuilder.DropColumn(
                name: "manual_override_reason",
                table: "regions");

            migrationBuilder.DropColumn(
                name: "source_fact_id",
                table: "regions");
        }
    }
}
