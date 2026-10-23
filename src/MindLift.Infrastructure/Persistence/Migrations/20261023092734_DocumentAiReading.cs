using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MindLift.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DocumentAiReading : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ai_reading_enabled",
                table: "tenant_settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "extraction_method",
                table: "business_documents",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "extraction_note",
                table: "business_documents",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "low_confidence_pages_json",
                table: "business_documents",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "read_confidence",
                table: "business_documents",
                type: "double precision",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ai_reading_enabled",
                table: "tenant_settings");

            migrationBuilder.DropColumn(
                name: "extraction_method",
                table: "business_documents");

            migrationBuilder.DropColumn(
                name: "extraction_note",
                table: "business_documents");

            migrationBuilder.DropColumn(
                name: "low_confidence_pages_json",
                table: "business_documents");

            migrationBuilder.DropColumn(
                name: "read_confidence",
                table: "business_documents");
        }
    }
}
