using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nosum.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GrowthInGameDays : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "growth_ms",
                table: "farm_plots");

            migrationBuilder.DropColumn(
                name: "settled_at_ms",
                table: "farm_plots");

            migrationBuilder.DropColumn(
                name: "wet_until_ms",
                table: "farm_plots");

            migrationBuilder.AddColumn<int>(
                name: "growth_days",
                table: "farm_plots",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "tended_day",
                table: "farm_plots",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "watered_day",
                table: "farm_plots",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "growth_days",
                table: "farm_plots");

            migrationBuilder.DropColumn(
                name: "tended_day",
                table: "farm_plots");

            migrationBuilder.DropColumn(
                name: "watered_day",
                table: "farm_plots");

            migrationBuilder.AddColumn<long>(
                name: "growth_ms",
                table: "farm_plots",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "settled_at_ms",
                table: "farm_plots",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "wet_until_ms",
                table: "farm_plots",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }
    }
}
