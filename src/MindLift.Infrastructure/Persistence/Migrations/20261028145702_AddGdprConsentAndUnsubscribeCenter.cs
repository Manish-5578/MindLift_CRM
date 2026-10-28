using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MindLift.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGdprConsentAndUnsubscribeCenter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "gdpr_consent_records",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    channel = table.Column<int>(type: "integer", nullable: false),
                    topic = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, defaultValue: "Marketing"),
                    status = table.Column<int>(type: "integer", nullable: false),
                    lawful_basis = table.Column<int>(type: "integer", nullable: false),
                    source = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ip_address = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    user_agent = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    effective_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
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
                    table.PrimaryKey("pk_gdpr_consent_records", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "gdpr_erasure_records",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    requested_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "Completed"),
                    records_erased = table.Column<int>(type: "integer", nullable: false),
                    summary = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    executed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
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
                    table.PrimaryKey("pk_gdpr_erasure_records", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "subscription_preference_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    token = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    lead_id = table.Column<Guid>(type: "uuid", nullable: true),
                    contact_id = table.Column<Guid>(type: "uuid", nullable: true),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_used_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_revoked = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("pk_subscription_preference_tokens", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_gdpr_consent_records_tenant_id",
                table: "gdpr_consent_records",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_gdpr_consent_records_tenant_id_effective_at",
                table: "gdpr_consent_records",
                columns: new[] { "tenant_id", "effective_at" });

            migrationBuilder.CreateIndex(
                name: "ix_gdpr_consent_records_tenant_id_email_channel_topic",
                table: "gdpr_consent_records",
                columns: new[] { "tenant_id", "email", "channel", "topic" });

            migrationBuilder.CreateIndex(
                name: "ix_gdpr_erasure_records_tenant_id",
                table: "gdpr_erasure_records",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_gdpr_erasure_records_tenant_id_executed_at",
                table: "gdpr_erasure_records",
                columns: new[] { "tenant_id", "executed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_subscription_preference_tokens_tenant_id",
                table: "subscription_preference_tokens",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_subscription_preference_tokens_tenant_id_email",
                table: "subscription_preference_tokens",
                columns: new[] { "tenant_id", "email" });

            migrationBuilder.CreateIndex(
                name: "ix_subscription_preference_tokens_token",
                table: "subscription_preference_tokens",
                column: "token",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "gdpr_consent_records");

            migrationBuilder.DropTable(
                name: "gdpr_erasure_records");

            migrationBuilder.DropTable(
                name: "subscription_preference_tokens");
        }
    }
}
