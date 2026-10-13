using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MindLift.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Deals_M8 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "description",
                table: "deals",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "last_activity_at",
                table: "deals",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "lost_notes",
                table: "deals",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "next_activity_at",
                table: "deals",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "primary_contact_id",
                table: "deals",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "probability_overridden",
                table: "deals",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "stage_changed_at",
                table: "deals",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.Sql("UPDATE deals SET stage_changed_at = created_at");

            migrationBuilder.CreateTable(
                name: "deal_contacts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    deal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contact_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    is_primary = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("pk_deal_contacts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "deal_stage_history",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    deal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_stage_id = table.Column<Guid>(type: "uuid", nullable: true),
                    to_stage_id = table.Column<Guid>(type: "uuid", nullable: false),
                    changed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    changed_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    seconds_in_from_stage = table.Column<long>(type: "bigint", nullable: true),
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
                    table.PrimaryKey("pk_deal_stage_history", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_deal_contacts_deal_id_contact_id",
                table: "deal_contacts",
                columns: new[] { "deal_id", "contact_id" },
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_deal_contacts_tenant_id",
                table: "deal_contacts",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_deal_contacts_tenant_id_deal_id",
                table: "deal_contacts",
                columns: new[] { "tenant_id", "deal_id" });

            migrationBuilder.CreateIndex(
                name: "ix_deal_stage_history_tenant_id",
                table: "deal_stage_history",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_deal_stage_history_tenant_id_deal_id_changed_at",
                table: "deal_stage_history",
                columns: new[] { "tenant_id", "deal_id", "changed_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "deal_contacts");

            migrationBuilder.DropTable(
                name: "deal_stage_history");

            migrationBuilder.DropColumn(
                name: "description",
                table: "deals");

            migrationBuilder.DropColumn(
                name: "last_activity_at",
                table: "deals");

            migrationBuilder.DropColumn(
                name: "lost_notes",
                table: "deals");

            migrationBuilder.DropColumn(
                name: "next_activity_at",
                table: "deals");

            migrationBuilder.DropColumn(
                name: "primary_contact_id",
                table: "deals");

            migrationBuilder.DropColumn(
                name: "probability_overridden",
                table: "deals");

            migrationBuilder.DropColumn(
                name: "stage_changed_at",
                table: "deals");
        }
    }
}
