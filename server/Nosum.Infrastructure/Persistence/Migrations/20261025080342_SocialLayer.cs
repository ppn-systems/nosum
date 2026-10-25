using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nosum.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SocialLayer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "coin_log",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    delta = table.Column<long>(type: "bigint", nullable: false),
                    reason = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_coin_log", x => x.id);
                    table.ForeignKey(
                        name: "fk_coin_log_players_player_id",
                        column: x => x.player_id,
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "coops",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_coops", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "market_listings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    seller_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<int>(type: "integer", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    price = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    buyer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_market_listings", x => x.id);
                    table.CheckConstraint("ck_market_listings_amounts", "quantity > 0 AND price > 0");
                    table.ForeignKey(
                        name: "fk_market_listings_players_seller_id",
                        column: x => x.seller_id,
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "player_appearances",
                columns: table => new
                {
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    skin_id = table.Column<int>(type: "integer", nullable: false),
                    hair_style_id = table.Column<int>(type: "integer", nullable: false),
                    hair_dye_id = table.Column<int>(type: "integer", nullable: false),
                    shirt_item_id = table.Column<int>(type: "integer", nullable: false),
                    pants_item_id = table.Column<int>(type: "integer", nullable: false),
                    hat_item_id = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_player_appearances", x => x.player_id);
                    table.ForeignKey(
                        name: "fk_player_appearances_players_player_id",
                        column: x => x.player_id,
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "player_wallets",
                columns: table => new
                {
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    coins = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_player_wallets", x => x.player_id);
                    table.CheckConstraint("ck_player_wallets_coins", "coins >= 0");
                    table.ForeignKey(
                        name: "fk_player_wallets_players_player_id",
                        column: x => x.player_id,
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "coop_invites",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    coop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_coop_invites", x => x.id);
                    table.ForeignKey(
                        name: "fk_coop_invites_coops_coop_id",
                        column: x => x.coop_id,
                        principalTable: "coops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_coop_invites_players_player_id",
                        column: x => x.player_id,
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "coop_log",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    coop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<int>(type: "integer", nullable: false),
                    delta = table.Column<int>(type: "integer", nullable: false),
                    reason = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_coop_log", x => x.id);
                    table.ForeignKey(
                        name: "fk_coop_log_coops_coop_id",
                        column: x => x.coop_id,
                        principalTable: "coops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "coop_members",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    coop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<byte>(type: "smallint", nullable: false),
                    joined_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_coop_members", x => x.id);
                    table.ForeignKey(
                        name: "fk_coop_members_coops_coop_id",
                        column: x => x.coop_id,
                        principalTable: "coops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_coop_members_players_player_id",
                        column: x => x.player_id,
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "coop_stock",
                columns: table => new
                {
                    coop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<int>(type: "integer", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_coop_stock", x => new { x.coop_id, x.item_id });
                    table.CheckConstraint("ck_coop_stock_quantity", "quantity > 0");
                    table.ForeignKey(
                        name: "fk_coop_stock_coops_coop_id",
                        column: x => x.coop_id,
                        principalTable: "coops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_coin_log_player_id_occurred_at",
                table: "coin_log",
                columns: new[] { "player_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_coop_invites_coop_id_player_id",
                table: "coop_invites",
                columns: new[] { "coop_id", "player_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_coop_invites_player_id",
                table: "coop_invites",
                column: "player_id");

            migrationBuilder.CreateIndex(
                name: "ix_coop_log_coop_id_occurred_at",
                table: "coop_log",
                columns: new[] { "coop_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_coop_members_coop_id",
                table: "coop_members",
                column: "coop_id");

            migrationBuilder.CreateIndex(
                name: "ix_coop_members_player_id",
                table: "coop_members",
                column: "player_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_market_listings_seller_id_status",
                table: "market_listings",
                columns: new[] { "seller_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_market_listings_status_expires_at",
                table: "market_listings",
                columns: new[] { "status", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "ix_market_listings_status_item_id_created_at",
                table: "market_listings",
                columns: new[] { "status", "item_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "coin_log");

            migrationBuilder.DropTable(
                name: "coop_invites");

            migrationBuilder.DropTable(
                name: "coop_log");

            migrationBuilder.DropTable(
                name: "coop_members");

            migrationBuilder.DropTable(
                name: "coop_stock");

            migrationBuilder.DropTable(
                name: "market_listings");

            migrationBuilder.DropTable(
                name: "player_appearances");

            migrationBuilder.DropTable(
                name: "player_wallets");

            migrationBuilder.DropTable(
                name: "coops");
        }
    }
}
