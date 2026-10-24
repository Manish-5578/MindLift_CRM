using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MindLift.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NumberSequencesAndIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "number_sequences",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    last_value = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_number_sequences", x => new { x.tenant_id, x.key });
                });

            migrationBuilder.CreateIndex(
                name: "ix_deals_tenant_id_account_id",
                table: "deals",
                columns: new[] { "tenant_id", "account_id" });

            migrationBuilder.CreateIndex(
                name: "ix_deals_tenant_id_primary_contact_id",
                table: "deals",
                columns: new[] { "tenant_id", "primary_contact_id" });

            migrationBuilder.CreateIndex(
                name: "ix_contacts_tenant_id_normalized_email",
                table: "contacts",
                columns: new[] { "tenant_id", "normalized_email" });

            migrationBuilder.CreateIndex(
                name: "ix_contacts_tenant_id_normalized_phone",
                table: "contacts",
                columns: new[] { "tenant_id", "normalized_phone" });

            migrationBuilder.CreateIndex(
                name: "ix_business_documents_tenant_id_associated_account_id",
                table: "business_documents",
                columns: new[] { "tenant_id", "associated_account_id" });

            migrationBuilder.CreateIndex(
                name: "ix_business_documents_tenant_id_associated_contact_id",
                table: "business_documents",
                columns: new[] { "tenant_id", "associated_contact_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "number_sequences");

            migrationBuilder.DropIndex(
                name: "ix_deals_tenant_id_account_id",
                table: "deals");

            migrationBuilder.DropIndex(
                name: "ix_deals_tenant_id_primary_contact_id",
                table: "deals");

            migrationBuilder.DropIndex(
                name: "ix_contacts_tenant_id_normalized_email",
                table: "contacts");

            migrationBuilder.DropIndex(
                name: "ix_contacts_tenant_id_normalized_phone",
                table: "contacts");

            migrationBuilder.DropIndex(
                name: "ix_business_documents_tenant_id_associated_account_id",
                table: "business_documents");

            migrationBuilder.DropIndex(
                name: "ix_business_documents_tenant_id_associated_contact_id",
                table: "business_documents");
        }
    }
}
