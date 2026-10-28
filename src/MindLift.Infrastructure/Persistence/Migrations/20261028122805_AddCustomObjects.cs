using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MindLift.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomObjects : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "custom_object_definitions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    plural_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    icon = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    title_field = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    fields_json = table.Column<string>(type: "jsonb", nullable: false),
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
                    table.PrimaryKey("pk_custom_object_definitions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "custom_object_records",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    object_definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    record_number = table.Column<int>(type: "integer", nullable: false),
                    display_name = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    data_json = table.Column<string>(type: "jsonb", nullable: false),
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
                    table.PrimaryKey("pk_custom_object_records", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_custom_object_definitions_tenant_id",
                table: "custom_object_definitions",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_custom_object_definitions_tenant_id_slug",
                table: "custom_object_definitions",
                columns: new[] { "tenant_id", "slug" });

            migrationBuilder.CreateIndex(
                name: "ix_custom_object_records_tenant_id",
                table: "custom_object_records",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_custom_object_records_tenant_id_object_definition_id_create",
                table: "custom_object_records",
                columns: new[] { "tenant_id", "object_definition_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_custom_object_records_tenant_id_object_definition_id_record",
                table: "custom_object_records",
                columns: new[] { "tenant_id", "object_definition_id", "record_number" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "custom_object_definitions");

            migrationBuilder.DropTable(
                name: "custom_object_records");
        }
    }
}
