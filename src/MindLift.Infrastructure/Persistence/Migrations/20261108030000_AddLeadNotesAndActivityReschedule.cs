using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MindLift.Infrastructure.Persistence;

#nullable disable

namespace MindLift.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20261108030000_AddLeadNotesAndActivityReschedule")]
    public partial class AddLeadNotesAndActivityReschedule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "lead_notes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lead_id = table.Column<Guid>(type: "uuid", nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    author_id = table.Column<Guid>(type: "uuid", nullable: false),
                    author_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lead_notes", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_lead_notes_tenant_id_lead_id_created_at",
                table: "lead_notes",
                columns: new[] { "tenant_id", "lead_id", "created_at" },
                descending: new[] { false, false, true });

            migrationBuilder.AddColumn<string>(
                name: "reschedule_reason",
                table: "activities",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "remarks",
                table: "activities",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "next_activity_id",
                table: "activities",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "lead_notes");

            migrationBuilder.DropColumn(
                name: "reschedule_reason",
                table: "activities");

            migrationBuilder.DropColumn(
                name: "remarks",
                table: "activities");

            migrationBuilder.DropColumn(
                name: "next_activity_id",
                table: "activities");
        }
    }
}

