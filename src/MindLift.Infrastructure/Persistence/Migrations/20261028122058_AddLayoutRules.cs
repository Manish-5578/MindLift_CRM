using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MindLift.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLayoutRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "layout_rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    entity_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    condition_json = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    show_fields = table.Column<string[]>(type: "text[]", nullable: false),
                    hide_fields = table.Column<string[]>(type: "text[]", nullable: false),
                    mandatory_fields = table.Column<string[]>(type: "text[]", nullable: false),
                    read_only_fields = table.Column<string[]>(type: "text[]", nullable: false),
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
                    table.PrimaryKey("pk_layout_rules", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_layout_rules_tenant_id",
                table: "layout_rules",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_layout_rules_tenant_id_entity_type_is_active_priority",
                table: "layout_rules",
                columns: new[] { "tenant_id", "entity_type", "is_active", "priority" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "layout_rules");
        }
    }
}
