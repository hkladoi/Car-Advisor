using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietnamCarPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAdminDataQuality : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "admin_users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    normalized_email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    display_name = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    password_hash = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    role = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    failed_login_count = table.Column<int>(type: "integer", nullable: false),
                    locked_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_login_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_admin_users", x => x.id);
                    table.CheckConstraint("ck_admin_users_email", "normalized_email <> ''");
                    table.CheckConstraint("ck_admin_users_failed_login", "failed_login_count >= 0");
                });

            migrationBuilder.CreateTable(
                name: "field_locks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    entity_type = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    field_path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    actor = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_field_locks", x => x.id);
                    table.CheckConstraint("ck_field_locks_reason", "NULLIF(BTRIM(reason), '') IS NOT NULL");
                });

            migrationBuilder.CreateTable(
                name: "manual_imports",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    format = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    content_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    content_text = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    validation_report_json = table.Column<string>(type: "jsonb", nullable: false),
                    submitted_by = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    staged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_manual_imports", x => x.id);
                    table.CheckConstraint("ck_manual_imports_reason", "NULLIF(BTRIM(reason), '') IS NOT NULL");
                });

            migrationBuilder.CreateTable(
                name: "admin_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    admin_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    client_fingerprint_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_admin_sessions", x => x.id);
                    table.CheckConstraint("ck_admin_sessions_expiry", "expires_at > created_at");
                    table.ForeignKey(
                        name: "fk_admin_sessions_admin_users_admin_user_id",
                        column: x => x.admin_user_id,
                        principalTable: "admin_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_admin_sessions_admin_user_id_expires_at",
                table: "admin_sessions",
                columns: new[] { "admin_user_id", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "ix_admin_sessions_token_hash",
                table: "admin_sessions",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_admin_users_active_role",
                table: "admin_users",
                columns: new[] { "active", "role" });

            migrationBuilder.CreateIndex(
                name: "ix_admin_users_normalized_email",
                table: "admin_users",
                column: "normalized_email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_field_locks_entity_type_entity_id_field_path_active",
                table: "field_locks",
                columns: new[] { "entity_type", "entity_id", "field_path", "active" });

            migrationBuilder.CreateIndex(
                name: "ix_manual_imports_content_hash",
                table: "manual_imports",
                column: "content_hash");

            migrationBuilder.CreateIndex(
                name: "ix_manual_imports_status_submitted_at",
                table: "manual_imports",
                columns: new[] { "status", "submitted_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "admin_sessions");

            migrationBuilder.DropTable(
                name: "field_locks");

            migrationBuilder.DropTable(
                name: "manual_imports");

            migrationBuilder.DropTable(
                name: "admin_users");
        }
    }
}
