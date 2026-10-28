using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MindLift.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddResponseSla : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "lead_sla_trackers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    lead_id = table.Column<Guid>(type: "uuid", nullable: false),
                    policy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    policy_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    due_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    responded_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    warning_notified_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    breached_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "Pending"),
                    escalated = table.Column<bool>(type: "boolean", nullable: false),
                    escalated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    escalated_to_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
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
                    table.PrimaryKey("pk_lead_sla_trackers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sla_policies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    max_response_minutes = table.Column<int>(type: "integer", nullable: false),
                    warning_threshold_percentage = table.Column<int>(type: "integer", nullable: false),
                    sources = table.Column<string[]>(type: "text[]", nullable: false),
                    ratings = table.Column<string[]>(type: "text[]", nullable: false),
                    escalation_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    escalation_action = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "ReassignAndNotify"),
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
                    table.PrimaryKey("pk_sla_policies", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_lead_sla_trackers_tenant_id",
                table: "lead_sla_trackers",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_lead_sla_trackers_tenant_id_lead_id",
                table: "lead_sla_trackers",
                columns: new[] { "tenant_id", "lead_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_lead_sla_trackers_tenant_id_status_due_at",
                table: "lead_sla_trackers",
                columns: new[] { "tenant_id", "status", "due_at" });

            migrationBuilder.CreateIndex(
                name: "ix_sla_policies_tenant_id",
                table: "sla_policies",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_sla_policies_tenant_id_is_active_priority",
                table: "sla_policies",
                columns: new[] { "tenant_id", "is_active", "priority" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "lead_sla_trackers");

            migrationBuilder.DropTable(
                name: "sla_policies");
        }
    }
}
