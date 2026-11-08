using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MindLift.Infrastructure.Persistence.Migrations;

public partial class AddInternalChatMessages : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "internal_chat_messages",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                sender_id = table.Column<Guid>(type: "uuid", nullable: false),
                sender_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                sender_email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                recipient_id = table.Column<Guid>(type: "uuid", nullable: true),
                channel_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                message = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                is_read = table.Column<bool>(type: "boolean", nullable: false),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                created_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                updated_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_internal_chat_messages", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "ix_internal_chat_messages_tenant_id_channel_name_created_at_utc",
            table: "internal_chat_messages",
            columns: new[] { "tenant_id", "channel_name", "created_at_utc" });

        migrationBuilder.CreateIndex(
            name: "ix_internal_chat_messages_tenant_id_recipient_id_sender_id_created_at_utc",
            table: "internal_chat_messages",
            columns: new[] { "tenant_id", "recipient_id", "sender_id", "created_at_utc" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "internal_chat_messages");
    }
}

