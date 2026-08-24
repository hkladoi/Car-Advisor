using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietnamCarPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddV34SearchSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "published_data_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    aggregate_type = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    aggregate_id = table.Column<Guid>(type: "uuid", nullable: true),
                    payload_json = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    available_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    processing_started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    correlation_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_published_data_events", x => x.id);
                    table.CheckConstraint("ck_published_data_events_attempts", "attempts >= 0");
                    table.CheckConstraint("ck_published_data_events_lifecycle", "(status = 'Pending' AND processing_started_at IS NULL AND processed_at IS NULL AND last_error IS NULL) OR (status = 'Processing' AND processing_started_at IS NOT NULL AND processed_at IS NULL) OR (status = 'Completed' AND processing_started_at IS NOT NULL AND processed_at IS NOT NULL AND last_error IS NULL) OR (status = 'Failed' AND processing_started_at IS NOT NULL AND processed_at IS NULL AND NULLIF(BTRIM(last_error), '') IS NOT NULL)");
                });

            migrationBuilder.CreateIndex(
                name: "ix_published_data_events_aggregate_type_aggregate_id_occurred_",
                table: "published_data_events",
                columns: new[] { "aggregate_type", "aggregate_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_published_data_events_correlation_id",
                table: "published_data_events",
                column: "correlation_id");

            migrationBuilder.CreateIndex(
                name: "ix_published_data_events_status_available_at_occurred_at",
                table: "published_data_events",
                columns: new[] { "status", "available_at", "occurred_at" });

            migrationBuilder.Sql(
                """
                CREATE FUNCTION process_catalog_search_events(batch_size integer DEFAULT 250)
                RETURNS integer
                LANGUAGE plpgsql
                AS $$
                DECLARE
                    event_ids uuid[];
                    event_count integer;
                BEGIN
                    IF batch_size < 1 OR batch_size > 1000 THEN
                        RAISE EXCEPTION 'batch_size must be between 1 and 1000';
                    END IF;

                    IF NOT pg_try_advisory_xact_lock(20260824034) THEN
                        RETURN 0;
                    END IF;

                    SELECT array_agg(candidate.id)
                    INTO event_ids
                    FROM (
                        SELECT id
                        FROM published_data_events
                        WHERE event_type LIKE 'CatalogSearchSync.%'
                          AND status IN ('Pending', 'Failed')
                          AND available_at <= CURRENT_TIMESTAMP
                        ORDER BY occurred_at, id
                        FOR UPDATE SKIP LOCKED
                        LIMIT batch_size
                    ) AS candidate;

                    event_count := COALESCE(cardinality(event_ids), 0);
                    IF event_count = 0 THEN
                        RETURN 0;
                    END IF;

                    UPDATE published_data_events
                    SET status = 'Processing', attempts = attempts + 1,
                        processing_started_at = CURRENT_TIMESTAMP, processed_at = NULL,
                        last_error = NULL, updated_at = CURRENT_TIMESTAMP
                    WHERE id = ANY(event_ids);

                    BEGIN
                        PERFORM refresh_current_searchable_trims();
                    EXCEPTION WHEN OTHERS THEN
                        UPDATE published_data_events
                        SET status = 'Failed',
                            last_error = LEFT(SQLERRM, 4000),
                            available_at = CURRENT_TIMESTAMP
                                + make_interval(secs => LEAST(300, power(2, LEAST(attempts, 8))::integer)),
                            updated_at = CURRENT_TIMESTAMP
                        WHERE id = ANY(event_ids);
                        RETURN -event_count;
                    END;

                    UPDATE published_data_events
                    SET status = 'Completed', processed_at = CURRENT_TIMESTAMP,
                        last_error = NULL, updated_at = CURRENT_TIMESTAMP
                    WHERE id = ANY(event_ids);
                    RETURN event_count;
                END;
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS process_catalog_search_events(integer);");

            migrationBuilder.DropTable(
                name: "published_data_events");
        }
    }
}
