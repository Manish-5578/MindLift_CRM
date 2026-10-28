using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MindLift.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBccDropbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "bcc_dropbox_addresses",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    dropbox_token = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    dropbox_email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    total_emails_received = table.Column<int>(type: "integer", nullable: false),
                    last_received_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
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
                    table.PrimaryKey("pk_bcc_dropbox_addresses", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "bcc_dropbox_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    bcc_dropbox_address_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    message_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    from_address = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    recipient_addresses = table.Column<string[]>(type: "text[]", nullable: false),
                    subject = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    matched_lead_id = table.Column<Guid>(type: "uuid", nullable: true),
                    matched_contact_id = table.Column<Guid>(type: "uuid", nullable: true),
                    matched_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    matched_deal_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action_taken = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    received_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_message_id = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("pk_bcc_dropbox_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "bcc_dropbox_settings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    dropbox_domain = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false, defaultValue: "bcc.mindlift.crm"),
                    auto_create_lead = table.Column<bool>(type: "boolean", nullable: false),
                    auto_create_contact = table.Column<bool>(type: "boolean", nullable: false),
                    match_deals = table.Column<bool>(type: "boolean", nullable: false),
                    default_privacy = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "Public"),
                    excluded_domains = table.Column<string[]>(type: "text[]", nullable: false),
                    log_non_matched = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("pk_bcc_dropbox_settings", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bcc_dropbox_addresses_dropbox_token",
                table: "bcc_dropbox_addresses",
                column: "dropbox_token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_bcc_dropbox_addresses_tenant_id",
                table: "bcc_dropbox_addresses",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_bcc_dropbox_addresses_tenant_id_user_id",
                table: "bcc_dropbox_addresses",
                columns: new[] { "tenant_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_bcc_dropbox_logs_tenant_id",
                table: "bcc_dropbox_logs",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_bcc_dropbox_logs_tenant_id_received_at_utc",
                table: "bcc_dropbox_logs",
                columns: new[] { "tenant_id", "received_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_bcc_dropbox_logs_tenant_id_user_id",
                table: "bcc_dropbox_logs",
                columns: new[] { "tenant_id", "user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_bcc_dropbox_settings_tenant_id",
                table: "bcc_dropbox_settings",
                column: "tenant_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bcc_dropbox_addresses");

            migrationBuilder.DropTable(
                name: "bcc_dropbox_logs");

            migrationBuilder.DropTable(
                name: "bcc_dropbox_settings");
        }
    }
}
