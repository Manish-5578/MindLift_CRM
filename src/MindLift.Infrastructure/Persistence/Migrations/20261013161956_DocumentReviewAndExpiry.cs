using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MindLift.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DocumentReviewAndExpiry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "expiry_notified_offsets_json",
                table: "business_documents",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reject_reason",
                table: "business_documents",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "expiry_notified_offsets_json",
                table: "business_documents");

            migrationBuilder.DropColumn(
                name: "reject_reason",
                table: "business_documents");
        }
    }
}
