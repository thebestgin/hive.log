using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HiveLog.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddWebhookRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
                    table.PrimaryKey("PK_webhook_rules", x => x.id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "webhook_rules");
        }
    }
}
