using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MindLift.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddContactAccountLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "contact_account_links",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    contact_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
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
                    table.PrimaryKey("pk_contact_account_links", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_contact_account_links_tenant_id",
                table: "contact_account_links",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_contact_account_links_tenant_id_account_id",
                table: "contact_account_links",
                columns: new[] { "tenant_id", "account_id" });

            migrationBuilder.CreateIndex(
                name: "ix_contact_account_links_tenant_id_contact_id_account_id",
                table: "contact_account_links",
                columns: new[] { "tenant_id", "contact_id", "account_id" },
                unique: true,
                filter: "is_deleted = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "contact_account_links");
        }
    }
}
