using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nosum.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PlayerCombatVitals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "combat_xp",
                table: "player_states",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "hp",
                table: "player_states",
                type: "integer",
                nullable: false,
                defaultValue: 100);

            migrationBuilder.AddColumn<int>(
                name: "mp",
                table: "player_states",
                type: "integer",
                nullable: false,
                defaultValue: 50);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "combat_xp",
                table: "player_states");

            migrationBuilder.DropColumn(
                name: "hp",
                table: "player_states");

            migrationBuilder.DropColumn(
                name: "mp",
                table: "player_states");
        }
    }
}
