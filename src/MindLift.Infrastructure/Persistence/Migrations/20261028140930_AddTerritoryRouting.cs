using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MindLift.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTerritoryRouting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "lead_routing_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    lead_id = table.Column<Guid>(type: "uuid", nullable: false),
                    territory_id = table.Column<Guid>(type: "uuid", nullable: true),
                    territory_name = table.Column<string>(type: "text", nullable: true),
                    assigned_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    routing_strategy = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    success = table.Column<bool>(type: "boolean", nullable: false),
                    routed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
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
                    table.PrimaryKey("pk_lead_routing_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "territories",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    routing_strategy = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "RoundRobin"),
                    country_codes = table.Column<string[]>(type: "text[]", nullable: false),
                    regions = table.Column<string[]>(type: "text[]", nullable: false),
                    cities = table.Column<string[]>(type: "text[]", nullable: false),
                    postal_codes = table.Column<string[]>(type: "text[]", nullable: false),
                    industries = table.Column<string[]>(type: "text[]", nullable: false),
                    min_revenue = table.Column<decimal>(type: "numeric", nullable: true),
                    max_revenue = table.Column<decimal>(type: "numeric", nullable: true),
                    min_company_size = table.Column<int>(type: "integer", nullable: true),
                    max_company_size = table.Column<int>(type: "integer", nullable: true),
                    fallback_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    last_assigned_member_id = table.Column<Guid>(type: "uuid", nullable: true),
                    last_assigned_index = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("pk_territories", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "territory_members",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    territory_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    weight = table.Column<int>(type: "integer", nullable: false),
                    max_capacity = table.Column<int>(type: "integer", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    assigned_lead_count = table.Column<int>(type: "integer", nullable: false),
                    last_assigned_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
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
                    table.PrimaryKey("pk_territory_members", x => x.id);
                    table.ForeignKey(
                        name: "fk_territory_members_territories_territory_id",
                        column: x => x.territory_id,
                        principalTable: "territories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_lead_routing_logs_tenant_id",
                table: "lead_routing_logs",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_lead_routing_logs_tenant_id_lead_id",
                table: "lead_routing_logs",
                columns: new[] { "tenant_id", "lead_id" });

            migrationBuilder.CreateIndex(
                name: "ix_lead_routing_logs_tenant_id_routed_at",
                table: "lead_routing_logs",
                columns: new[] { "tenant_id", "routed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_lead_routing_logs_tenant_id_territory_id",
                table: "lead_routing_logs",
                columns: new[] { "tenant_id", "territory_id" });

            migrationBuilder.CreateIndex(
                name: "ix_territories_tenant_id",
                table: "territories",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_territories_tenant_id_is_active_priority",
                table: "territories",
                columns: new[] { "tenant_id", "is_active", "priority" });

            migrationBuilder.CreateIndex(
                name: "ix_territory_members_tenant_id",
                table: "territory_members",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_territory_members_tenant_id_territory_id_user_id",
                table: "territory_members",
                columns: new[] { "tenant_id", "territory_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_territory_members_tenant_id_user_id",
                table: "territory_members",
                columns: new[] { "tenant_id", "user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_territory_members_territory_id",
                table: "territory_members",
                column: "territory_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "lead_routing_logs");

            migrationBuilder.DropTable(
                name: "territory_members");

            migrationBuilder.DropTable(
                name: "territories");
        }
    }
}
