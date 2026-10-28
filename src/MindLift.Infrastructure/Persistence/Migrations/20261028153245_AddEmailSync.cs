using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MindLift.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "email_sync_accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    email_address = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    display_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "IMAP"),
                    imap_host = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    imap_port = table.Column<int>(type: "integer", nullable: true),
                    imap_use_ssl = table.Column<bool>(type: "boolean", nullable: false),
                    imap_username = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    encrypted_password = table.Column<string>(type: "text", nullable: true),
                    smtp_host = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    smtp_port = table.Column<int>(type: "integer", nullable: true),
                    smtp_use_ssl = table.Column<bool>(type: "boolean", nullable: false),
                    smtp_username = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    encrypted_o_auth_token = table.Column<string>(type: "text", nullable: true),
                    encrypted_refresh_token = table.Column<string>(type: "text", nullable: true),
                    sync_direction = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "TwoWay"),
                    email_visibility = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "Public"),
                    auto_create_leads = table.Column<bool>(type: "boolean", nullable: false),
                    excluded_domains = table.Column<string[]>(type: "text[]", nullable: false),
                    sync_folders = table.Column<string[]>(type: "text[]", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    last_synced_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    sync_status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "Idle"),
                    last_error_message = table.Column<string>(type: "text", nullable: true),
                    total_synced_count = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("pk_email_sync_accounts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "email_sync_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email_sync_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "Success"),
                    messages_fetched = table.Column<int>(type: "integer", nullable: false),
                    messages_created = table.Column<int>(type: "integer", nullable: false),
                    messages_linked = table.Column<int>(type: "integer", nullable: false),
                    messages_skipped = table.Column<int>(type: "integer", nullable: false),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    error_message = table.Column<string>(type: "text", nullable: true),
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
                    table.PrimaryKey("pk_email_sync_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "email_sync_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email_sync_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    external_message_id = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    thread_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    subject = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    from_address = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    from_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    to_addresses = table.Column<string[]>(type: "text[]", nullable: false),
                    cc_addresses = table.Column<string[]>(type: "text[]", nullable: false),
                    bcc_addresses = table.Column<string[]>(type: "text[]", nullable: false),
                    date_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    body_plain = table.Column<string>(type: "text", nullable: true),
                    body_html = table.Column<string>(type: "text", nullable: true),
                    snippet = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    direction = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "Inbound"),
                    is_read = table.Column<bool>(type: "boolean", nullable: false),
                    has_attachments = table.Column<bool>(type: "boolean", nullable: false),
                    attachments_json = table.Column<string>(type: "text", nullable: false),
                    lead_id = table.Column<Guid>(type: "uuid", nullable: true),
                    contact_id = table.Column<Guid>(type: "uuid", nullable: true),
                    account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    deal_id = table.Column<Guid>(type: "uuid", nullable: true),
                    synced_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
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
                    table.PrimaryKey("pk_email_sync_messages", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_email_sync_accounts_tenant_id",
                table: "email_sync_accounts",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_email_sync_accounts_tenant_id_email_address",
                table: "email_sync_accounts",
                columns: new[] { "tenant_id", "email_address" });

            migrationBuilder.CreateIndex(
                name: "ix_email_sync_accounts_tenant_id_user_id",
                table: "email_sync_accounts",
                columns: new[] { "tenant_id", "user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_email_sync_logs_tenant_id",
                table: "email_sync_logs",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_email_sync_logs_tenant_id_email_sync_account_id_started_at",
                table: "email_sync_logs",
                columns: new[] { "tenant_id", "email_sync_account_id", "started_at" });

            migrationBuilder.CreateIndex(
                name: "ix_email_sync_messages_tenant_id",
                table: "email_sync_messages",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_email_sync_messages_tenant_id_contact_id",
                table: "email_sync_messages",
                columns: new[] { "tenant_id", "contact_id" });

            migrationBuilder.CreateIndex(
                name: "ix_email_sync_messages_tenant_id_email_sync_account_id_date_utc",
                table: "email_sync_messages",
                columns: new[] { "tenant_id", "email_sync_account_id", "date_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_email_sync_messages_tenant_id_external_message_id",
                table: "email_sync_messages",
                columns: new[] { "tenant_id", "external_message_id" });

            migrationBuilder.CreateIndex(
                name: "ix_email_sync_messages_tenant_id_lead_id",
                table: "email_sync_messages",
                columns: new[] { "tenant_id", "lead_id" });

            migrationBuilder.CreateIndex(
                name: "ix_email_sync_messages_tenant_id_thread_id",
                table: "email_sync_messages",
                columns: new[] { "tenant_id", "thread_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "email_sync_accounts");

            migrationBuilder.DropTable(
                name: "email_sync_logs");

            migrationBuilder.DropTable(
                name: "email_sync_messages");
        }
    }
}
