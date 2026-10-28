using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MindLift.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTelephony : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "call_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    call_sid = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    from_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    to_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    direction = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "Outbound"),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "Initiated"),
                    started_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    answered_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ended_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    duration_seconds = table.Column<int>(type: "integer", nullable: false),
                    lead_id = table.Column<Guid>(type: "uuid", nullable: true),
                    contact_id = table.Column<Guid>(type: "uuid", nullable: true),
                    account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    deal_id = table.Column<Guid>(type: "uuid", nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    disposition = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    recording_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    recording_duration_seconds = table.Column<int>(type: "integer", nullable: true),
                    transcription_text = table.Column<string>(type: "text", nullable: true),
                    transcription_status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true, defaultValue: "None"),
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
                    table.PrimaryKey("pk_call_sessions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "telephony_settings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "MockTelephony"),
                    account_sid = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    encrypted_auth_token = table.Column<string>(type: "text", nullable: true),
                    caller_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "+18005550199"),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    auto_log_calls = table.Column<bool>(type: "boolean", nullable: false),
                    record_calls_default = table.Column<bool>(type: "boolean", nullable: false),
                    web_rtc_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    twilio_app_sid = table.Column<string>(type: "text", nullable: true),
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
                    table.PrimaryKey("pk_telephony_settings", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_call_sessions_call_sid",
                table: "call_sessions",
                column: "call_sid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_call_sessions_tenant_id",
                table: "call_sessions",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_call_sessions_tenant_id_started_at_utc",
                table: "call_sessions",
                columns: new[] { "tenant_id", "started_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_call_sessions_tenant_id_user_id",
                table: "call_sessions",
                columns: new[] { "tenant_id", "user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_telephony_settings_tenant_id",
                table: "telephony_settings",
                column: "tenant_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "call_sessions");

            migrationBuilder.DropTable(
                name: "telephony_settings");
        }
    }
}
