using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MindLift.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Contacts_Accounts_M7 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "description",
                table: "contacts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "job_title",
                table: "contacts",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "website",
                table: "accounts",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "description",
                table: "accounts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "industry",
                table: "accounts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "parent_account_id",
                table: "accounts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "phone",
                table: "accounts",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_accounts_tenant_id_parent_account_id",
                table: "accounts",
                columns: new[] { "tenant_id", "parent_account_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_accounts_tenant_id_parent_account_id",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "description",
                table: "contacts");

            migrationBuilder.DropColumn(
                name: "job_title",
                table: "contacts");

            migrationBuilder.DropColumn(
                name: "description",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "industry",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "parent_account_id",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "phone",
                table: "accounts");

            migrationBuilder.AlterColumn<string>(
                name: "website",
                table: "accounts",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(300)",
                oldMaxLength: 300,
                oldNullable: true);
        }
    }
}
