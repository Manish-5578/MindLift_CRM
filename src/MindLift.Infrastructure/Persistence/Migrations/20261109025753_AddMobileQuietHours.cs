using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MindLift.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMobileQuietHours : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "quiet_end_minute",
                table: "mobile_devices",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "quiet_start_minute",
                table: "mobile_devices",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "quiet_end_minute",
                table: "mobile_devices");

            migrationBuilder.DropColumn(
                name: "quiet_start_minute",
                table: "mobile_devices");
        }
    }
}
