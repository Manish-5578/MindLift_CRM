using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MindLift.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PlatformConsole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ai_reading_allowed",
                table: "tenants",
                type: "boolean",
                nullable: false,
                defaultValue: true);   // existing companies keep what they had: allowed

            migrationBuilder.AddColumn<int>(
                name: "max_users_override",
                table: "tenants",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string[]>(
                name: "platform_disabled_modules",
                table: "tenants",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            migrationBuilder.AddColumn<string>(
                name: "platform_notes",
                table: "tenants",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "platform_audit",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    company = table.Column<string>(type: "text", nullable: true),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor = table.Column<string>(type: "text", nullable: false),
                    action = table.Column<string>(type: "text", nullable: false),
                    summary = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_platform_audit", x => x.id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "platform_audit");

            migrationBuilder.DropColumn(
                name: "ai_reading_allowed",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "max_users_override",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "platform_disabled_modules",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "platform_notes",
                table: "tenants");
        }
    }
}
