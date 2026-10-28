using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MindLift.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddVisitorTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "visitor_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tracker_id = table.Column<Guid>(type: "uuid", nullable: false),
                    visitor_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    session_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    lead_id = table.Column<Guid>(type: "uuid", nullable: true),
                    contact_id = table.Column<Guid>(type: "uuid", nullable: true),
                    identified_email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    first_seen_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    page_views_count = table.Column<int>(type: "integer", nullable: false),
                    total_duration_seconds = table.Column<int>(type: "integer", nullable: false),
                    ip_address = table.Column<string>(type: "text", nullable: true),
                    user_agent = table.Column<string>(type: "text", nullable: true),
                    country = table.Column<string>(type: "text", nullable: true),
                    city = table.Column<string>(type: "text", nullable: true),
                    initial_referrer = table.Column<string>(type: "text", nullable: true),
                    utm_source = table.Column<string>(type: "text", nullable: true),
                    utm_medium = table.Column<string>(type: "text", nullable: true),
                    utm_campaign = table.Column<string>(type: "text", nullable: true),
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
                    table.PrimaryKey("pk_visitor_sessions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "website_trackers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    domain = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    tracking_token = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    auto_create_leads = table.Column<bool>(type: "boolean", nullable: false),
                    allowed_origins = table.Column<string[]>(type: "text[]", nullable: false),
                    total_visits = table.Column<int>(type: "integer", nullable: false),
                    total_page_views = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("pk_website_trackers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "visitor_page_views",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    visitor_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    lead_id = table.Column<Guid>(type: "uuid", nullable: true),
                    url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    referrer = table.Column<string>(type: "text", nullable: true),
                    duration_seconds = table.Column<int>(type: "integer", nullable: false),
                    viewed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
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
                    table.PrimaryKey("pk_visitor_page_views", x => x.id);
                    table.ForeignKey(
                        name: "fk_visitor_page_views_visitor_sessions_session_id",
                        column: x => x.session_id,
                        principalTable: "visitor_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_visitor_page_views_session_id",
                table: "visitor_page_views",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "ix_visitor_page_views_tenant_id",
                table: "visitor_page_views",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_visitor_page_views_tenant_id_lead_id",
                table: "visitor_page_views",
                columns: new[] { "tenant_id", "lead_id" });

            migrationBuilder.CreateIndex(
                name: "ix_visitor_page_views_tenant_id_session_id",
                table: "visitor_page_views",
                columns: new[] { "tenant_id", "session_id" });

            migrationBuilder.CreateIndex(
                name: "ix_visitor_page_views_tenant_id_viewed_at",
                table: "visitor_page_views",
                columns: new[] { "tenant_id", "viewed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_visitor_sessions_tenant_id",
                table: "visitor_sessions",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_visitor_sessions_tenant_id_last_seen_at",
                table: "visitor_sessions",
                columns: new[] { "tenant_id", "last_seen_at" });

            migrationBuilder.CreateIndex(
                name: "ix_visitor_sessions_tenant_id_lead_id",
                table: "visitor_sessions",
                columns: new[] { "tenant_id", "lead_id" });

            migrationBuilder.CreateIndex(
                name: "ix_visitor_sessions_tenant_id_visitor_id",
                table: "visitor_sessions",
                columns: new[] { "tenant_id", "visitor_id" });

            migrationBuilder.CreateIndex(
                name: "ix_website_trackers_tenant_id",
                table: "website_trackers",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_website_trackers_tenant_id_is_active",
                table: "website_trackers",
                columns: new[] { "tenant_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_website_trackers_tracking_token",
                table: "website_trackers",
                column: "tracking_token",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "visitor_page_views");

            migrationBuilder.DropTable(
                name: "website_trackers");

            migrationBuilder.DropTable(
                name: "visitor_sessions");
        }
    }
}
