using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nosum.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CoopUpgrades : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "land_level",
                table: "coops",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "warehouse_level",
                table: "coops",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "land_level",
                table: "coops");

            migrationBuilder.DropColumn(
                name: "warehouse_level",
                table: "coops");
        }
    }
}
