using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MindLift.Infrastructure.Persistence;

#nullable disable

namespace MindLift.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20261105143000_EnhanceAuditTrail")]
    public partial class EnhanceAuditTrail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "user_name",
                table: "audit_logs",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "user_email",
                table: "audit_logs",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "module",
                table: "audit_logs",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "record_title",
                table: "audit_logs",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "summary",
                table: "audit_logs",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ip_address",
                table: "audit_logs",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_tenant_id_module_occurred_at",
                table: "audit_logs",
                columns: new[] { "tenant_id", "module", "occurred_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_audit_logs_tenant_id_module_occurred_at",
                table: "audit_logs");

            migrationBuilder.DropColumn(
                name: "user_name",
                table: "audit_logs");

            migrationBuilder.DropColumn(
                name: "user_email",
                table: "audit_logs");

            migrationBuilder.DropColumn(
                name: "module",
                table: "audit_logs");

            migrationBuilder.DropColumn(
                name: "record_title",
                table: "audit_logs");

            migrationBuilder.DropColumn(
                name: "summary",
                table: "audit_logs");

            migrationBuilder.DropColumn(
                name: "ip_address",
                table: "audit_logs");
        }
    }
}

