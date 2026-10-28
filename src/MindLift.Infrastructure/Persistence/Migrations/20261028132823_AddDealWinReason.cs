using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MindLift.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDealWinReason : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "win_notes",
                table: "deals",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "win_reason",
                table: "deals",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_deals_tenant_id_win_reason",
                table: "deals",
                columns: new[] { "tenant_id", "win_reason" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_deals_tenant_id_win_reason",
                table: "deals");

            migrationBuilder.DropColumn(
                name: "win_notes",
                table: "deals");

            migrationBuilder.DropColumn(
                name: "win_reason",
                table: "deals");
        }
    }
}
