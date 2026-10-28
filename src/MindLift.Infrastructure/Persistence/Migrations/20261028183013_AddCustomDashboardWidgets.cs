using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MindLift.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomDashboardWidgets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "custom_dashboard_widgets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    widget_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    source = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    metric_aggregation = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    target_field = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    group_by_field = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    filter_json = table.Column<string>(type: "text", nullable: true),
                    size = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    is_shared = table.Column<bool>(type: "boolean", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("pk_custom_dashboard_widgets", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_custom_dashboard_widgets_tenant_id",
                table: "custom_dashboard_widgets",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_custom_dashboard_widgets_tenant_id_created_by_user_id",
                table: "custom_dashboard_widgets",
                columns: new[] { "tenant_id", "created_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_custom_dashboard_widgets_tenant_id_is_shared",
                table: "custom_dashboard_widgets",
                columns: new[] { "tenant_id", "is_shared" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "custom_dashboard_widgets");
        }
    }
}
