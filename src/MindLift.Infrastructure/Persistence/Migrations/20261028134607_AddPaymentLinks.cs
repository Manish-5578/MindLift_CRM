using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MindLift.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "payment_links",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    link_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    paid_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "Active"),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: true),
                    quote_id = table.Column<Guid>(type: "uuid", nullable: true),
                    customer_email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    customer_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    payment_token = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    gateway_provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "Hosted"),
                    gateway_reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    allow_partial = table.Column<bool>(type: "boolean", nullable: false),
                    paid_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    payment_method_used = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    receipt_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
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
                    table.PrimaryKey("pk_payment_links", x => x.id);
                    table.ForeignKey(
                        name: "fk_payment_links_invoices_invoice_id",
                        column: x => x.invoice_id,
                        principalTable: "invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_payment_links_quotes_quote_id",
                        column: x => x.quote_id,
                        principalTable: "quotes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "payment_webhook_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    event_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    event_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    payload_json = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    error_message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    received_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
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
                    table.PrimaryKey("pk_payment_webhook_logs", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_payment_links_invoice_id",
                table: "payment_links",
                column: "invoice_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_links_payment_token",
                table: "payment_links",
                column: "payment_token");

            migrationBuilder.CreateIndex(
                name: "ix_payment_links_quote_id",
                table: "payment_links",
                column: "quote_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_links_tenant_id",
                table: "payment_links",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_links_tenant_id_invoice_id",
                table: "payment_links",
                columns: new[] { "tenant_id", "invoice_id" });

            migrationBuilder.CreateIndex(
                name: "ix_payment_links_tenant_id_link_number",
                table: "payment_links",
                columns: new[] { "tenant_id", "link_number" });

            migrationBuilder.CreateIndex(
                name: "ix_payment_links_tenant_id_quote_id",
                table: "payment_links",
                columns: new[] { "tenant_id", "quote_id" });

            migrationBuilder.CreateIndex(
                name: "ix_payment_links_tenant_id_status",
                table: "payment_links",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_payment_webhook_logs_tenant_id",
                table: "payment_webhook_logs",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_webhook_logs_tenant_id_provider_event_id",
                table: "payment_webhook_logs",
                columns: new[] { "tenant_id", "provider", "event_id" });

            migrationBuilder.CreateIndex(
                name: "ix_payment_webhook_logs_tenant_id_received_at",
                table: "payment_webhook_logs",
                columns: new[] { "tenant_id", "received_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payment_links");

            migrationBuilder.DropTable(
                name: "payment_webhook_logs");
        }
    }
}
