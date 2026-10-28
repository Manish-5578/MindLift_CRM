using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MindLift.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddQuoteESign : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "decline_reason",
                table: "quotes",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "e_sign_status",
                table: "quotes",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Draft");

            migrationBuilder.AddColumn<string>(
                name: "security_hash",
                table: "quotes",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "signature_data",
                table: "quotes",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "signed_at",
                table: "quotes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "signer_email",
                table: "quotes",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "signer_ip_address",
                table: "quotes",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "signer_name",
                table: "quotes",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "signer_title",
                table: "quotes",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "signing_expires_at",
                table: "quotes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "signing_token",
                table: "quotes",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "quote_signature_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    quote_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    actor_email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    actor_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    ip_address = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    user_agent = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    details = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
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
                    table.PrimaryKey("pk_quote_signature_logs", x => x.id);
                    table.ForeignKey(
                        name: "fk_quote_signature_logs_quotes_quote_id",
                        column: x => x.quote_id,
                        principalTable: "quotes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_quotes_signing_token",
                table: "quotes",
                column: "signing_token");

            migrationBuilder.CreateIndex(
                name: "ix_quotes_tenant_id_e_sign_status",
                table: "quotes",
                columns: new[] { "tenant_id", "e_sign_status" });

            migrationBuilder.CreateIndex(
                name: "ix_quote_signature_logs_quote_id",
                table: "quote_signature_logs",
                column: "quote_id");

            migrationBuilder.CreateIndex(
                name: "ix_quote_signature_logs_tenant_id",
                table: "quote_signature_logs",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_quote_signature_logs_tenant_id_quote_id_timestamp",
                table: "quote_signature_logs",
                columns: new[] { "tenant_id", "quote_id", "timestamp" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "quote_signature_logs");

            migrationBuilder.DropIndex(
                name: "ix_quotes_signing_token",
                table: "quotes");

            migrationBuilder.DropIndex(
                name: "ix_quotes_tenant_id_e_sign_status",
                table: "quotes");

            migrationBuilder.DropColumn(
                name: "decline_reason",
                table: "quotes");

            migrationBuilder.DropColumn(
                name: "e_sign_status",
                table: "quotes");

            migrationBuilder.DropColumn(
                name: "security_hash",
                table: "quotes");

            migrationBuilder.DropColumn(
                name: "signature_data",
                table: "quotes");

            migrationBuilder.DropColumn(
                name: "signed_at",
                table: "quotes");

            migrationBuilder.DropColumn(
                name: "signer_email",
                table: "quotes");

            migrationBuilder.DropColumn(
                name: "signer_ip_address",
                table: "quotes");

            migrationBuilder.DropColumn(
                name: "signer_name",
                table: "quotes");

            migrationBuilder.DropColumn(
                name: "signer_title",
                table: "quotes");

            migrationBuilder.DropColumn(
                name: "signing_expires_at",
                table: "quotes");

            migrationBuilder.DropColumn(
                name: "signing_token",
                table: "quotes");
        }
    }
}
