using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietnamCarPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddV28MarketCoverage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_brand_scopes_brand_id_effective_from",
                table: "brand_scopes");

            migrationBuilder.AddColumn<string>(
                name: "category",
                table: "sources",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "unknown");

            migrationBuilder.AddColumn<Guid>(
                name: "evidence_snapshot_id",
                table: "brand_scopes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "market",
                table: "brand_scopes",
                type: "character varying(8)",
                maxLength: 8,
                nullable: false,
                defaultValue: "VN");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "reviewed_at",
                table: "brand_scopes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reviewed_by",
                table: "brand_scopes",
                type: "character varying(320)",
                maxLength: 320,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "source_id",
                table: "brand_scopes",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "market_candidates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    market = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    brand_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    evidence_snapshot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    external_key = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    name = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    parent_external_key = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    market_status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    resolution = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    model_id = table.Column<Guid>(type: "uuid", nullable: true),
                    trim_id = table.Column<Guid>(type: "uuid", nullable: true),
                    blocked_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    trim_inventory_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    trim_inventory_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    discovered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reviewed_by = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_market_candidates", x => x.id);
                    table.CheckConstraint("ck_market_candidates_parent", "(kind = 'Model' AND parent_external_key IS NULL AND trim_id IS NULL AND trim_inventory_status <> 'NotApplicable') OR (kind = 'Trim' AND parent_external_key IS NOT NULL AND trim_inventory_status = 'NotApplicable')");
                    table.CheckConstraint("ck_market_candidates_resolution", "(resolution = 'Published' AND blocked_reason IS NULL AND model_id IS NOT NULL AND (kind = 'Model' OR trim_id IS NOT NULL)) OR (resolution = 'BlockedWithReason' AND blocked_reason IS NOT NULL AND length(trim(blocked_reason)) > 0)");
                    table.CheckConstraint("ck_market_candidates_review_times", "discovered_at <= last_seen_at AND discovered_at <= reviewed_at");
                    table.CheckConstraint("ck_market_candidates_trim_inventory", "trim_inventory_status <> 'BlockedWithReason' OR (trim_inventory_reason IS NOT NULL AND length(trim(trim_inventory_reason)) > 0)");
                    table.ForeignKey(
                        name: "fk_market_candidates_brands_brand_id",
                        column: x => x.brand_id,
                        principalTable: "brands",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_market_candidates_models_model_id",
                        column: x => x.model_id,
                        principalTable: "models",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_market_candidates_source_snapshots_evidence_snapshot_id",
                        column: x => x.evidence_snapshot_id,
                        principalTable: "source_snapshots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_market_candidates_sources_source_id",
                        column: x => x.source_id,
                        principalTable: "sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_market_candidates_trims_trim_id",
                        column: x => x.trim_id,
                        principalTable: "trims",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "market_scope_reviews",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    market = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    schema_version = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    manifest_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    reviewed_brand_count = table.Column<int>(type: "integer", nullable: false),
                    included_brand_count = table.Column<int>(type: "integer", nullable: false),
                    excluded_brand_count = table.Column<int>(type: "integer", nullable: false),
                    model_candidate_count = table.Column<int>(type: "integer", nullable: false),
                    trim_candidate_count = table.Column<int>(type: "integer", nullable: false),
                    policy_source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    policy_snapshot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    observed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reviewed_by = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    review_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_market_scope_reviews", x => x.id);
                    table.CheckConstraint("ck_market_scope_reviews_counts", "reviewed_brand_count > 0 AND included_brand_count > 0 AND excluded_brand_count >= 0 AND reviewed_brand_count = included_brand_count + excluded_brand_count AND model_candidate_count > 0 AND trim_candidate_count >= 0");
                    table.CheckConstraint("ck_market_scope_reviews_times", "observed_at <= reviewed_at");
                    table.ForeignKey(
                        name: "fk_market_scope_reviews_source_snapshots_policy_snapshot_id",
                        column: x => x.policy_snapshot_id,
                        principalTable: "source_snapshots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_market_scope_reviews_sources_policy_source_id",
                        column: x => x.policy_source_id,
                        principalTable: "sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_brand_scopes_brand_id",
                table: "brand_scopes",
                column: "brand_id");

            migrationBuilder.CreateIndex(
                name: "ix_brand_scopes_evidence_snapshot_id",
                table: "brand_scopes",
                column: "evidence_snapshot_id");

            migrationBuilder.CreateIndex(
                name: "ix_brand_scopes_market_brand_id_effective_from",
                table: "brand_scopes",
                columns: new[] { "market", "brand_id", "effective_from" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_brand_scopes_source_id",
                table: "brand_scopes",
                column: "source_id");

            migrationBuilder.CreateIndex(
                name: "ix_market_candidates_brand_id",
                table: "market_candidates",
                column: "brand_id");

            migrationBuilder.CreateIndex(
                name: "ix_market_candidates_evidence_snapshot_id",
                table: "market_candidates",
                column: "evidence_snapshot_id");

            migrationBuilder.CreateIndex(
                name: "ix_market_candidates_last_seen_at",
                table: "market_candidates",
                column: "last_seen_at");

            migrationBuilder.CreateIndex(
                name: "ix_market_candidates_market_brand_id_kind_external_key",
                table: "market_candidates",
                columns: new[] { "market", "brand_id", "kind", "external_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_market_candidates_market_market_status_resolution",
                table: "market_candidates",
                columns: new[] { "market", "market_status", "resolution" });

            migrationBuilder.CreateIndex(
                name: "ix_market_candidates_model_id",
                table: "market_candidates",
                column: "model_id");

            migrationBuilder.CreateIndex(
                name: "ix_market_candidates_source_id",
                table: "market_candidates",
                column: "source_id");

            migrationBuilder.CreateIndex(
                name: "ix_market_candidates_trim_id",
                table: "market_candidates",
                column: "trim_id");

            migrationBuilder.CreateIndex(
                name: "ix_market_scope_reviews_market_reviewed_at",
                table: "market_scope_reviews",
                columns: new[] { "market", "reviewed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_market_scope_reviews_market_schema_version",
                table: "market_scope_reviews",
                columns: new[] { "market", "schema_version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_market_scope_reviews_policy_snapshot_id",
                table: "market_scope_reviews",
                column: "policy_snapshot_id");

            migrationBuilder.CreateIndex(
                name: "ix_market_scope_reviews_policy_source_id",
                table: "market_scope_reviews",
                column: "policy_source_id");

            migrationBuilder.AddForeignKey(
                name: "fk_brand_scopes_source_snapshots_evidence_snapshot_id",
                table: "brand_scopes",
                column: "evidence_snapshot_id",
                principalTable: "source_snapshots",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_brand_scopes_sources_source_id",
                table: "brand_scopes",
                column: "source_id",
                principalTable: "sources",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_brand_scopes_source_snapshots_evidence_snapshot_id",
                table: "brand_scopes");

            migrationBuilder.DropForeignKey(
                name: "fk_brand_scopes_sources_source_id",
                table: "brand_scopes");

            migrationBuilder.DropTable(
                name: "market_candidates");

            migrationBuilder.DropTable(
                name: "market_scope_reviews");

            migrationBuilder.DropIndex(
                name: "ix_brand_scopes_brand_id",
                table: "brand_scopes");

            migrationBuilder.DropIndex(
                name: "ix_brand_scopes_evidence_snapshot_id",
                table: "brand_scopes");

            migrationBuilder.DropIndex(
                name: "ix_brand_scopes_market_brand_id_effective_from",
                table: "brand_scopes");

            migrationBuilder.DropIndex(
                name: "ix_brand_scopes_source_id",
                table: "brand_scopes");

            migrationBuilder.DropColumn(
                name: "category",
                table: "sources");

            migrationBuilder.DropColumn(
                name: "evidence_snapshot_id",
                table: "brand_scopes");

            migrationBuilder.DropColumn(
                name: "market",
                table: "brand_scopes");

            migrationBuilder.DropColumn(
                name: "reviewed_at",
                table: "brand_scopes");

            migrationBuilder.DropColumn(
                name: "reviewed_by",
                table: "brand_scopes");

            migrationBuilder.DropColumn(
                name: "source_id",
                table: "brand_scopes");

            migrationBuilder.CreateIndex(
                name: "ix_brand_scopes_brand_id_effective_from",
                table: "brand_scopes",
                columns: new[] { "brand_id", "effective_from" },
                unique: true);
        }
    }
}
