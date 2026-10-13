using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MindLift.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Activities_M9 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "type",
                table: "activities",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "subject",
                table: "activities",
                type: "character varying(250)",
                maxLength: 250,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "activities",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<Guid>(
                name: "checklist_template_id",
                table: "activities",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "completed_at",
                table: "activities",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "completed_by_id",
                table: "activities",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "description",
                table: "activities",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "direction",
                table: "activities",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "duration_minutes",
                table: "activities",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "end_at",
                table: "activities",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "location",
                table: "activities",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "parent_activity_id",
                table: "activities",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "priority",
                table: "activities",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "Normal");

            migrationBuilder.AddColumn<int>(
                name: "reminder_minutes",
                table: "activities",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "checklist_templates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    trigger = table.Column<string>(type: "text", nullable: false),
                    pipeline_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    items = table.Column<string>(type: "jsonb", nullable: false),
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
                    table.PrimaryKey("pk_checklist_templates", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_activities_tenant_id_account_id",
                table: "activities",
                columns: new[] { "tenant_id", "account_id" });

            migrationBuilder.CreateIndex(
                name: "ix_activities_tenant_id_contact_id",
                table: "activities",
                columns: new[] { "tenant_id", "contact_id" });

            migrationBuilder.CreateIndex(
                name: "ix_activities_tenant_id_deal_id",
                table: "activities",
                columns: new[] { "tenant_id", "deal_id" });

            migrationBuilder.CreateIndex(
                name: "ix_activities_tenant_id_lead_id",
                table: "activities",
                columns: new[] { "tenant_id", "lead_id" });

            migrationBuilder.CreateIndex(
                name: "ix_checklist_templates_tenant_id",
                table: "checklist_templates",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_checklist_templates_tenant_id_trigger",
                table: "checklist_templates",
                columns: new[] { "tenant_id", "trigger" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "checklist_templates");

            migrationBuilder.DropIndex(
                name: "ix_activities_tenant_id_account_id",
                table: "activities");

            migrationBuilder.DropIndex(
                name: "ix_activities_tenant_id_contact_id",
                table: "activities");

            migrationBuilder.DropIndex(
                name: "ix_activities_tenant_id_deal_id",
                table: "activities");

            migrationBuilder.DropIndex(
                name: "ix_activities_tenant_id_lead_id",
                table: "activities");

            migrationBuilder.DropColumn(
                name: "checklist_template_id",
                table: "activities");

            migrationBuilder.DropColumn(
                name: "completed_at",
                table: "activities");

            migrationBuilder.DropColumn(
                name: "completed_by_id",
                table: "activities");

            migrationBuilder.DropColumn(
                name: "description",
                table: "activities");

            migrationBuilder.DropColumn(
                name: "direction",
                table: "activities");

            migrationBuilder.DropColumn(
                name: "duration_minutes",
                table: "activities");

            migrationBuilder.DropColumn(
                name: "end_at",
                table: "activities");

            migrationBuilder.DropColumn(
                name: "location",
                table: "activities");

            migrationBuilder.DropColumn(
                name: "parent_activity_id",
                table: "activities");

            migrationBuilder.DropColumn(
                name: "priority",
                table: "activities");

            migrationBuilder.DropColumn(
                name: "reminder_minutes",
                table: "activities");

            migrationBuilder.AlterColumn<string>(
                name: "type",
                table: "activities",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.AlterColumn<string>(
                name: "subject",
                table: "activities",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(250)",
                oldMaxLength: 250);

            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "activities",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);
        }
    }
}
