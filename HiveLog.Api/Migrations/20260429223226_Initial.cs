using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HiveLog.Api.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "log_entries",
                columns: table => new
                {
                    timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    trace_id = table.Column<string>(type: "text", nullable: true),
                    span_id = table.Column<string>(type: "text", nullable: true),
                    parent_span_id = table.Column<string>(type: "text", nullable: true),
                    source = table.Column<string>(type: "text", nullable: false),
                    source_type = table.Column<string>(type: "text", nullable: false),
                    instance_id = table.Column<string>(type: "text", nullable: true),
                    level = table.Column<short>(type: "smallint", nullable: false),
                    category = table.Column<string>(type: "text", nullable: false),
                    message = table.Column<string>(type: "text", nullable: false),
                    message_template = table.Column<string>(type: "text", nullable: true),
                    properties = table.Column<string>(type: "jsonb", nullable: true),
                    exception = table.Column<string>(type: "jsonb", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    request_id = table.Column<string>(type: "text", nullable: true),
                    session_id = table.Column<string>(type: "text", nullable: true),
                    tags = table.Column<string[]>(type: "text[]", nullable: true),
                    stream = table.Column<string>(type: "text", nullable: false, defaultValue: "app"),
                    is_authenticated = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_log_entries", x => new { x.timestamp, x.id });
                });

            migrationBuilder.CreateTable(
                name: "webhook_rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    name = table.Column<string>(type: "text", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    trigger_level_min = table.Column<short>(type: "smallint", nullable: true),
                    trigger_source = table.Column<string>(type: "text", nullable: true),
                    trigger_stream = table.Column<string>(type: "text", nullable: true),
                    trigger_tags = table.Column<string[]>(type: "text[]", nullable: true),
                    action_url = table.Column<string>(type: "text", nullable: false),
                    action_body_template = table.Column<string>(type: "text", nullable: true),
                    throttle_window_seconds = table.Column<int>(type: "integer", nullable: false),
                    throttle_max_fires = table.Column<int>(type: "integer", nullable: false),
                    last_fired_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    fire_count_in_window = table.Column<int>(type: "integer", nullable: false),
                    window_start_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_webhook_rules", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_log_entries_trace_id_parent_span_id",
                table: "log_entries",
                columns: new[] { "trace_id", "parent_span_id" });

            // Convert log_entries to a TimescaleDB hypertable (idempotent: no-op if extension missing)
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM pg_available_extensions WHERE name = 'timescaledb'
                    ) THEN
                        PERFORM create_hypertable('log_entries', 'timestamp', if_not_exists => true);
                    END IF;
                END;
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "log_entries");

            migrationBuilder.DropTable(
                name: "webhook_rules");
        }
    }
}
