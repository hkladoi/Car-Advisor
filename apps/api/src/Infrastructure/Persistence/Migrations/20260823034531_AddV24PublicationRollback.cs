using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietnamCarPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddV24PublicationRollback : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "anomaly_code",
                table: "data_changes",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "detection_context",
                table: "data_changes",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "publication_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    data_change_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entity_type = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    field_path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    before_value = table.Column<string>(type: "text", nullable: true),
                    after_value = table.Column<string>(type: "text", nullable: true),
                    before_source_fact_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_fact_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    published_by = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    rolled_back_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    rolled_back_by = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    rollback_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_publication_versions", x => x.id);
                    table.CheckConstraint("ck_publication_versions_rollback", "(status = 'Published' AND rolled_back_at IS NULL AND rolled_back_by IS NULL AND rollback_reason IS NULL) OR (status = 'RolledBack' AND rolled_back_at IS NOT NULL AND NULLIF(BTRIM(rolled_back_by), '') IS NOT NULL AND NULLIF(BTRIM(rollback_reason), '') IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_publication_versions_data_changes_data_change_id",
                        column: x => x.data_change_id,
                        principalTable: "data_changes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_publication_versions_source_facts_before_source_fact_id",
                        column: x => x.before_source_fact_id,
                        principalTable: "source_facts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_publication_versions_source_facts_source_fact_id",
                        column: x => x.source_fact_id,
                        principalTable: "source_facts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_data_changes_anomaly_code",
                table: "data_changes",
                column: "anomaly_code");

            migrationBuilder.CreateIndex(
                name: "ix_publication_versions_before_source_fact_id",
                table: "publication_versions",
                column: "before_source_fact_id");

            migrationBuilder.CreateIndex(
                name: "ix_publication_versions_data_change_id",
                table: "publication_versions",
                column: "data_change_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_publication_versions_entity_type_entity_id_field_path_publi",
                table: "publication_versions",
                columns: new[] { "entity_type", "entity_id", "field_path", "published_at" });

            migrationBuilder.CreateIndex(
                name: "ix_publication_versions_source_fact_id",
                table: "publication_versions",
                column: "source_fact_id");

            migrationBuilder.CreateIndex(
                name: "ix_publication_versions_status_published_at",
                table: "publication_versions",
                columns: new[] { "status", "published_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "publication_versions");

            migrationBuilder.DropIndex(
                name: "ix_data_changes_anomaly_code",
                table: "data_changes");

            migrationBuilder.DropColumn(
                name: "anomaly_code",
                table: "data_changes");

            migrationBuilder.DropColumn(
                name: "detection_context",
                table: "data_changes");
        }
    }
}
