using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nosum.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InventoryFarmQuests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "farm_plots",
                columns: table => new
                {
                    map_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    x = table.Column<int>(type: "integer", nullable: false),
                    y = table.Column<int>(type: "integer", nullable: false),
                    crop_id = table.Column<int>(type: "integer", nullable: false),
                    planted_at_ms = table.Column<long>(type: "bigint", nullable: false),
                    growth_ms = table.Column<long>(type: "bigint", nullable: false),
                    settled_at_ms = table.Column<long>(type: "bigint", nullable: false),
                    wet_until_ms = table.Column<long>(type: "bigint", nullable: false),
                    last_worked_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_farm_plots", x => new { x.map_id, x.x, x.y });
                });

            migrationBuilder.CreateTable(
                name: "inventory_log",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<int>(type: "integer", nullable: false),
                    delta = table.Column<int>(type: "integer", nullable: false),
                    reason = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inventory_log", x => x.id);
                    table.ForeignKey(
                        name: "fk_inventory_log_players_player_id",
                        column: x => x.player_id,
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "player_inventories",
                columns: table => new
                {
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    capacity = table.Column<int>(type: "integer", nullable: false),
                    selected_slot = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_player_inventories", x => x.player_id);
                    table.ForeignKey(
                        name: "fk_player_inventories_players_player_id",
                        column: x => x.player_id,
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "player_quests",
                columns: table => new
                {
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quest_id = table.Column<int>(type: "integer", nullable: false),
                    state = table.Column<short>(type: "smallint", nullable: false),
                    progress = table.Column<int[]>(type: "integer[]", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_player_quests", x => new { x.player_id, x.quest_id });
                    table.ForeignKey(
                        name: "fk_player_quests_players_player_id",
                        column: x => x.player_id,
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "inventory_slots",
                columns: table => new
                {
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    slot = table.Column<int>(type: "integer", nullable: false),
                    item_id = table.Column<int>(type: "integer", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inventory_slots", x => new { x.player_id, x.slot });
                    table.CheckConstraint("ck_inventory_slots_quantity", "quantity > 0");
                    table.ForeignKey(
                        name: "fk_inventory_slots_player_inventories_player_id",
                        column: x => x.player_id,
                        principalTable: "player_inventories",
                        principalColumn: "player_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_inventory_log_player_id_occurred_at",
                table: "inventory_log",
                columns: new[] { "player_id", "occurred_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "farm_plots");

            migrationBuilder.DropTable(
                name: "inventory_log");

            migrationBuilder.DropTable(
                name: "inventory_slots");

            migrationBuilder.DropTable(
                name: "player_quests");

            migrationBuilder.DropTable(
                name: "player_inventories");
        }
    }
}
