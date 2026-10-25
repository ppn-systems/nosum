using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nosum.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EnergySkills : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "energy",
                table: "player_states",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "energy_refilled_day",
                table: "player_states",
                type: "bigint",
                nullable: false,
                defaultValue: -1L);

            migrationBuilder.AddColumn<int>(
                name: "farming_xp",
                table: "player_states",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "foraging_xp",
                table: "player_states",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "mining_xp",
                table: "player_states",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "energy",
                table: "player_states");

            migrationBuilder.DropColumn(
                name: "energy_refilled_day",
                table: "player_states");

            migrationBuilder.DropColumn(
                name: "farming_xp",
                table: "player_states");

            migrationBuilder.DropColumn(
                name: "foraging_xp",
                table: "player_states");

            migrationBuilder.DropColumn(
                name: "mining_xp",
                table: "player_states");
        }
    }
}
