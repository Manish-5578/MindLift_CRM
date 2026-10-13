using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MindLift.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Leads_M6 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "assigned_at",
                table: "leads",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "converted_account_id",
                table: "leads",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "converted_at",
                table: "leads",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "converted_by_id",
                table: "leads",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "converted_contact_id",
                table: "leads",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "converted_deal_id",
                table: "leads",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "description",
                table: "leads",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "disqualify_reason",
                table: "leads",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "first_contacted_at",
                table: "leads",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "normalized_email",
                table: "leads",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "normalized_phone",
                table: "leads",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string[]>(
                name: "tags",
                table: "leads",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            migrationBuilder.AddColumn<Guid>(
                name: "web_form_id",
                table: "leads",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "normalized_email",
                table: "contacts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "normalized_phone",
                table: "contacts",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "assignment_rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    conditions = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    method = table.Column<string>(type: "text", nullable: false),
                    user_ids = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    last_assigned_index = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("pk_assignment_rules", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "web_forms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    public_token = table.Column<string>(type: "text", nullable: false),
                    fields = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    default_source = table.Column<string>(type: "text", nullable: false),
                    success_message = table.Column<string>(type: "text", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    submission_count = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("pk_web_forms", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_leads_tenant_id_normalized_email",
                table: "leads",
                columns: new[] { "tenant_id", "normalized_email" });

            migrationBuilder.CreateIndex(
                name: "ix_leads_tenant_id_normalized_phone",
                table: "leads",
                columns: new[] { "tenant_id", "normalized_phone" });

            migrationBuilder.CreateIndex(
                name: "ix_assignment_rules_tenant_id",
                table: "assignment_rules",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_assignment_rules_tenant_id_priority",
                table: "assignment_rules",
                columns: new[] { "tenant_id", "priority" });

            migrationBuilder.CreateIndex(
                name: "ix_web_forms_public_token",
                table: "web_forms",
                column: "public_token",
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_web_forms_tenant_id",
                table: "web_forms",
                column: "tenant_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "assignment_rules");

            migrationBuilder.DropTable(
                name: "web_forms");

            migrationBuilder.DropIndex(
                name: "ix_leads_tenant_id_normalized_email",
                table: "leads");

            migrationBuilder.DropIndex(
                name: "ix_leads_tenant_id_normalized_phone",
                table: "leads");

            migrationBuilder.DropColumn(
                name: "assigned_at",
                table: "leads");

            migrationBuilder.DropColumn(
                name: "converted_account_id",
                table: "leads");

            migrationBuilder.DropColumn(
                name: "converted_at",
                table: "leads");

            migrationBuilder.DropColumn(
                name: "converted_by_id",
                table: "leads");

            migrationBuilder.DropColumn(
                name: "converted_contact_id",
                table: "leads");

            migrationBuilder.DropColumn(
                name: "converted_deal_id",
                table: "leads");

            migrationBuilder.DropColumn(
                name: "description",
                table: "leads");

            migrationBuilder.DropColumn(
                name: "disqualify_reason",
                table: "leads");

            migrationBuilder.DropColumn(
                name: "first_contacted_at",
                table: "leads");

            migrationBuilder.DropColumn(
                name: "normalized_email",
                table: "leads");

            migrationBuilder.DropColumn(
                name: "normalized_phone",
                table: "leads");

            migrationBuilder.DropColumn(
                name: "tags",
                table: "leads");

            migrationBuilder.DropColumn(
                name: "web_form_id",
                table: "leads");

            migrationBuilder.DropColumn(
                name: "normalized_email",
                table: "contacts");

            migrationBuilder.DropColumn(
                name: "normalized_phone",
                table: "contacts");
        }
    }
}
