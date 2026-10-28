using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MindLift.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesCadences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cadences",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    target_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "Lead"),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "Draft"),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: true),
                    active_enrollment_count = table.Column<int>(type: "integer", nullable: false),
                    completed_enrollment_count = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("pk_cadences", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "cadence_enrollments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    cadence_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    target_id = table.Column<Guid>(type: "uuid", nullable: false),
                    current_step_number = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "Active"),
                    enrolled_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    next_step_due_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
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
                    table.PrimaryKey("pk_cadence_enrollments", x => x.id);
                    table.ForeignKey(
                        name: "fk_cadence_enrollments_cadences_cadence_id",
                        column: x => x.cadence_id,
                        principalTable: "cadences",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "cadence_steps",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    cadence_id = table.Column<Guid>(type: "uuid", nullable: false),
                    step_number = table.Column<int>(type: "integer", nullable: false),
                    step_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "Email"),
                    wait_days = table.Column<int>(type: "integer", nullable: false),
                    subject = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    content = table.Column<string>(type: "text", nullable: true),
                    action_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "ManualEmail"),
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
                    table.PrimaryKey("pk_cadence_steps", x => x.id);
                    table.ForeignKey(
                        name: "fk_cadence_steps_cadences_cadence_id",
                        column: x => x.cadence_id,
                        principalTable: "cadences",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "cadence_step_executions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    enrollment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    step_id = table.Column<Guid>(type: "uuid", nullable: true),
                    step_number = table.Column<int>(type: "integer", nullable: false),
                    executed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    result = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "Completed"),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    executed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("pk_cadence_step_executions", x => x.id);
                    table.ForeignKey(
                        name: "fk_cadence_step_executions_cadence_enrollments_enrollment_id",
                        column: x => x.enrollment_id,
                        principalTable: "cadence_enrollments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_cadence_enrollments_cadence_id",
                table: "cadence_enrollments",
                column: "cadence_id");

            migrationBuilder.CreateIndex(
                name: "ix_cadence_enrollments_tenant_id",
                table: "cadence_enrollments",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_cadence_enrollments_tenant_id_cadence_id_status",
                table: "cadence_enrollments",
                columns: new[] { "tenant_id", "cadence_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_cadence_enrollments_tenant_id_next_step_due_at",
                table: "cadence_enrollments",
                columns: new[] { "tenant_id", "next_step_due_at" });

            migrationBuilder.CreateIndex(
                name: "ix_cadence_enrollments_tenant_id_target_type_target_id",
                table: "cadence_enrollments",
                columns: new[] { "tenant_id", "target_type", "target_id" });

            migrationBuilder.CreateIndex(
                name: "ix_cadence_step_executions_enrollment_id",
                table: "cadence_step_executions",
                column: "enrollment_id");

            migrationBuilder.CreateIndex(
                name: "ix_cadence_step_executions_tenant_id",
                table: "cadence_step_executions",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_cadence_step_executions_tenant_id_enrollment_id_step_number",
                table: "cadence_step_executions",
                columns: new[] { "tenant_id", "enrollment_id", "step_number" });

            migrationBuilder.CreateIndex(
                name: "ix_cadence_steps_cadence_id",
                table: "cadence_steps",
                column: "cadence_id");

            migrationBuilder.CreateIndex(
                name: "ix_cadence_steps_tenant_id",
                table: "cadence_steps",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_cadence_steps_tenant_id_cadence_id_step_number",
                table: "cadence_steps",
                columns: new[] { "tenant_id", "cadence_id", "step_number" });

            migrationBuilder.CreateIndex(
                name: "ix_cadences_tenant_id",
                table: "cadences",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_cadences_tenant_id_status",
                table: "cadences",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_cadences_tenant_id_target_type",
                table: "cadences",
                columns: new[] { "tenant_id", "target_type" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cadence_step_executions");

            migrationBuilder.DropTable(
                name: "cadence_steps");

            migrationBuilder.DropTable(
                name: "cadence_enrollments");

            migrationBuilder.DropTable(
                name: "cadences");
        }
    }
}
