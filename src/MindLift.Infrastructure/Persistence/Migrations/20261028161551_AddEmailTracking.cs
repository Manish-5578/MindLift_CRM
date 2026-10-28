using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MindLift.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "email_tracking_settings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    track_opens = table.Column<bool>(type: "boolean", nullable: false),
                    track_clicks = table.Column<bool>(type: "boolean", nullable: false),
                    notify_on_first_open = table.Column<bool>(type: "boolean", nullable: false),
                    notify_on_link_click = table.Column<bool>(type: "boolean", nullable: false),
                    custom_tracking_domain = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
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
                    table.PrimaryKey("pk_email_tracking_settings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tracked_emails",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tracking_token = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    recipient_email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    subject = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    sender_email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    sent_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    lead_id = table.Column<Guid>(type: "uuid", nullable: true),
                    contact_id = table.Column<Guid>(type: "uuid", nullable: true),
                    account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    deal_id = table.Column<Guid>(type: "uuid", nullable: true),
                    message_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sent_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    open_count = table.Column<int>(type: "integer", nullable: false),
                    unique_open_count = table.Column<int>(type: "integer", nullable: false),
                    first_opened_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_opened_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    click_count = table.Column<int>(type: "integer", nullable: false),
                    unique_click_count = table.Column<int>(type: "integer", nullable: false),
                    first_clicked_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_clicked_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_bounced = table.Column<bool>(type: "boolean", nullable: false),
                    bounce_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
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
                    table.PrimaryKey("pk_tracked_emails", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tracked_email_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tracked_email_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "Open"),
                    link_token = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    ip_address = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    user_agent = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    device_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true, defaultValue: "Desktop"),
                    occurred_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
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
                    table.PrimaryKey("pk_tracked_email_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_tracked_email_events_tracked_emails_tracked_email_id",
                        column: x => x.tracked_email_id,
                        principalTable: "tracked_emails",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tracked_email_links",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tracked_email_id = table.Column<Guid>(type: "uuid", nullable: false),
                    link_token = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    original_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    click_count = table.Column<int>(type: "integer", nullable: false),
                    first_clicked_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_clicked_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
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
                    table.PrimaryKey("pk_tracked_email_links", x => x.id);
                    table.ForeignKey(
                        name: "fk_tracked_email_links_tracked_emails_tracked_email_id",
                        column: x => x.tracked_email_id,
                        principalTable: "tracked_emails",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_email_tracking_settings_tenant_id",
                table: "email_tracking_settings",
                column: "tenant_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tracked_email_events_tenant_id",
                table: "tracked_email_events",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_tracked_email_events_tenant_id_tracked_email_id_occurred_at",
                table: "tracked_email_events",
                columns: new[] { "tenant_id", "tracked_email_id", "occurred_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_tracked_email_events_tracked_email_id",
                table: "tracked_email_events",
                column: "tracked_email_id");

            migrationBuilder.CreateIndex(
                name: "ix_tracked_email_links_link_token",
                table: "tracked_email_links",
                column: "link_token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tracked_email_links_tenant_id",
                table: "tracked_email_links",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_tracked_email_links_tenant_id_tracked_email_id",
                table: "tracked_email_links",
                columns: new[] { "tenant_id", "tracked_email_id" });

            migrationBuilder.CreateIndex(
                name: "ix_tracked_email_links_tracked_email_id",
                table: "tracked_email_links",
                column: "tracked_email_id");

            migrationBuilder.CreateIndex(
                name: "ix_tracked_emails_tenant_id",
                table: "tracked_emails",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_tracked_emails_tenant_id_contact_id",
                table: "tracked_emails",
                columns: new[] { "tenant_id", "contact_id" });

            migrationBuilder.CreateIndex(
                name: "ix_tracked_emails_tenant_id_lead_id",
                table: "tracked_emails",
                columns: new[] { "tenant_id", "lead_id" });

            migrationBuilder.CreateIndex(
                name: "ix_tracked_emails_tenant_id_recipient_email",
                table: "tracked_emails",
                columns: new[] { "tenant_id", "recipient_email" });

            migrationBuilder.CreateIndex(
                name: "ix_tracked_emails_tenant_id_sent_at_utc",
                table: "tracked_emails",
                columns: new[] { "tenant_id", "sent_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_tracked_emails_tracking_token",
                table: "tracked_emails",
                column: "tracking_token",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "email_tracking_settings");

            migrationBuilder.DropTable(
                name: "tracked_email_events");

            migrationBuilder.DropTable(
                name: "tracked_email_links");

            migrationBuilder.DropTable(
                name: "tracked_emails");
        }
    }
}
