using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietnamCarPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddV35PartnerApi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "partner_api_usage_plans",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    requests_per_minute = table.Column<int>(type: "integer", nullable: false),
                    requests_per_month = table.Column<long>(type: "bigint", nullable: false),
                    max_page_size = table.Column<int>(type: "integer", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_partner_api_usage_plans", x => x.id);
                    table.CheckConstraint("ck_partner_api_usage_plans_code", "code ~ '^[a-z][a-z0-9-]{2,31}$'");
                    table.CheckConstraint("ck_partner_api_usage_plans_limits", "requests_per_minute > 0 AND requests_per_month > 0 AND max_page_size BETWEEN 1 AND 100");
                });

            migrationBuilder.CreateTable(
                name: "partner_api_keys",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    usage_plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    key_prefix = table.Column<string>(type: "character varying(17)", maxLength: 17, nullable: false),
                    key_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    scope = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    policy_version = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    issued_by = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_by = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    revocation_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_partner_api_keys", x => x.id);
                    table.CheckConstraint("ck_partner_api_keys_expiry", "expires_at IS NULL OR expires_at > issued_at");
                    table.CheckConstraint("ck_partner_api_keys_hash", "key_hash ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("ck_partner_api_keys_policy", "NULLIF(BTRIM(policy_version), '') IS NOT NULL");
                    table.CheckConstraint("ck_partner_api_keys_prefix", "key_prefix ~ '^vcp_v1_[A-Za-z0-9_-]{10}$'");
                    table.CheckConstraint("ck_partner_api_keys_revocation", "(revoked_at IS NULL AND revoked_by IS NULL AND revocation_reason IS NULL) OR (revoked_at IS NOT NULL AND NULLIF(BTRIM(revoked_by), '') IS NOT NULL AND NULLIF(BTRIM(revocation_reason), '') IS NOT NULL)");
                    table.CheckConstraint("ck_partner_api_keys_scope", "scope = 'catalog.read'");
                    table.ForeignKey(
                        name: "fk_partner_api_keys_partner_api_usage_plans_usage_plan_id",
                        column: x => x.usage_plan_id,
                        principalTable: "partner_api_usage_plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_partner_api_keys_key_hash",
                table: "partner_api_keys",
                column: "key_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_partner_api_keys_key_prefix",
                table: "partner_api_keys",
                column: "key_prefix",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_partner_api_keys_usage_plan_id_revoked_at_expires_at",
                table: "partner_api_keys",
                columns: new[] { "usage_plan_id", "revoked_at", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "ix_partner_api_usage_plans_active_code",
                table: "partner_api_usage_plans",
                columns: new[] { "active", "code" });

            migrationBuilder.CreateIndex(
                name: "ix_partner_api_usage_plans_code",
                table: "partner_api_usage_plans",
                column: "code",
                unique: true);

            migrationBuilder.InsertData(
                table: "partner_api_usage_plans",
                columns: new[]
                {
                    "id", "code", "name", "requests_per_minute", "requests_per_month",
                    "max_page_size", "active", "created_at", "updated_at"
                },
                values: new object[,]
                {
                    {
                        new Guid("42a5ee91-647b-5c00-bd5c-a150afddf351"),
                        "sandbox", "Sandbox read access", 30, 10_000L, 25, true,
                        new DateTimeOffset(2026, 8, 24, 3, 26, 19, TimeSpan.Zero),
                        new DateTimeOffset(2026, 8, 24, 3, 26, 19, TimeSpan.Zero)
                    },
                    {
                        new Guid("7e73b721-b2fa-5ac0-8ca6-70eaec0f82ed"),
                        "standard", "Standard read access", 300, 500_000L, 100, true,
                        new DateTimeOffset(2026, 8, 24, 3, 26, 19, TimeSpan.Zero),
                        new DateTimeOffset(2026, 8, 24, 3, 26, 19, TimeSpan.Zero)
                    }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "partner_api_keys");

            migrationBuilder.DropTable(
                name: "partner_api_usage_plans");
        }
    }
}
