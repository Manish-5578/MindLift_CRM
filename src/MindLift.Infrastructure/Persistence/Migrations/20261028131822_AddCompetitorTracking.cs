using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MindLift.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCompetitorTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "competitors",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    website = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    strengths = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    weaknesses = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    battlecard = table.Column<string>(type: "text", nullable: true),
                    pricing_strategy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("pk_competitors", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "deal_competitors",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    deal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    competitor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    threat_level = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    strengths_observed = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    weaknesses_observed = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
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
                    table.PrimaryKey("pk_deal_competitors", x => x.id);
                    table.ForeignKey(
                        name: "fk_deal_competitors_competitors_competitor_id",
                        column: x => x.competitor_id,
                        principalTable: "competitors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_deal_competitors_deals_deal_id",
                        column: x => x.deal_id,
                        principalTable: "deals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_competitors_tenant_id",
                table: "competitors",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_competitors_tenant_id_name",
                table: "competitors",
                columns: new[] { "tenant_id", "name" });

            migrationBuilder.CreateIndex(
                name: "ix_deal_competitors_competitor_id",
                table: "deal_competitors",
                column: "competitor_id");

            migrationBuilder.CreateIndex(
                name: "ix_deal_competitors_deal_id",
                table: "deal_competitors",
                column: "deal_id");

            migrationBuilder.CreateIndex(
                name: "ix_deal_competitors_tenant_id",
                table: "deal_competitors",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_deal_competitors_tenant_id_competitor_id",
                table: "deal_competitors",
                columns: new[] { "tenant_id", "competitor_id" });

            migrationBuilder.CreateIndex(
                name: "ix_deal_competitors_tenant_id_deal_id",
                table: "deal_competitors",
                columns: new[] { "tenant_id", "deal_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "deal_competitors");

            migrationBuilder.DropTable(
                name: "competitors");
        }
    }
}
