using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MindLift.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Unique_Indexes_Ignore_Deleted : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_teams_tenant_id_name",
                table: "teams");

            migrationBuilder.DropIndex(
                name: "ix_roles_tenant_id_name",
                table: "roles");

            migrationBuilder.DropIndex(
                name: "ix_pipelines_tenant_id_name",
                table: "pipelines");

            migrationBuilder.DropIndex(
                name: "ix_picklist_values_tenant_id_picklist_key_value",
                table: "picklist_values");

            migrationBuilder.DropIndex(
                name: "ix_layouts_tenant_id_entity_type_kind",
                table: "layouts");

            migrationBuilder.DropIndex(
                name: "ix_custom_field_definitions_tenant_id_entity_type_key",
                table: "custom_field_definitions");

            migrationBuilder.DropIndex(
                name: "ix_branches_tenant_id_name",
                table: "branches");

            migrationBuilder.CreateIndex(
                name: "ix_teams_tenant_id_name",
                table: "teams",
                columns: new[] { "tenant_id", "name" },
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_roles_tenant_id_name",
                table: "roles",
                columns: new[] { "tenant_id", "name" },
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_pipelines_tenant_id_name",
                table: "pipelines",
                columns: new[] { "tenant_id", "name" },
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_picklist_values_tenant_id_picklist_key_value",
                table: "picklist_values",
                columns: new[] { "tenant_id", "picklist_key", "value" },
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_layouts_tenant_id_entity_type_kind",
                table: "layouts",
                columns: new[] { "tenant_id", "entity_type", "kind" },
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_custom_field_definitions_tenant_id_entity_type_key",
                table: "custom_field_definitions",
                columns: new[] { "tenant_id", "entity_type", "key" },
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_branches_tenant_id_name",
                table: "branches",
                columns: new[] { "tenant_id", "name" },
                unique: true,
                filter: "is_deleted = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_teams_tenant_id_name",
                table: "teams");

            migrationBuilder.DropIndex(
                name: "ix_roles_tenant_id_name",
                table: "roles");

            migrationBuilder.DropIndex(
                name: "ix_pipelines_tenant_id_name",
                table: "pipelines");

            migrationBuilder.DropIndex(
                name: "ix_picklist_values_tenant_id_picklist_key_value",
                table: "picklist_values");

            migrationBuilder.DropIndex(
                name: "ix_layouts_tenant_id_entity_type_kind",
                table: "layouts");

            migrationBuilder.DropIndex(
                name: "ix_custom_field_definitions_tenant_id_entity_type_key",
                table: "custom_field_definitions");

            migrationBuilder.DropIndex(
                name: "ix_branches_tenant_id_name",
                table: "branches");

            migrationBuilder.CreateIndex(
                name: "ix_teams_tenant_id_name",
                table: "teams",
                columns: new[] { "tenant_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_roles_tenant_id_name",
                table: "roles",
                columns: new[] { "tenant_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pipelines_tenant_id_name",
                table: "pipelines",
                columns: new[] { "tenant_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_picklist_values_tenant_id_picklist_key_value",
                table: "picklist_values",
                columns: new[] { "tenant_id", "picklist_key", "value" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_layouts_tenant_id_entity_type_kind",
                table: "layouts",
                columns: new[] { "tenant_id", "entity_type", "kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_custom_field_definitions_tenant_id_entity_type_key",
                table: "custom_field_definitions",
                columns: new[] { "tenant_id", "entity_type", "key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_branches_tenant_id_name",
                table: "branches",
                columns: new[] { "tenant_id", "name" },
                unique: true);
        }
    }
}
