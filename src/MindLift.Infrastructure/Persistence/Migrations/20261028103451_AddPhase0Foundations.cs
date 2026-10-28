using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MindLift.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPhase0Foundations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "idempotency_records",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    scope = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
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
                    table.PrimaryKey("pk_idempotency_records", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "integration_credentials",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    encrypted_secret = table.Column<string>(type: "text", nullable: false),
                    settings_json = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    last_used_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    rotated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
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
                    table.PrimaryKey("pk_integration_credentials", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "timeline_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    entity_type = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    summary = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    detail_json = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    source = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    occurred_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
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
                    table.PrimaryKey("pk_timeline_events", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_idempotency_records_expires_at",
                table: "idempotency_records",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_idempotency_records_tenant_id",
                table: "idempotency_records",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_idempotency_records_tenant_id_scope_key",
                table: "idempotency_records",
                columns: new[] { "tenant_id", "scope", "key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_integration_credentials_tenant_id",
                table: "integration_credentials",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_integration_credentials_tenant_id_provider_label",
                table: "integration_credentials",
                columns: new[] { "tenant_id", "provider", "label" },
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_timeline_events_tenant_id",
                table: "timeline_events",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_timeline_events_tenant_id_entity_type_entity_id_occurred_at",
                table: "timeline_events",
                columns: new[] { "tenant_id", "entity_type", "entity_id", "occurred_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "idempotency_records");

            migrationBuilder.DropTable(
                name: "integration_credentials");

            migrationBuilder.DropTable(
                name: "timeline_events");
        }
    }
}
