using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HiveLog.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddIsAuthenticated : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_authenticated",
                table: "log_entries",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_authenticated",
                table: "log_entries");
        }
    }
}
