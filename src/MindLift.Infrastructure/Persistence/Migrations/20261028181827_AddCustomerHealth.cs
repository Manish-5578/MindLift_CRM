using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MindLift.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerHealth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "customer_surveys",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    score = table.Column<int>(type: "integer", nullable: false),
                    sentiment = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    channel = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    customer_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    customer_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    customer_phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    contact_id = table.Column<Guid>(type: "uuid", nullable: true),
                    ticket_id = table.Column<Guid>(type: "uuid", nullable: true),
                    deal_id = table.Column<Guid>(type: "uuid", nullable: true),
                    submitted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
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
                    table.PrimaryKey("pk_customer_surveys", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_customer_surveys_tenant_id",
                table: "customer_surveys",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_customer_surveys_tenant_id_account_id",
                table: "customer_surveys",
                columns: new[] { "tenant_id", "account_id" });

            migrationBuilder.CreateIndex(
                name: "ix_customer_surveys_tenant_id_contact_id",
                table: "customer_surveys",
                columns: new[] { "tenant_id", "contact_id" });

            migrationBuilder.CreateIndex(
                name: "ix_customer_surveys_tenant_id_sentiment",
                table: "customer_surveys",
                columns: new[] { "tenant_id", "sentiment" });

            migrationBuilder.CreateIndex(
                name: "ix_customer_surveys_tenant_id_type",
                table: "customer_surveys",
                columns: new[] { "tenant_id", "type" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "customer_surveys");
        }
    }
}
