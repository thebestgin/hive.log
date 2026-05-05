using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HiveLog.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCallerToLogEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "caller",
                table: "log_entries",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_log_entries_caller",
                table: "log_entries",
                column: "caller");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_log_entries_caller",
                table: "log_entries");

            migrationBuilder.DropColumn(
                name: "caller",
                table: "log_entries");
        }
    }
}
