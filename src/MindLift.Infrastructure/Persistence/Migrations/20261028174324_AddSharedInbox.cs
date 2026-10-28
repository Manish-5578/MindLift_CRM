using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MindLift.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSharedInbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "shared_mailbox_members",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    mailbox_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "Agent"),
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
                    table.PrimaryKey("pk_shared_mailbox_members", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "shared_mailboxes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    email_address = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    auto_assignment_rule = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "Manual"),
                    default_assignee_user_id = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("pk_shared_mailboxes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "shared_thread_collision_locks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    thread_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    action = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "Drafting"),
                    locked_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expires_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
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
                    table.PrimaryKey("pk_shared_thread_collision_locks", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "shared_thread_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    thread_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sender_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "Customer"),
                    sender_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    sender_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    sender_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    body = table.Column<string>(type: "text", nullable: false),
                    is_internal_note = table.Column<bool>(type: "boolean", nullable: false),
                    sent_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
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
                    table.PrimaryKey("pk_shared_thread_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "shared_threads",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    mailbox_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    customer_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    customer_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "Open"),
                    priority = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "Medium"),
                    assigned_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    assigned_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_message_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    first_response_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    closed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lead_id = table.Column<Guid>(type: "uuid", nullable: true),
                    contact_id = table.Column<Guid>(type: "uuid", nullable: true),
                    account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    deal_id = table.Column<Guid>(type: "uuid", nullable: true),
                    message_count = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("pk_shared_threads", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_shared_mailbox_members_tenant_id",
                table: "shared_mailbox_members",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_shared_mailbox_members_tenant_id_mailbox_id_user_id",
                table: "shared_mailbox_members",
                columns: new[] { "tenant_id", "mailbox_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_shared_mailboxes_tenant_id",
                table: "shared_mailboxes",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_shared_mailboxes_tenant_id_email_address",
                table: "shared_mailboxes",
                columns: new[] { "tenant_id", "email_address" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_shared_thread_collision_locks_tenant_id",
                table: "shared_thread_collision_locks",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_shared_thread_collision_locks_tenant_id_expires_at_utc",
                table: "shared_thread_collision_locks",
                columns: new[] { "tenant_id", "expires_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_shared_thread_collision_locks_tenant_id_thread_id_user_id",
                table: "shared_thread_collision_locks",
                columns: new[] { "tenant_id", "thread_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_shared_thread_messages_tenant_id",
                table: "shared_thread_messages",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_shared_thread_messages_tenant_id_sent_at_utc",
                table: "shared_thread_messages",
                columns: new[] { "tenant_id", "sent_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_shared_thread_messages_tenant_id_thread_id",
                table: "shared_thread_messages",
                columns: new[] { "tenant_id", "thread_id" });

            migrationBuilder.CreateIndex(
                name: "ix_shared_threads_tenant_id",
                table: "shared_threads",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_shared_threads_tenant_id_assigned_user_id",
                table: "shared_threads",
                columns: new[] { "tenant_id", "assigned_user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_shared_threads_tenant_id_last_message_at_utc",
                table: "shared_threads",
                columns: new[] { "tenant_id", "last_message_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_shared_threads_tenant_id_mailbox_id",
                table: "shared_threads",
                columns: new[] { "tenant_id", "mailbox_id" });

            migrationBuilder.CreateIndex(
                name: "ix_shared_threads_tenant_id_status",
                table: "shared_threads",
                columns: new[] { "tenant_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "shared_mailbox_members");

            migrationBuilder.DropTable(
                name: "shared_mailboxes");

            migrationBuilder.DropTable(
                name: "shared_thread_collision_locks");

            migrationBuilder.DropTable(
                name: "shared_thread_messages");

            migrationBuilder.DropTable(
                name: "shared_threads");
        }
    }
}
