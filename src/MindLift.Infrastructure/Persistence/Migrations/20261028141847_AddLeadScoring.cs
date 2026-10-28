using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MindLift.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLeadScoring : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "last_scored_at",
                table: "leads",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "score",
                table: "leads",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "score_grade",
                table: "leads",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "lead_score_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    lead_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rule_id = table.Column<Guid>(type: "uuid", nullable: true),
                    rule_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    points = table.Column<int>(type: "integer", nullable: false),
                    running_score = table.Column<int>(type: "integer", nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    scored_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
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
                    table.PrimaryKey("pk_lead_score_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "lead_scoring_rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    rule_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "Demographic"),
                    points = table.Column<int>(type: "integer", nullable: false),
                    condition_field = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    @operator = table.Column<string>(name: "operator", type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "Equals"),
                    condition_value = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    order = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("pk_lead_scoring_rules", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_lead_score_logs_tenant_id",
                table: "lead_score_logs",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_lead_score_logs_tenant_id_lead_id_scored_at",
                table: "lead_score_logs",
                columns: new[] { "tenant_id", "lead_id", "scored_at" });

            migrationBuilder.CreateIndex(
                name: "ix_lead_scoring_rules_tenant_id",
                table: "lead_scoring_rules",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_lead_scoring_rules_tenant_id_is_active_order",
                table: "lead_scoring_rules",
                columns: new[] { "tenant_id", "is_active", "order" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "lead_score_logs");

            migrationBuilder.DropTable(
                name: "lead_scoring_rules");

            migrationBuilder.DropColumn(
                name: "last_scored_at",
                table: "leads");

            migrationBuilder.DropColumn(
                name: "score",
                table: "leads");

            migrationBuilder.DropColumn(
                name: "score_grade",
                table: "leads");
        }
    }
}
