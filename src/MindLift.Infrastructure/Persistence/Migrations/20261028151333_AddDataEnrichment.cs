using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MindLift.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDataEnrichment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "enrichment_configs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    auto_enrich_new_leads = table.Column<bool>(type: "boolean", nullable: false),
                    auto_enrich_new_accounts = table.Column<bool>(type: "boolean", nullable: false),
                    overwrite_existing_fields = table.Column<bool>(type: "boolean", nullable: false),
                    default_provider = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, defaultValue: "MindLift Intelligence"),
                    excluded_domains = table.Column<string[]>(type: "text[]", nullable: false),
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
                    table.PrimaryKey("pk_enrichment_configs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "enrichment_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    entity_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "Lead"),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    domain = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "Enriched"),
                    provider = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    enriched_data_json = table.Column<string>(type: "jsonb", nullable: false),
                    fields_updated = table.Column<string[]>(type: "text[]", nullable: false),
                    confidence_score = table.Column<double>(type: "double precision", nullable: false),
                    enriched_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
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
                    table.PrimaryKey("pk_enrichment_logs", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_enrichment_configs_tenant_id",
                table: "enrichment_configs",
                column: "tenant_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_enrichment_logs_tenant_id",
                table: "enrichment_logs",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_enrichment_logs_tenant_id_domain",
                table: "enrichment_logs",
                columns: new[] { "tenant_id", "domain" });

            migrationBuilder.CreateIndex(
                name: "ix_enrichment_logs_tenant_id_enriched_at",
                table: "enrichment_logs",
                columns: new[] { "tenant_id", "enriched_at" });

            migrationBuilder.CreateIndex(
                name: "ix_enrichment_logs_tenant_id_entity_type_entity_id",
                table: "enrichment_logs",
                columns: new[] { "tenant_id", "entity_type", "entity_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "enrichment_configs");

            migrationBuilder.DropTable(
                name: "enrichment_logs");
        }
    }
}
