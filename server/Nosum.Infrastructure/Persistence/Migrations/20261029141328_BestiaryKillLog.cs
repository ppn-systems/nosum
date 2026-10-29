using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nosum.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BestiaryKillLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "equipped_bestiary_titles",
                columns: table => new
                {
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    species = table.Column<byte>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_equipped_bestiary_titles", x => x.player_id);
                    table.CheckConstraint("ck_equipped_bestiary_titles_species", "species > 0");
                    table.ForeignKey(
                        name: "fk_equipped_bestiary_titles_players_player_id",
                        column: x => x.player_id,
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "player_bestiary",
                columns: table => new
                {
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    species = table.Column<byte>(type: "smallint", nullable: false),
                    kills = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_player_bestiary", x => new { x.player_id, x.species });
                    table.CheckConstraint("ck_player_bestiary_kills_positive", "kills > 0");
                    table.ForeignKey(
                        name: "fk_player_bestiary_players_player_id",
                        column: x => x.player_id,
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "equipped_bestiary_titles");

            migrationBuilder.DropTable(
                name: "player_bestiary");
        }
    }
}
