using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietnamCarPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddV32UserAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "user_accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    normalized_email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    display_name = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    password_hash = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    failed_login_count = table.Column<int>(type: "integer", nullable: false),
                    locked_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_login_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    consented_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    privacy_policy_version = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_accounts", x => x.id);
                    table.CheckConstraint("ck_user_accounts_consent", "consented_at >= created_at AND privacy_policy_version <> ''");
                    table.CheckConstraint("ck_user_accounts_email", "normalized_email <> ''");
                    table.CheckConstraint("ck_user_accounts_failed_login", "failed_login_count >= 0");
                });

            migrationBuilder.CreateTable(
                name: "saved_comparisons",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    trim_ids_json = table.Column<string>(type: "jsonb", nullable: false),
                    region_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    profile_preset = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    financing_preset = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_saved_comparisons", x => x.id);
                    table.CheckConstraint("ck_saved_comparisons_trim_ids", "jsonb_typeof(trim_ids_json) = 'array' AND jsonb_array_length(trim_ids_json) BETWEEN 2 AND 4");
                    table.ForeignKey(
                        name: "fk_saved_comparisons_user_accounts_user_account_id",
                        column: x => x.user_account_id,
                        principalTable: "user_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_account_id = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("pk_user_sessions", x => x.id);
                    table.CheckConstraint("ck_user_sessions_expiry", "expires_at > created_at");
                    table.ForeignKey(
                        name: "fk_user_sessions_user_accounts_user_account_id",
                        column: x => x.user_account_id,
                        principalTable: "user_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "watchlist_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trim_id = table.Column<Guid>(type: "uuid", nullable: false),
                    region_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    target_price = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: true),
                    price_alerts = table.Column<bool>(type: "boolean", nullable: false),
                    promotion_alerts = table.Column<bool>(type: "boolean", nullable: false),
                    dealer_offer_alerts = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_watchlist_entries", x => x.id);
                    table.CheckConstraint("ck_watchlist_entries_target_price", "target_price IS NULL OR target_price >= 0");
                    table.ForeignKey(
                        name: "fk_watchlist_entries_trims_trim_id",
                        column: x => x.trim_id,
                        principalTable: "trims",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_watchlist_entries_user_accounts_user_account_id",
                        column: x => x.user_account_id,
                        principalTable: "user_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_saved_comparisons_user_account_id_updated_at",
                table: "saved_comparisons",
                columns: new[] { "user_account_id", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "ix_user_accounts_active",
                table: "user_accounts",
                column: "active");

            migrationBuilder.CreateIndex(
                name: "ix_user_accounts_normalized_email",
                table: "user_accounts",
                column: "normalized_email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_sessions_token_hash",
                table: "user_sessions",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_sessions_user_account_id_expires_at",
                table: "user_sessions",
                columns: new[] { "user_account_id", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "ix_watchlist_entries_trim_id_price_alerts_promotion_alerts_dea",
                table: "watchlist_entries",
                columns: new[] { "trim_id", "price_alerts", "promotion_alerts", "dealer_offer_alerts" });

            migrationBuilder.CreateIndex(
                name: "ix_watchlist_entries_user_account_id_trim_id",
                table: "watchlist_entries",
                columns: new[] { "user_account_id", "trim_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "saved_comparisons");

            migrationBuilder.DropTable(
                name: "user_sessions");

            migrationBuilder.DropTable(
                name: "watchlist_entries");

            migrationBuilder.DropTable(
                name: "user_accounts");
        }
    }
}
