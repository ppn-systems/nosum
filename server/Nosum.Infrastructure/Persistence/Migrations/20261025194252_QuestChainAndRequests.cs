using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nosum.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class QuestChainAndRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "daily_requests",
                columns: table => new
                {
                    day = table.Column<int>(type: "integer", nullable: false),
                    slot = table.Column<int>(type: "integer", nullable: false),
                    item_id = table.Column<int>(type: "integer", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    reward_coins = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_daily_requests", x => new { x.day, x.slot });
                    table.CheckConstraint("ck_daily_requests_amounts", "quantity > 0 AND reward_coins >= 0");
                });

            migrationBuilder.CreateTable(
                name: "request_fills",
                columns: table => new
                {
                    day = table.Column<int>(type: "integer", nullable: false),
                    slot = table.Column<int>(type: "integer", nullable: false),
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reward_coins = table.Column<long>(type: "bigint", nullable: false),
                    filled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_request_fills", x => new { x.day, x.slot, x.player_id });
                    table.ForeignKey(
                        name: "fk_request_fills_daily_requests_day_slot",
                        columns: x => new { x.day, x.slot },
                        principalTable: "daily_requests",
                        principalColumns: new[] { "day", "slot" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_request_fills_players_player_id",
                        column: x => x.player_id,
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_request_fills_player_id",
                table: "request_fills",
                column: "player_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "request_fills");

            migrationBuilder.DropTable(
                name: "daily_requests");
        }
    }
}
