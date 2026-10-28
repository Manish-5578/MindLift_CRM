using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MindLift.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCallRecordingFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "consent_mode",
                table: "telephony_settings",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "Prompt");

            migrationBuilder.AddColumn<bool>(
                name: "dual_party_notification",
                table: "telephony_settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "retention_days",
                table: "telephony_settings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "is_recording_deleted",
                table: "call_sessions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string[]>(
                name: "transcription_action_items",
                table: "call_sessions",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            migrationBuilder.AddColumn<string>(
                name: "transcription_sentiment",
                table: "call_sessions",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "transcription_summary",
                table: "call_sessions",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "consent_mode",
                table: "telephony_settings");

            migrationBuilder.DropColumn(
                name: "dual_party_notification",
                table: "telephony_settings");

            migrationBuilder.DropColumn(
                name: "retention_days",
                table: "telephony_settings");

            migrationBuilder.DropColumn(
                name: "is_recording_deleted",
                table: "call_sessions");

            migrationBuilder.DropColumn(
                name: "transcription_action_items",
                table: "call_sessions");

            migrationBuilder.DropColumn(
                name: "transcription_sentiment",
                table: "call_sessions");

            migrationBuilder.DropColumn(
                name: "transcription_summary",
                table: "call_sessions");
        }
    }
}
