using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MindLift.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailTemplateBuilder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "email_templates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    subject = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    preheader_text = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, defaultValue: "General"),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    layout_json = table.Column<string>(type: "jsonb", nullable: false),
                    rendered_html = table.Column<string>(type: "text", nullable: false),
                    plain_text_body = table.Column<string>(type: "text", nullable: false),
                    merge_tags = table.Column<string[]>(type: "text[]", nullable: false),
                    thumbnail_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    is_preset = table.Column<bool>(type: "boolean", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_email_templates", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_email_templates_tenant_id",
                table: "email_templates",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_email_templates_tenant_id_category_is_active",
                table: "email_templates",
                columns: new[] { "tenant_id", "category", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_email_templates_tenant_id_name",
                table: "email_templates",
                columns: new[] { "tenant_id", "name" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "email_templates");
        }
    }
}
