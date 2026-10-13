using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MindLift.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Onboarding_Templates_Trial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "checklist_dismissed",
                table: "tenants",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int[]>(
                name: "onboarding_done",
                table: "tenants",
                type: "integer[]",
                nullable: false,
                defaultValue: new int[0]);

            migrationBuilder.AddColumn<int[]>(
                name: "onboarding_skipped",
                table: "tenants",
                type: "integer[]",
                nullable: false,
                defaultValue: new int[0]);

            migrationBuilder.AddColumn<int>(
                name: "template_version",
                table: "tenants",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "trial_reminder_sent",
                table: "tenants",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "origin",
                table: "pipelines",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "origin",
                table: "pipeline_stages",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "origin",
                table: "picklist_values",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "origin",
                table: "layouts",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "origin",
                table: "custom_field_definitions",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "checklist_dismissed",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "onboarding_done",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "onboarding_skipped",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "template_version",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "trial_reminder_sent",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "origin",
                table: "pipelines");

            migrationBuilder.DropColumn(
                name: "origin",
                table: "pipeline_stages");

            migrationBuilder.DropColumn(
                name: "origin",
                table: "picklist_values");

            migrationBuilder.DropColumn(
                name: "origin",
                table: "layouts");

            migrationBuilder.DropColumn(
                name: "origin",
                table: "custom_field_definitions");
        }
    }
}
