using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MindLift.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BuiltInDocumentRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "built_in_document_rules_enabled",
                table: "tenant_settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "built_in_document_rules_enabled",
                table: "tenant_settings");
        }
    }
}
