using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MindLift.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDealForecastCategory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "forecast_category",
                table: "deals",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Pipeline");

            migrationBuilder.CreateIndex(
                name: "ix_deals_tenant_id_forecast_category",
                table: "deals",
                columns: new[] { "tenant_id", "forecast_category" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_deals_tenant_id_forecast_category",
                table: "deals");

            migrationBuilder.DropColumn(
                name: "forecast_category",
                table: "deals");
        }
    }
}
