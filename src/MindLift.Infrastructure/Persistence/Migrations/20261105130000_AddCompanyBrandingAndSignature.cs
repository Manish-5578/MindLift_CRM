using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MindLift.Infrastructure.Persistence;

#nullable disable

namespace MindLift.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20261105130000_AddCompanyBrandingAndSignature")]
    public partial class AddCompanyBrandingAndSignature : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "address",
                table: "tenant_settings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "logo_url",
                table: "tenant_settings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "signature_url",
                table: "tenant_settings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "signatory_name",
                table: "tenant_settings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "signatory_title",
                table: "tenant_settings",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "address",
                table: "tenant_settings");

            migrationBuilder.DropColumn(
                name: "logo_url",
                table: "tenant_settings");

            migrationBuilder.DropColumn(
                name: "signature_url",
                table: "tenant_settings");

            migrationBuilder.DropColumn(
                name: "signatory_name",
                table: "tenant_settings");

            migrationBuilder.DropColumn(
                name: "signatory_title",
                table: "tenant_settings");
        }
    }
}

