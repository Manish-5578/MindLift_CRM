using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MindLift.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDependentPicklistMaps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "dependent_picklist_maps",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    entity_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    parent_field = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    child_field = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    mapping_json = table.Column<string>(type: "jsonb", nullable: false),
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
                    table.PrimaryKey("pk_dependent_picklist_maps", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_dependent_picklist_maps_tenant_id",
                table: "dependent_picklist_maps",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_dependent_picklist_maps_tenant_id_entity_type_parent_field_",
                table: "dependent_picklist_maps",
                columns: new[] { "tenant_id", "entity_type", "parent_field", "child_field" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "dependent_picklist_maps");
        }
    }
}
