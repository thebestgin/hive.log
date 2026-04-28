using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HiveLog.Api.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
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
                    trace_id = table.Column<Guid>(type: "uuid", nullable: true),
                    span_id = table.Column<Guid>(type: "uuid", nullable: true),
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
                    stream = table.Column<string>(type: "text", nullable: false, defaultValue: "app")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_log_entries", x => new { x.timestamp, x.id });
                });

            // TimescaleDB extension + hypertable conversion.
            // Wrapped in DO block so it only runs when TimescaleDB is available.
            // Without TimescaleDB (e.g. dev without 00188 docker stack), the table stays as a
            // regular PostgreSQL table — queries and inserts still work, just without time-partitioning.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM pg_available_extensions WHERE name = 'timescaledb'
                    ) THEN
                        CREATE EXTENSION IF NOT EXISTS timescaledb CASCADE;

                        PERFORM create_hypertable(
                            'log_entries',
                            'timestamp',
                            chunk_time_interval => INTERVAL '1 hour',
                            if_not_exists => TRUE
                        );
                    END IF;
                END;
                $$;
                """);

            // Indexes for common query patterns (work with or without TimescaleDB)
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS idx_log_entries_source ON log_entries (source, timestamp DESC);");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS idx_log_entries_level ON log_entries (level, timestamp DESC) WHERE level >= 3;");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS idx_log_entries_trace ON log_entries (trace_id, timestamp DESC) WHERE trace_id IS NOT NULL;");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS idx_log_entries_stream ON log_entries (stream, timestamp DESC);");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS idx_log_entries_tags ON log_entries USING GIN (tags);");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS idx_log_entries_properties ON log_entries USING GIN (properties jsonb_path_ops);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop indexes explicitly (TimescaleDB may not cascade them with DropTable)
            migrationBuilder.Sql("DROP INDEX IF EXISTS idx_log_entries_properties;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS idx_log_entries_tags;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS idx_log_entries_stream;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS idx_log_entries_trace;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS idx_log_entries_level;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS idx_log_entries_source;");

            migrationBuilder.DropTable(
                name: "log_entries");

            // Note: timescaledb extension is not dropped here — it may be shared with other tables
        }
    }
}
