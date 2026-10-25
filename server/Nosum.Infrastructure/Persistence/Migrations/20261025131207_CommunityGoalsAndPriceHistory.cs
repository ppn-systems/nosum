using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nosum.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CommunityGoalsAndPriceHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "community_goals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    goal_id = table.Column<int>(type: "integer", nullable: false),
                    season_number = table.Column<int>(type: "integer", nullable: false),
                    total = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_community_goals", x => x.id);
                    table.CheckConstraint("ck_community_goals_total", "total >= 0");
                });

            migrationBuilder.CreateTable(
                name: "community_unlocks",
                columns: table => new
                {
                    key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    goal_id = table.Column<int>(type: "integer", nullable: false),
                    unlocked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_community_unlocks", x => x.key);
                });

            migrationBuilder.CreateTable(
                name: "market_sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<int>(type: "integer", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    price = table.Column<long>(type: "bigint", nullable: false),
                    unit_price = table.Column<long>(type: "bigint", nullable: false),
                    day = table.Column<int>(type: "integer", nullable: false),
                    sold_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_market_sales", x => x.id);
                    table.CheckConstraint("ck_market_sales_amounts", "quantity > 0 AND price > 0 AND unit_price > 0");
                });

            migrationBuilder.CreateTable(
                name: "community_contributions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    progress_id = table.Column<Guid>(type: "uuid", nullable: false),
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    delivered = table.Column<long>(type: "bigint", nullable: false),
                    paid_coins = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_community_contributions", x => x.id);
                    table.CheckConstraint("ck_community_contributions_delivered", "delivered > 0");
                    table.ForeignKey(
                        name: "fk_community_contributions_community_goals_progress_id",
                        column: x => x.progress_id,
                        principalTable: "community_goals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_community_contributions_players_player_id",
                        column: x => x.player_id,
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_community_contributions_player_id",
                table: "community_contributions",
                column: "player_id");

            migrationBuilder.CreateIndex(
                name: "ix_community_contributions_progress_id_player_id",
                table: "community_contributions",
                columns: new[] { "progress_id", "player_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_community_goals_goal_id_season_number",
                table: "community_goals",
                columns: new[] { "goal_id", "season_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_community_goals_status",
                table: "community_goals",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_market_sales_item_id_day",
                table: "market_sales",
                columns: new[] { "item_id", "day" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "community_contributions");

            migrationBuilder.DropTable(
                name: "community_unlocks");

            migrationBuilder.DropTable(
                name: "market_sales");

            migrationBuilder.DropTable(
                name: "community_goals");
        }
    }
}
