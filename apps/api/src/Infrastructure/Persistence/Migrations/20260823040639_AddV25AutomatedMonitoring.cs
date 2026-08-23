using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietnamCarPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddV25AutomatedMonitoring : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ingestion_job_runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_type = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    monitor_kind = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    source_key = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    source_id = table.Column<Guid>(type: "uuid", nullable: true),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    http_status = table.Column<int>(type: "integer", nullable: true),
                    parse_status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    content_changed = table.Column<bool>(type: "boolean", nullable: true),
                    error_stage = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    error_code = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: true),
                    error_message = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    duration_milliseconds = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ingestion_job_runs", x => x.id);
                    table.CheckConstraint("ck_ingestion_job_runs_http_status", "http_status IS NULL OR http_status BETWEEN 0 AND 599");
                    table.CheckConstraint("ck_ingestion_job_runs_lifecycle", "(status = 'Running' AND completed_at IS NULL AND duration_milliseconds IS NULL) OR (status <> 'Running' AND completed_at IS NOT NULL AND duration_milliseconds >= 0)");
                    table.ForeignKey(
                        name: "fk_ingestion_job_runs_sources_source_id",
                        column: x => x.source_id,
                        principalTable: "sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "monitoring_alerts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    fingerprint = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    alert_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    severity = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    source_key = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    source_id = table.Column<Guid>(type: "uuid", nullable: true),
                    job_run_id = table.Column<Guid>(type: "uuid", nullable: true),
                    message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    occurrence_count = table.Column<int>(type: "integer", nullable: false),
                    first_triggered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_triggered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    acknowledged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    acknowledged_by = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_monitoring_alerts", x => x.id);
                    table.CheckConstraint("ck_monitoring_alerts_lifecycle", "(status = 'Open' AND acknowledged_at IS NULL AND acknowledged_by IS NULL AND resolved_at IS NULL) OR (status = 'Acknowledged' AND acknowledged_at IS NOT NULL AND NULLIF(BTRIM(acknowledged_by), '') IS NOT NULL AND resolved_at IS NULL) OR (status = 'Resolved' AND resolved_at IS NOT NULL)");
                    table.CheckConstraint("ck_monitoring_alerts_occurrences", "occurrence_count > 0");
                    table.ForeignKey(
                        name: "fk_monitoring_alerts_ingestion_job_runs_job_run_id",
                        column: x => x.job_run_id,
                        principalTable: "ingestion_job_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_monitoring_alerts_sources_source_id",
                        column: x => x.source_id,
                        principalTable: "sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ingestion_job_runs_monitor_kind_started_at",
                table: "ingestion_job_runs",
                columns: new[] { "monitor_kind", "started_at" });

            migrationBuilder.CreateIndex(
                name: "ix_ingestion_job_runs_source_id",
                table: "ingestion_job_runs",
                column: "source_id");

            migrationBuilder.CreateIndex(
                name: "ix_ingestion_job_runs_source_key_started_at",
                table: "ingestion_job_runs",
                columns: new[] { "source_key", "started_at" });

            migrationBuilder.CreateIndex(
                name: "ix_ingestion_job_runs_status_started_at",
                table: "ingestion_job_runs",
                columns: new[] { "status", "started_at" });

            migrationBuilder.CreateIndex(
                name: "ix_monitoring_alerts_fingerprint",
                table: "monitoring_alerts",
                column: "fingerprint",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_monitoring_alerts_job_run_id",
                table: "monitoring_alerts",
                column: "job_run_id");

            migrationBuilder.CreateIndex(
                name: "ix_monitoring_alerts_source_id",
                table: "monitoring_alerts",
                column: "source_id");

            migrationBuilder.CreateIndex(
                name: "ix_monitoring_alerts_source_key_alert_type",
                table: "monitoring_alerts",
                columns: new[] { "source_key", "alert_type" });

            migrationBuilder.CreateIndex(
                name: "ix_monitoring_alerts_status_severity_last_triggered_at",
                table: "monitoring_alerts",
                columns: new[] { "status", "severity", "last_triggered_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "monitoring_alerts");

            migrationBuilder.DropTable(
                name: "ingestion_job_runs");
        }
    }
}
