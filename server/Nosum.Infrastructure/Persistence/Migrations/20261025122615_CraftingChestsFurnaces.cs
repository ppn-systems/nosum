using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nosum.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CraftingChestsFurnaces : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_player_inventories_players_player_id",
                table: "player_inventories");

            migrationBuilder.CreateTable(
                name: "chest_log",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    object_id = table.Column<int>(type: "integer", nullable: false),
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<int>(type: "integer", nullable: false),
                    delta = table.Column<int>(type: "integer", nullable: false),
                    reason = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chest_log", x => x.id);
                    table.ForeignKey(
                        name: "fk_chest_log_players_player_id",
                        column: x => x.player_id,
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "furnace_jobs",
                columns: table => new
                {
                    object_id = table.Column<int>(type: "integer", nullable: false),
                    ore_item_id = table.Column<int>(type: "integer", nullable: false),
                    started_at_ms = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_furnace_jobs", x => x.object_id);
                    table.ForeignKey(
                        name: "fk_furnace_jobs_placed_objects_object_id",
                        column: x => x.object_id,
                        principalTable: "placed_objects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_chest_log_object_id_occurred_at",
                table: "chest_log",
                columns: new[] { "object_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_chest_log_player_id",
                table: "chest_log",
                column: "player_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "chest_log");

            migrationBuilder.DropTable(
                name: "furnace_jobs");

            migrationBuilder.AddForeignKey(
                name: "fk_player_inventories_players_player_id",
                table: "player_inventories",
                column: "player_id",
                principalTable: "players",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
