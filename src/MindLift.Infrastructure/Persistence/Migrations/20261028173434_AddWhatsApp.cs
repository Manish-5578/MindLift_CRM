using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MindLift.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWhatsApp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "whatsapp_conversations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_phone_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    last_customer_message_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_message_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    unread_count = table.Column<int>(type: "integer", nullable: false),
                    lead_id = table.Column<Guid>(type: "uuid", nullable: true),
                    contact_id = table.Column<Guid>(type: "uuid", nullable: true),
                    account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    deal_id = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("pk_whatsapp_conversations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "whatsapp_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    message_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    direction = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "Inbound"),
                    from_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    to_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    message_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "text"),
                    content = table.Column<string>(type: "text", nullable: false),
                    template_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    template_language = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    template_parameters = table.Column<string[]>(type: "text[]", nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "sent"),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    sent_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    delivered_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    read_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lead_id = table.Column<Guid>(type: "uuid", nullable: true),
                    contact_id = table.Column<Guid>(type: "uuid", nullable: true),
                    account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    deal_id = table.Column<Guid>(type: "uuid", nullable: true),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("pk_whatsapp_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "whatsapp_settings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    phone_number_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    waba_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    display_phone_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    access_token = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    webhook_verify_token = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    enforce24hour_window = table.Column<bool>(type: "boolean", nullable: false),
                    auto_create_leads = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("pk_whatsapp_settings", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_whatsapp_conversations_tenant_id",
                table: "whatsapp_conversations",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_whatsapp_conversations_tenant_id_customer_phone_number",
                table: "whatsapp_conversations",
                columns: new[] { "tenant_id", "customer_phone_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_whatsapp_conversations_tenant_id_last_message_at_utc",
                table: "whatsapp_conversations",
                columns: new[] { "tenant_id", "last_message_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_whatsapp_messages_message_id",
                table: "whatsapp_messages",
                column: "message_id");

            migrationBuilder.CreateIndex(
                name: "ix_whatsapp_messages_tenant_id",
                table: "whatsapp_messages",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_whatsapp_messages_tenant_id_conversation_id",
                table: "whatsapp_messages",
                columns: new[] { "tenant_id", "conversation_id" });

            migrationBuilder.CreateIndex(
                name: "ix_whatsapp_messages_tenant_id_sent_at_utc",
                table: "whatsapp_messages",
                columns: new[] { "tenant_id", "sent_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_whatsapp_settings_phone_number_id",
                table: "whatsapp_settings",
                column: "phone_number_id");

            migrationBuilder.CreateIndex(
                name: "ix_whatsapp_settings_tenant_id",
                table: "whatsapp_settings",
                column: "tenant_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "whatsapp_conversations");

            migrationBuilder.DropTable(
                name: "whatsapp_messages");

            migrationBuilder.DropTable(
                name: "whatsapp_settings");
        }
    }
}
